using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MonitorPilot;

// Matches all displays to a reference from one phone photo per round: show a gray patch everywhere, photograph
// all screens at once, click each screen once, then apply the computed brightness/RGB/dimming step.
internal sealed class PhotoMatchWindow : Window
{
    private readonly AppController c;
    private readonly PhotoUploadServer server=new();
    private readonly List<Window> patches=new();
    private readonly Dictionary<string,Point> positions=new();
    private readonly ComboBox reference=new(){MinWidth=260};
    private readonly Grid photo=new();
    private readonly Image image=new(){Stretch=Stretch.Fill};
    private readonly Canvas markers=new();
    private readonly TextBlock prompt=Theme.Label("",13,null,true);
    private readonly StackPanel results=new();
    private readonly TextBlock status=Theme.Label("",14,Theme.Muted,true);
    private readonly Button apply;
    private readonly Button undo;
    private readonly CheckBox colors=new(){Content=L.T("Farben angleichen (aus: nur Helligkeit)"),IsChecked=true,Margin=new Thickness(0,2,0,8)};
    // Each round then only needs a photo: apply, show the test image again, wait for the next upload.
    private readonly CheckBox autoApply=new(){Content=L.T("Nach jedem Foto automatisch anwenden"),IsChecked=true,Margin=new Thickness(0,0,0,8)};
    private readonly TextBlock hint=Theme.Label("",12,Theme.Muted);
    private readonly Button useHint;
    private readonly Dictionary<string,(int? Brightness,int? Red,int? Green,int? Blue,int Dimming)> original=new();
    private string? central;
    // Set once a suggestion is applied: the current photo no longer shows the screens as they are.
    private bool stale;
    // Set when positions were clicked or dragged by hand; later photos then keep them instead of detecting again.
    private bool manual;
    private string? dragging;
    private bool referenceChosen,choosingReference,done,warned;
    // Camera noise keeps a few percent of spread; after this many rounds the best state so far is kept.
    private const int MaxRounds=10;
    private readonly MatchCalibration? matchBefore;
    private sealed record Choice(MonitorDevice Monitor,string Text){public override string ToString()=>Text;}
    private readonly TextBlock empty;
    private BitmapSource? current;
    private List<MatchPlan> plans=new();
    private int round;
    private List<MonitorDevice> Active=>c.Monitors.Where(m=>m.Active).OrderBy(m=>m.Number).ToList();
    public PhotoMatchWindow(AppController controller)
    {
        c=controller;matchBefore=c.Settings.Match;Title=L.T("MonitorPilot · Per Foto angleichen");Theme.Decorate(this);
        Theme.FitToWorkArea(this,SystemParameters.WorkArea,1320,860);WindowStartupLocation=WindowStartupLocation.CenterScreen;AllowDrop=true;
        var choices=Active.Select(m=>new Choice(m,$"{m} · {Where(m)}")).ToList();reference.ItemsSource=choices;
        reference.SelectedItem=choices.FirstOrDefault(x=>x.Monitor==c.Main)??choices.FirstOrDefault();
        reference.SelectionChanged+=(_,_)=>{if(!choosingReference)referenceChosen=true;Measure();};
        // Big numbers on every screen while choosing, so the list entries can be matched to the desk.
        reference.DropDownOpened+=(_,_)=>c.Overlays.Identify(c.Monitors);
        apply=Theme.Button(L.T("Vorschlag anwenden"),()=>_=Apply(),true);apply.IsEnabled=false;
        undo=Theme.Button(L.T("Rückgängig"),()=>_=Undo());undo.IsEnabled=false;
        useHint=Theme.Button(L.T("Als Referenz nehmen"),()=>Choose(central));
        var hintRow=new DockPanel{Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(useHint,Dock.Right);useHint.VerticalAlignment=VerticalAlignment.Top;useHint.Margin=new Thickness(8,0,0,0);hintRow.Children.Add(useHint);hintRow.Children.Add(hint);hintRow.Visibility=Visibility.Collapsed;
        colors.Click+=(_,_)=>Measure();
        var root=new Grid{Margin=new Thickness(20)};root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});root.ColumnDefinitions.Add(new(){Width=new GridLength(16)});root.ColumnDefinitions.Add(new(){Width=new GridLength(380)});
        photo.Children.Add(image);photo.Children.Add(markers);photo.MouseLeftButtonDown+=PhotoDown;photo.MouseMove+=PhotoMove;photo.MouseLeftButtonUp+=PhotoUp;photo.Cursor=Cursors.Cross;
        empty=Theme.Label(L.T("Noch kein Foto. Testbild zeigen, dann mit dem Handy alle Bildschirme fotografieren."),14,Theme.Muted);empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;empty.TextAlignment=TextAlignment.Center;empty.MaxWidth=420;
        var stage=new Grid();stage.Children.Add(empty);stage.Children.Add(new Viewbox{Stretch=Stretch.Uniform,Child=photo});
        var left=new DockPanel();DockPanel.SetDock(prompt,Dock.Top);left.Children.Add(prompt);left.Children.Add(new Border{Background=Theme.Map,BorderBrush=Theme.Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(10),Child=stage});
        root.Children.Add(left);
        var urls=server.Running?string.Join(Environment.NewLine,server.Urls):L.T("Upload-Seite konnte nicht starten. Foto per Datei oder Zwischenablage laden.");
        var urlBox=new TextBox{Text=urls,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,4,0,6)};
        var start=Theme.Button(L.T("Start: Testbild zeigen"),ShowPatches,true);start.HorizontalAlignment=HorizontalAlignment.Stretch;start.MinHeight=44;start.Margin=new Thickness(0,4,0,10);
        var more=Theme.Stack(
            Theme.Label(L.T("Referenzmonitor"),12,null,true),reference,hintRow,colors,autoApply,
            Theme.Row(Theme.Button(L.T("Foto öffnen …"),OpenFile),Theme.Button(L.T("Aus Zwischenablage"),Paste),Theme.Button(L.T("Automatisch erkennen"),Redetect),Theme.Button(L.T("Positionen neu setzen"),ResetPositions)),
            Theme.Row(apply,Theme.Button(L.T("Als Profil speichern"),()=>_=c.Run(()=>c.SaveProfile(L.F($"Angeglichen {DateTime.Now:dd.MM. HH:mm}"))))));
        more.Visibility=Visibility.Collapsed;
        Button? toggle=null;toggle=Theme.Button(L.T("Erweitert ▾"),()=>{bool open=more.Visibility!=Visibility.Visible;more.Visibility=open?Visibility.Visible:Visibility.Collapsed;toggle!.Content=open?L.T("Erweitert ▴"):L.T("Erweitert ▾");});
        var side=Theme.Stack(
            Theme.Label(L.T("Per Foto angleichen"),20,null,true),
            Theme.Label(L.T("1. Start klicken: Alle Bildschirme werden grau mit großer Nummer."),13),
            Theme.Label(L.T("2. Am Handy die Adresse öffnen und ein Foto aller Bildschirme machen. Das Handy zeigt, wie es weitergeht."),13),
            Theme.Label(L.T("3. Weitere Fotos machen, bis das Handy „Fertig“ meldet. Das Ergebnis wird automatisch gespeichert."),13),
            Theme.Label(L.T("Vorher an allen Monitoren denselben Bildmodus wählen; dynamischen Kontrast, Blaulichtfilter und Eco-Modus aus."),12,Theme.Muted),
            start,
            Theme.Label(L.T("Adresse fürs Handy (gleiches WLAN, als Lesezeichen speichern)"),12,null,true),urlBox,
            status,Theme.Row(undo,toggle),more,
            new Border{Height=1,Background=Theme.Border,Margin=new Thickness(0,6,0,10)},results);
        var scroll=new ScrollViewer{Content=side,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(0,0,8,0)};Grid.SetColumn(scroll,2);root.Children.Add(scroll);
        Content=root;
        // The phone waits for this answer, so each round can be run from the phone alone.
        server.Received=data=>Dispatcher.InvokeAsync(()=>{ClosePatches();var round=Round(()=>PhotoMatch.Load(data));Activate();return round;}).Task.Unwrap();
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.Length>0)_=Round(()=>PhotoMatch.Load(File.ReadAllBytes(files[0])));};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.V&&Keyboard.Modifiers==ModifierKeys.Control){Paste();e.Handled=true;}};
        Closed+=(_,_)=>{ClosePatches();server.Dispose();};
        Prompt();
    }
    private void Prompt()
    {
        var missing=Active.FirstOrDefault(m=>!positions.ContainsKey(m.Key));
        prompt.Text=current==null?L.T("1. Testbild zeigen  ·  2. Foto machen  ·  3. Zuordnung prüfen"):missing!=null?L.F($"Klicke im Foto auf den Bildschirm mit der großen {missing.Number} ({c.Name(missing)}, {Where(missing)}).")
            :manual?L.T("Alle Bildschirme zugeordnet. Falsch? Markierung auf den richtigen Bildschirm ziehen."):L.T("Automatisch zugeordnet. Die Nummern sollten zu den großen Zahlen im Foto passen. Sonst eine Markierung auf den richtigen Bildschirm ziehen.");
    }
    // Rough desk position from the Windows arrangement, e.g. "oben links" or "links, hochkant".
    private string Where(MonitorDevice m)
    {
        var all=Active;double left=all.Min(x=>x.X),right=all.Max(x=>x.X+x.Width),top=all.Min(x=>x.Y),bottom=all.Max(x=>x.Y+x.Height);
        double h=(m.X+m.Width/2.0-left)/Math.Max(1,right-left),v=(m.Y+m.Height/2.0-top)/Math.Max(1,bottom-top);
        var parts=new List<string>();
        if(all.Any(x=>x.Y+x.Height<=m.Y+m.Height/2.0||x.Y>=m.Y+m.Height/2.0)){if(v<0.4)parts.Add(L.T("oben"));else if(v>0.6)parts.Add(L.T("unten"));}
        parts.Add(h<1/3.0?L.T("links"):h>2/3.0?L.T("rechts"):L.T("Mitte"));
        var text=string.Join(" ",parts);return m.Height>m.Width?text+", "+L.T("hochkant"):text;
    }
    private void Choose(string? key)
    {
        if(reference.ItemsSource is IEnumerable<Choice> choices&&choices.FirstOrDefault(x=>x.Monitor.Key==key) is {} choice)reference.SelectedItem=choice;
    }
    // One round: read the photo, find the screens, apply the step (or finish). Returns the message for the phone.
    private async Task<string> Round(Func<BitmapSource> read)
    {
        try{current=read();}catch(Exception ex){status.Text=L.T("Foto konnte nicht geöffnet werden: ")+ex.Message;status.Foreground=Theme.Danger;return status.Text;}
        stale=false;empty.Visibility=Visibility.Collapsed;image.Source=current;photo.Width=current.PixelWidth;photo.Height=current.PixelHeight;round++;
        if(!manual)Detect();Draw();Measure();Prompt();
        if(plans.Count<Active.Count)return L.T("Nicht alle Bildschirme erkannt. Bitte am PC im Foto anklicken, dann geht es weiter.");
        if(warned)return status.Text+" "+L.T("Bitte noch einmal fotografieren.");
        if(done||round>=MaxRounds){Finish();return done?L.T("Fertig! Alle Bildschirme sind angeglichen und gespeichert."):L.T("Fertig. Genauer misst die Handykamera nicht; der beste Stand ist gespeichert.");}
        if(autoApply.IsChecked!=true)return L.T("Foto ist am PC. Dort „Vorschlag anwenden“ klicken.");
        await Apply();
        return L.F($"Runde {round} angewendet. Das Testbild ist wieder da: nächstes Foto machen.");
    }
    private void Finish()
    {
        ClosePatches();apply.IsEnabled=false;
        if(reference.SelectedItem is Choice{Monitor:var refMonitor})c.SaveMatch(refMonitor.Key);
        status.Text=L.T("Fertig und gespeichert. Der gemeinsame Helligkeitsregler hält die Monitore ab jetzt gleich.");status.Foreground=Theme.Success;
    }
    // Finds the lit screens and pairs them with displays; keeps earlier positions when not every screen is found.
    private bool Detect()
    {
        if(current==null)return false;var all=Active;
        var found=PhotoMatch.Assign(PhotoMatch.FindScreens(current,all.Count),all.Select(m=>(m.Key,m.X+m.Width/2.0,m.Y+m.Height/2.0,m.Width/(double)Math.Max(1,m.Height))).ToList());
        if(found==null)return false;positions.Clear();foreach(var pair in found)positions[pair.Key]=pair.Value;return true;
    }
    private void Redetect()
    {
        if(current==null)return;manual=false;
        if(!Detect()){positions.Clear();manual=true;results.Children.Clear();Draw();Prompt();status.Text=L.T("Nicht alle Bildschirme gefunden. Bitte im Foto anklicken.");status.Foreground=Theme.Danger;return;}
        Draw();Measure();Prompt();
    }
    private void OpenFile()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter=L.T("Bilder")+"|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff|*.*|*.*"};
        if(dialog.ShowDialog(this)==true)_=Round(()=>PhotoMatch.Load(File.ReadAllBytes(dialog.FileName)));
    }
    private void Paste()
    {
        if(Clipboard.ContainsImage()&&Clipboard.GetImage() is {} bitmap)_=Round(()=>PhotoMatch.Normalize(bitmap));
        else if(Clipboard.ContainsFileDropList()&&Clipboard.GetFileDropList() is {Count:>0} files)_=Round(()=>PhotoMatch.Load(File.ReadAllBytes(files[0]!)));
    }
    private void ResetPositions(){positions.Clear();manual=true;plans.Clear();apply.IsEnabled=false;results.Children.Clear();Draw();Prompt();}
    private Point PhotoPoint(MouseEventArgs e){var p=e.GetPosition(photo);return new(Math.Clamp(p.X/current!.PixelWidth,0,1),Math.Clamp(p.Y/current.PixelHeight,0,1));}
    // A press on a marker drags it to another screen; a click elsewhere places the next unassigned display.
    private void PhotoDown(object sender,MouseButtonEventArgs e)
    {
        if(current==null)return;var p=PhotoPoint(e);double grab=PhotoMatch.SampleRadius*Math.Min(current.PixelWidth,current.PixelHeight)*3;
        double Distance(Point q)=>Math.Sqrt(Math.Pow((q.X-p.X)*current.PixelWidth,2)+Math.Pow((q.Y-p.Y)*current.PixelHeight,2));
        dragging=positions.Where(x=>Distance(x.Value)<=grab).OrderBy(x=>Distance(x.Value)).Select(x=>x.Key).FirstOrDefault();
        if(dragging!=null){photo.CaptureMouse();e.Handled=true;}
    }
    private void PhotoMove(object sender,MouseEventArgs e){if(dragging!=null&&current!=null){positions[dragging]=PhotoPoint(e);Draw();}}
    private void PhotoUp(object sender,MouseButtonEventArgs e)
    {
        if(current==null)return;
        if(dragging!=null){positions[dragging]=PhotoPoint(e);dragging=null;photo.ReleaseMouseCapture();manual=true;Draw();Measure();Prompt();return;}
        var missing=Active.FirstOrDefault(m=>!positions.ContainsKey(m.Key));if(missing==null)return;
        positions[missing.Key]=PhotoPoint(e);manual=true;Draw();Measure();Prompt();
    }
    private void Draw()
    {
        markers.Children.Clear();if(current==null)return;double size=Math.Min(current.PixelWidth,current.PixelHeight),r=PhotoMatch.SampleRadius*size;
        foreach(var m in Active.Where(m=>positions.ContainsKey(m.Key)))
        {
            var p=positions[m.Key];double x=p.X*current.PixelWidth,y=p.Y*current.PixelHeight;
            var box=new Rectangle{Width=2*r,Height=2*r,Stroke=Brushes.White,StrokeThickness=Math.Max(2,size/400),Fill=new SolidColorBrush(Color.FromArgb(40,0,0,0))};Canvas.SetLeft(box,x-r);Canvas.SetTop(box,y-r);markers.Children.Add(box);
            var tag=new Border{Background=Brushes.Black,CornerRadius=new CornerRadius(size/150),Padding=new Thickness(size/160,0,size/160,0),Child=new TextBlock{Text=m.Number.ToString(),Foreground=Brushes.White,FontSize=Math.Max(14,size/28),FontWeight=FontWeights.SemiBold}};
            Canvas.SetLeft(tag,x+r+size/120);Canvas.SetTop(tag,y-r);markers.Children.Add(tag);
        }
    }
    private void Measure()
    {
        results.Children.Clear();plans.Clear();apply.IsEnabled=false;((FrameworkElement)hint.Parent).Visibility=Visibility.Collapsed;
        if(current==null||reference.SelectedItem is not Choice{Monitor:var refMonitor}||!positions.ContainsKey(refMonitor.Key))return;
        var samples=Active.Where(m=>positions.ContainsKey(m.Key)).ToDictionary(m=>m.Key,m=>PhotoMatch.Sample(current,positions[m.Key].X,positions[m.Key].Y));
        var refSample=samples[refMonitor.Key];
        central=samples.Count==Active.Count?PhotoMatch.Central(samples):null;
        // Until the user picks one, the most central display becomes the reference automatically.
        if(!referenceChosen&&central!=null&&central!=refMonitor.Key){choosingReference=true;Choose(central);choosingReference=false;return;}
        if(central!=null&&central!=refMonitor.Key&&Active.FirstOrDefault(m=>m.Key==central) is {} better)
        {
            hint.Text=L.F($"Empfohlen: {better.Number} · {c.Name(better)}. Er liegt farblich in der Mitte; ein Ausreißer als Referenz zieht alle anderen zu sich.");
            ((FrameworkElement)hint.Parent).Visibility=Visibility.Visible;
        }
        foreach(var m in Active.Where(m=>samples.ContainsKey(m.Key)))
        {
            var s=samples[m.Key];c.Overlays.Levels.TryGetValue(m.Key,out int dim);
            var plan=PhotoMatch.Plan(m.Key,s,refSample,Value(m,MonitorControl.Brightness),Value(m,MonitorControl.Red),Value(m,MonitorControl.Green),Value(m,MonitorControl.Blue),dim,m.Key==refMonitor.Key,colors.IsChecked==true);plans.Add(plan);
            results.Children.Add(Row(m,plan,s));
        }
        var warnings=new List<string>();
        if(samples.Values.Any(s=>s.Clipped))warnings.Add(L.T("Foto ist überbelichtet. Belichtung am Handy etwas runter oder Testbild dunkler fotografieren."));
        if(samples.Values.Any(s=>s.Dark))warnings.Add(L.T("Foto ist zu dunkel. Belichtung am Handy etwas hoch."));
        done=plans.Count==Active.Count&&plans.All(p=>p.Reference||p.Matched);warned=warnings.Count>0;
        status.Text=stale?L.T("Vorschlag ist übernommen. Für die nächste Runde ein neues Foto machen."):warnings.Count>0?string.Join(" ",warnings):done?L.F($"Runde {round}: alle Monitore liegen innerhalb von 5 % Helligkeit und 3 % Farbe. Fertig, als Profil speichern."):plans.Count<Active.Count?L.T("Noch nicht alle Bildschirme zugeordnet."):L.F($"Runde {round}: Vorschlag anwenden, dann neues Foto.");
        status.Foreground=stale?Theme.Muted:warnings.Count>0?Theme.Danger:done?Theme.Success:Theme.Muted;
        apply.IsEnabled=!stale&&warnings.Count==0&&!done&&plans.Count==Active.Count&&!c.Busy;
    }
    private UIElement Row(MonitorDevice m,MatchPlan plan,PatchSample s)
    {
        string Change(string label,int? from,int? to)=>from is int a&&to is int b&&a!=b?$"{label} {a}→{b}":"";
        var lines=new List<string>();
        if(plan.Reference)lines.Add(L.T("Referenz"));
        else
        {
            lines.Add(L.F($"Helligkeit {plan.RelativeBrightness*100:0} % der Referenz")+(Math.Abs(plan.Cooler)>=1?" · "+(plan.Cooler>0?L.F($"{plan.Cooler:0} % kühler"):L.F($"{-plan.Cooler:0} % wärmer")):"")+(Math.Abs(plan.Greener)>=1?" · "+(plan.Greener>0?L.F($"{plan.Greener:0} % grünlich"):L.F($"{-plan.Greener:0} % magenta")):""));
            int? Now(MonitorControl control)=>LiveControlQueue.Control(m.Hardware,control)!=null?c.ControlPercent(m,control):null;
            c.Overlays.Levels.TryGetValue(m.Key,out int dim);
            var changes=new[]{Change(L.T("Helligkeit"),Now(MonitorControl.Brightness),plan.Brightness),Change(L.T("Rot"),Now(MonitorControl.Red),plan.Red),Change(L.T("Grün"),Now(MonitorControl.Green),plan.Green),Change(L.T("Blau"),Now(MonitorControl.Blue),plan.Blue),Change(L.T("Dimmung"),dim,plan.Dimming)}.Where(t=>t.Length>0).ToList();
            lines.Add(plan.Matched?L.T("Passt."):changes.Count>0?string.Join(" · ",changes):L.T("Keine Änderung möglich."));
            if(plan.TooDim)lines.Add(L.T("Wird bei 100 % nicht so hell wie die Referenz. Einen dunkleren Monitor als Referenz wählen."));
            if(m.Hardware.Red==null)lines.Add(L.T("Keine RGB-Regler über DDC/CI: Farbe nur im Monitormenü (Farbtemperatur „Benutzer“) änderbar."));
        }
        var title=Theme.Label($"{m.Number} · {c.Name(m)}",13,null,true);title.Margin=new Thickness(0,0,0,2);
        var stack=Theme.Stack(title);foreach(var line in lines)stack.Children.Add(Theme.Label(line,12,line==lines[0]&&!plan.Reference?Theme.Text:Theme.Muted));
        var swatch=new Border{Width=26,Height=26,CornerRadius=new CornerRadius(6),BorderBrush=Theme.Border,BorderThickness=new Thickness(1),Margin=new Thickness(0,2,12,0),VerticalAlignment=VerticalAlignment.Top,Background=new SolidColorBrush(Color.FromRgb(Code(s.R),Code(s.G),Code(s.B)))};
        var row=new DockPanel{Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(swatch,Dock.Left);row.Children.Add(swatch);row.Children.Add(stack);return row;
    }
    private static byte Code(double linear)=>(byte)Math.Round(255*Math.Clamp(linear<=0.0031308?linear*12.92:1.055*Math.Pow(linear,1/2.4)-0.055,0,1));
    private async Task Apply()
    {
        if(c.Busy)return;apply.IsEnabled=false;stale=true;referenceChosen=true;
        foreach(var m in Active.Where(m=>!original.ContainsKey(m.Key))){c.Overlays.Levels.TryGetValue(m.Key,out int dim);original[m.Key]=(Value(m,MonitorControl.Brightness),Value(m,MonitorControl.Red),Value(m,MonitorControl.Green),Value(m,MonitorControl.Blue),dim);}
        foreach(var plan in plans.Where(p=>!p.Reference))
        {
            var m=c.Monitors.FirstOrDefault(x=>x.Key==plan.Key);if(m==null)continue;
            Set(m,MonitorControl.Brightness,plan.Brightness);Set(m,MonitorControl.Red,plan.Red);Set(m,MonitorControl.Green,plan.Green);Set(m,MonitorControl.Blue,plan.Blue);
            c.SetDimming(m,plan.Dimming);
        }
        status.Text=L.T("Wird übernommen …");status.Foreground=Theme.Muted;
        await c.Live.DrainAsync();await Task.Delay(1500);undo.IsEnabled=true;
        // Every round keeps the best state so far, even if no round ever reports "done".
        if(reference.SelectedItem is Choice{Monitor:var refMonitor})c.SaveMatch(refMonitor.Key);
        status.Text=L.T("Übernommen. Neues Testbild ist sichtbar: jetzt das nächste Foto machen.");ShowPatches();
    }
    // Restores every display to the values it had before the first suggestion in this window.
    private async Task Undo()
    {
        if(c.Busy||original.Count==0)return;undo.IsEnabled=false;apply.IsEnabled=false;
        foreach(var m in Active.Where(m=>original.ContainsKey(m.Key)))
        {
            var o=original[m.Key];Set(m,MonitorControl.Brightness,o.Brightness);Set(m,MonitorControl.Red,o.Red);Set(m,MonitorControl.Green,o.Green);Set(m,MonitorControl.Blue,o.Blue);c.SetDimming(m,o.Dimming);
        }
        original.Clear();stale=true;c.Settings.Match=matchBefore;c.SaveSettings();status.Text=L.T("Wird zurückgesetzt …");status.Foreground=Theme.Muted;
        await c.Live.DrainAsync();
        status.Text=L.T("Werte von vor dem Abgleich wiederhergestellt. Für einen neuen Versuch Testbild zeigen und neues Foto machen.");
    }
    private int? Value(MonitorDevice m,MonitorControl control)=>LiveControlQueue.Control(m.Hardware,control)!=null?c.ControlPercent(m,control):null;
    private void Set(MonitorDevice m,MonitorControl control,int? value){if(value is int v&&LiveControlQueue.Control(m.Hardware,control)!=null&&c.ControlPercent(m,control)!=v)c.LiveControl(m,control,v);}
    private void ShowPatches()
    {
        ClosePatches();var gray=new SolidColorBrush(Color.FromRgb(PhotoMatch.PatchLevel,PhotoMatch.PatchLevel,PhotoMatch.PatchLevel));
        foreach(var m in Active)
        {
            var label=new TextBlock{Text=$"{c.Name(m)}  ·  {L.T("Handy")}: {server.Urls.FirstOrDefault()}  ·  {L.T("Esc oder Klick schließt")}",Foreground=new SolidColorBrush(Color.FromRgb(120,120,120)),FontSize=16,Margin=new Thickness(24),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
            // The number sits in a corner, away from the sampled center, so the photo shows which screen is which.
            var number=new TextBlock{Text=m.Number.ToString(),Foreground=new SolidColorBrush(Color.FromRgb(110,110,110)),FontSize=Math.Clamp(Math.Min(m.Width,m.Height)*0.16,90,240),FontWeight=FontWeights.SemiBold,Margin=new Thickness(40,10,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
            var root=new Grid{Background=gray};root.Children.Add(number);root.Children.Add(label);
            var w=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=gray,Content=root,Cursor=Cursors.None,Left=m.X,Top=m.Y,Width=Math.Max(100,m.Width),Height=Math.Max(100,m.Height)};
            w.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)ClosePatches();};w.MouseLeftButtonUp+=(_,_)=>ClosePatches();
            w.Show();Native.SetWindowPos(new WindowInteropHelper(w).Handle,new IntPtr(-1),m.X,m.Y,m.Width,m.Height,0x10|0x40);patches.Add(w);
        }
        // Software dimming must stay above the patch or the photo would not show its effect.
        c.Overlays.Reposition(c.Monitors);
        var timeout=new DispatcherTimer{Interval=TimeSpan.FromMinutes(3)};timeout.Tick+=(_,_)=>{timeout.Stop();ClosePatches();};timeout.Start();
    }
    private void ClosePatches(){foreach(var w in patches.ToArray())w.Close();patches.Clear();}
}
