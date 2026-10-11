using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MonitorPilot;
internal sealed partial class MainWindow
{
    private readonly Dictionary<string,List<Button>> appearanceButtons=new();
    private ScrollViewer? pageScroll;
    private void BuildShell()
    {
        var root=new Grid();root.ColumnDefinitions.Add(new(){Width=new GridLength(210)});root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var side=new DockPanel{Background=Theme.Sidebar};root.Children.Add(new Border{BorderBrush=Theme.Border,BorderThickness=new Thickness(0,0,1,0),Child=side});
        var logo=Theme.Icon("\uE7F4",25);logo.Foreground=Theme.Accent;
        var brand=new DockPanel{Margin=new Thickness(23,25,15,28)};logo.Margin=new Thickness(0,0,10,0);brand.Children.Add(logo);brand.Children.Add(Theme.Stack(Theme.Label("MonitorPilot",19,null,true),Theme.Label(L.T("Dein Display-Workspace"),10,Theme.Muted)));DockPanel.SetDock(brand,Dock.Top);side.Children.Add(brand);
        var footer=Theme.Stack(Theme.Label(L.T("IM HINTERGRUND AKTIV"),10,Theme.Success,true),hotkeyStatus,Theme.Label(L.T("Version 1.4.0 · Lokal"),11,Theme.Muted),Theme.Button(L.T("Ins Tray minimieren"),Hide));footer.Margin=new Thickness(18,14,10,16);DockPanel.SetDock(footer,Dock.Bottom);side.Children.Add(footer);
        var links=new StackPanel{Margin=new Thickness(12,0,12,0)};
        foreach(var item in new[]{("monitors","\uE7F4",L.T("Monitore")),("colors","\uE790",L.T("Bild & Farbe")),("profiles","\uE8B7",L.T("Arbeitsprofile")),("calibrate","\uE81E",L.T("Farbvergleich")),("settings","\uE713",L.T("Einstellungen"))})
        {
            string key=item.Item1;var icon=Theme.Icon(item.Item2,17);icon.Margin=new Thickness(0,0,12,0);var text=Theme.Label(item.Item3,13);text.Margin=new Thickness(0);var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(icon);row.Children.Add(text);
            var button=Theme.Button(item.Item3,()=>Navigate(key));button.Style=(Style)FindResource("NavButton");button.Content=row;nav[key]=button;links.Children.Add(button);
        }
        links.Children.Add(new Border{Height=1,Background=Theme.Border,Margin=new Thickness(8,19,8,18)});links.Children.Add(Theme.Label(L.T("WERKZEUGE"),10,Theme.Muted,true));
        links.Children.Add(Theme.Button(L.T("Schnellsteuerung"),ShowTrayPanel));
        links.Children.Add(Theme.Button(L.T("Fenster einsammeln"),()=>_=c.Run(c.Rescue)));
        links.Children.Add(Theme.Button(L.T("Softwaredimmung aus"),()=>{c.Overlays.Clear();c.Report(L.T("Softwaredimmung entfernt."));Render();}));side.Children.Add(links);
        var main=new Grid{Margin=new Thickness(26,22,24,12)};Grid.SetColumn(main,1);main.RowDefinitions.Add(new(){Height=GridLength.Auto});main.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});main.RowDefinitions.Add(new(){Height=GridLength.Auto});root.Children.Add(main);
        heading.FontSize=27;
        var header=new DockPanel{Margin=new Thickness(0,0,0,18)};var switcher=Theme.Stack(Theme.Label(L.T("APP-DESIGN"),9,Theme.Muted,true),AppearanceSelector());switcher.HorizontalAlignment=HorizontalAlignment.Right;switcher.Margin=new Thickness(20,2,0,0);DockPanel.SetDock(switcher,Dock.Right);header.Children.Add(switcher);header.Children.Add(Theme.Stack(heading,subtitle));main.Children.Add(header);
        pageScroll=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,7,0)};Grid.SetRow(pageScroll,1);main.Children.Add(pageScroll);
        status.FontSize=12;status.Margin=new Thickness(0);var foot=new Border{BorderBrush=Theme.Border,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,10,0,2),Margin=new Thickness(0,8,0,0),Child=status,MinHeight=36};Grid.SetRow(foot,2);main.Children.Add(foot);
        Content=root;Render();
    }
    private UIElement AppearanceSelector()
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var option in new[]{("Light",L.T("Hell")),("Dark",L.T("Dunkel")),("System","System")})
        {
            string mode=option.Item1;var button=Theme.Button(option.Item2,()=>SetAppearance(mode));button.Margin=new Thickness(2);button.Padding=new Thickness(12,6,12,6);button.MinHeight=30;button.BorderThickness=new Thickness(0);button.FontSize=12;
            button.ToolTip=mode=="System"?L.T("App-Design folgt der Windows-Einstellung"):L.T("App im ")+(mode=="Light"?L.T("hellen"):L.T("dunklen"))+L.T(" Design anzeigen");
            if(!appearanceButtons.TryGetValue(mode,out var list)){list=new();appearanceButtons[mode]=list;}list.Add(button);row.Children.Add(button);
        }
        var container=new Border{Background=Theme.Hover,BorderBrush=Theme.Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(2),Child=row,HorizontalAlignment=HorizontalAlignment.Left};UpdateAppearanceButtons();return container;
    }
    private void SetAppearance(string mode)
    {
        try{c.Settings.Appearance=mode;Theme.SetMode(mode);c.SaveSettings();c.Report(L.T("App-Design: ")+(mode=="Light"?L.T("Hell"):mode=="Dark"?L.T("Dunkel"):"System")+L.T(" · Auswahl gespeichert."));}catch(Exception ex){c.Report(ex.Message,true);}
    }
    private void ThemeChanged(){UpdateAppearanceButtons();if(loaded)BuildTrayMenu();}
    private void UpdateAppearanceButtons()
    {
        foreach(var pair in appearanceButtons)foreach(var b in pair.Value){b.Background=pair.Key==Theme.Mode?Theme.Card:Brushes.Transparent;b.Foreground=pair.Key==Theme.Mode?Theme.Accent:Theme.Muted;b.FontWeight=pair.Key==Theme.Mode?FontWeights.SemiBold:FontWeights.Normal;}
    }
    private void Navigate(string target){currentPage=target;Render();pageScroll?.ScrollToTop();}
    private Button ActionButton(string text,Func<Task> action,bool primary=false)=>Theme.Button(text,()=>_=c.Run(action),primary);
    private void Render()
    {
        updateHardwareControls=null;
        foreach(var item in nav){bool selected=item.Key==currentPage;item.Value.Background=selected?Theme.Soft:Brushes.Transparent;if(item.Value.Content is StackPanel row)foreach(var element in row.Children.OfType<TextBlock>())element.Foreground=selected?Theme.Accent:Theme.Muted;}
        (heading.Text,subtitle.Text)=currentPage switch{"colors"=>(L.T("Bild & Farbe"),L.T("Helligkeit und Farben für jeden Monitor fein abstimmen.")),"profiles"=>(L.T("Arbeitsprofile"),L.T("Deine Monitore und Fenster als Arbeitsplatz speichern.")),"calibrate"=>(L.T("Farbvergleich"),L.T("Unterschiede erkennen. Schritt für Schritt angleichen.")),"settings"=>(L.T("Einstellungen"),L.T("Design, Hintergrundbetrieb und Tastenkürzel.")),_=>(L.T("Monitore"),c.Monitors.Count>0?L.F($"{c.Monitors.Count(m=>m.Active)} aktiv · {c.Monitors.Count} verbunden · {c.Profiles.Count} gespeicherte Profile"):L.T("Deine Bildschirme werden erkannt …"))};
        page.Content=currentPage switch{"colors"=>ColorsPage(),"profiles"=>ProfilesPage(),"calibrate"=>CalibrationPage(),"settings"=>SettingsPage(),_=>MonitorsPage()};page.IsEnabled=!c.Busy;
    }
    private UIElement MonitorsPage()
    {
        var content=new StackPanel();
        var hardwareCount=Theme.Badge(c.Monitors.Count(m=>m.Hardware.Brightness!=null)+L.T(" mit Helligkeitsregelung"));
        var updates=new List<Action>{()=>((TextBlock)hardwareCount.Child).Text=c.Monitors.Count(m=>m.Hardware.Brightness!=null)+L.T(" mit Helligkeitsregelung")};
        var toolbar=new DockPanel{Margin=new Thickness(0,0,0,8)};var left=Theme.Row(Theme.Badge("●  "+c.Monitors.Count(m=>m.Active)+L.T(" aktiv"),true),hardwareCount);left.VerticalAlignment=VerticalAlignment.Center;
        var actions=Theme.Row(Theme.Button(L.T("Identifizieren"),()=>c.Overlays.Identify(c.Monitors)),ActionButton(L.T("Neu erkennen"),c.Refresh));DockPanel.SetDock(actions,Dock.Right);toolbar.Children.Add(actions);toolbar.Children.Add(left);content.Children.Add(toolbar);
        var mapHeader=new DockPanel();var arrange=Theme.Button(L.T("Anordnen"),EditLayout);arrange.MinHeight=30;arrange.Padding=new Thickness(10,5,10,5);arrange.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(arrange,Dock.Right);mapHeader.Children.Add(arrange);mapHeader.Children.Add(Theme.Label(L.T("Monitoranordnung"),16,null,true));
        var canvas=new Canvas{Height=226,ClipToBounds=true,Background=Theme.Map,Margin=new Thickness(0,10,0,0)};canvas.SizeChanged+=(_,_)=>DrawMap(canvas);
        var mapCard=Theme.Panel(Theme.Stack(mapHeader,canvas,Theme.Label(L.T("Klicke auf einen Bildschirm, um seine Werte zu bearbeiten."),11,Theme.Muted)));mapCard.Padding=new Thickness(18,16,18,8);
        var quick=Theme.Stack(Theme.Label(L.T("Fokus & Wiederherstellung"),16,null,true),Theme.Label(L.T("Zusatzmonitore ausschalten. Dein Arbeitsplatz bleibt gesichert."),12,Theme.Muted));
        var focus=ActionButton(c.Monitors.Count(m=>m.Active)==1?L.T("Arbeitsplatz wiederherstellen"):L.T("Nur Hauptmonitor verwenden"),c.Focus,true);focus.Margin=new Thickness(0,6,0,8);quick.Children.Add(focus);
        var restore=ActionButton(L.T("Gesicherten Arbeitsplatz laden"),c.Restore);restore.Margin=new Thickness(0,0,0,8);quick.Children.Add(restore);
        var extend=ActionButton(L.T("Alle Monitore erweitern"),c.Extend);extend.Margin=new Thickness(0,0,0,12);quick.Children.Add(extend);
        quick.Children.Add(Theme.Label(c.FocusBackup==null?L.T("Vor jedem Wechsel wird die aktuelle Anordnung automatisch gesichert."):L.F($"Sicherung: {c.FocusBackup.Topology.Identities.Count} Monitore · {c.FocusBackup.Windows.Count} Fenster\n{c.FocusBackup.CapturedAt:dd.MM. HH:mm}"),11,c.FocusBackup==null?Theme.Muted:Theme.Success));
        content.Children.Add(Theme.Columns(mapCard,Theme.Panel(quick),300));
        var table=new StackPanel();table.Children.Add(Theme.Label(L.T("Verbundene Bildschirme"),16,null,true));
        var selected=new Dictionary<string,CheckBox>();
        foreach(var monitor in c.Monitors)
        {
            var row=new Grid{Margin=new Thickness(0,7,0,7)};
            row.ColumnDefinitions.Add(new(){Width=new GridLength(40)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(110)});row.ColumnDefinitions.Add(new(){Width=new GridLength(75)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var number=Theme.Badge(monitor.Number.ToString(),monitor.Primary);row.Children.Add(number);
            var title=Theme.Stack(Theme.Label(c.Name(monitor),14,null,true),Theme.Label(monitor.Primary?L.T("Hauptmonitor · ")+monitor.Connection:monitor.Connection,11,Theme.Muted));Grid.SetColumn(title,1);row.Children.Add(title);
            var hardwareInfo=Theme.Label(monitor.Hardware.Brightness!=null?L.T("Hardwaresteuerung verfügbar"):monitor.Active?monitor.DdcStatus:L.T("Deaktiviert"),11,Theme.Muted);
            var info=Theme.Stack(Theme.Label(monitor.Summary,12),hardwareInfo);Grid.SetColumn(info,2);row.Children.Add(info);
            var brightness=Theme.Label(monitor.Hardware.Brightness is {} b?L.F($"{b.Percent} %  Helligkeit"):"—",12,Theme.Muted);brightness.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(brightness,3);row.Children.Add(brightness);
            updates.Add(()=>{hardwareInfo.Text=monitor.Hardware.Brightness!=null?L.T("Hardwaresteuerung verfügbar"):monitor.DdcStatus;brightness.Text=monitor.Hardware.Brightness is {} value?L.F($"{value.Percent} %  Helligkeit"):"—";});
            var check=new CheckBox{Content=L.T("Aktiv"),IsChecked=monitor.Active,VerticalAlignment=VerticalAlignment.Center};selected[monitor.Key]=check;Grid.SetColumn(check,4);row.Children.Add(check);
            var detail=Theme.Button(L.T("Bearbeiten"),()=>{selectedKey=monitor.Key;Navigate("colors");});detail.Margin=new Thickness(8,0,0,0);detail.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(detail,5);row.Children.Add(detail);
            row.SizeChanged+=(_,_)=>
            {
                bool compact=row.ActualWidth<650;info.Visibility=brightness.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                row.ColumnDefinitions[2].Width=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);row.ColumnDefinitions[3].Width=new GridLength(compact?0:110);
                title.ToolTip=monitor.Summary+" · "+monitor.DdcStatus;
            };
            table.Children.Add(row);table.Children.Add(new Border{Height=1,Background=Theme.Border});
        }
        var primary=new ComboBox{MinWidth=250,MaxWidth=350,ItemsSource=c.Monitors,SelectedItem=c.Main};
        var apply=ActionButton(L.T("Auswahl anwenden"),async()=>{if(primary.SelectedItem is not MonitorDevice main)throw new InvalidOperationException(L.T("Hauptmonitor auswählen."));await c.SelectMonitors(selected.Where(v=>v.Value.IsChecked==true).Select(v=>v.Key).ToList(),main.Key);},true);apply.Margin=new Thickness(0,4,0,10);
        var footer=Theme.Row(primary,apply);footer.Margin=new Thickness(0,10,0,0);table.Children.Add(footer);content.Children.Add(Theme.Panel(table));
        double Average()=>Math.Round(c.Monitors.Where(m=>m.Active&&m.Hardware.Brightness!=null).Select(m=>(double)c.ControlPercent(m,MonitorControl.Brightness)!).DefaultIfEmpty(50).Average());
        bool updating=false;var master=new Slider{Minimum=0,Maximum=100,Value=Average(),Width=210,IsEnabled=c.Monitors.Any(m=>m.Active&&m.Hardware.Brightness!=null),IsSnapToTickEnabled=true,TickFrequency=1,SmallChange=1,LargeChange=5,Margin=new Thickness(12,0,12,0)};System.Windows.Automation.AutomationProperties.SetName(master,L.T("Alle Displays"));var numberLabel=Theme.Label($"{master.Value:0} %",13,Theme.Accent,true);numberLabel.VerticalAlignment=VerticalAlignment.Center;master.ValueChanged+=(_,e)=>{numberLabel.Text=$"{e.NewValue:0} %";if(!updating)c.LiveBrightness((int)e.NewValue);};
        var liveControls=Theme.Stack(Theme.Row(Theme.Label(L.T("Alle Displays"),13,null,true),master,numberLabel),Theme.Label(L.T("Helligkeit wird live übernommen."),11,Theme.Muted));var windows=Theme.Button(L.T("Fensterpositionen zurück"),()=>_=c.Run(()=>{c.RestoreWindows();return Task.CompletedTask;}));content.Children.Add(Theme.Columns(liveControls,windows,220));
        updateHardwareControls=()=>{updating=true;try{foreach(var update in updates)update();master.IsEnabled=c.Monitors.Any(m=>m.Active&&m.Hardware.Brightness!=null);master.Value=Average();}finally{updating=false;}};return content;
    }
    private void DrawMap(Canvas canvas)
    {
        canvas.Children.Clear();var monitors=c.Monitors.Where(m=>m.Active).ToList();if(monitors.Count==0)return;
        double width=Math.Max(100,canvas.ActualWidth),height=canvas.Height;
        for(double x=12;x<width;x+=20)for(double y=12;y<height;y+=20){var dot=new Ellipse{Width=1.5,Height=1.5,Fill=Theme.Border};Canvas.SetLeft(dot,x);Canvas.SetTop(dot,y);canvas.Children.Add(dot);}
        int left=monitors.Min(m=>m.X),top=monitors.Min(m=>m.Y),right=monitors.Max(m=>m.X+m.Width),bottom=monitors.Max(m=>m.Y+m.Height);
        double scale=Math.Min((width-46)/Math.Max(1,right-left),(height-38)/Math.Max(1,bottom-top));double ox=(width-(right-left)*scale)/2,oy=(height-(bottom-top)*scale)/2;
        foreach(var m in monitors)
        {
            double w=m.Width*scale-5,h=m.Height*scale-5;
            var number=Theme.Label(m.Number.ToString(),20,m.Primary?Theme.Accent:Theme.Text,true);number.Margin=new Thickness(0,0,0,3);
            var text=Theme.Label(c.Name(m),10,Theme.Muted);text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;text.Margin=new Thickness(0);
            var stack=Theme.Stack(number,text);stack.VerticalAlignment=VerticalAlignment.Center;
            var box=new Border{Width=Math.Max(30,w),Height=Math.Max(30,h),Background=m.Primary?Theme.Soft:Theme.Card,BorderBrush=m.Primary?Theme.Accent:Theme.Track,BorderThickness=new Thickness(m.Primary?2:1),CornerRadius=new CornerRadius(6),Padding=new Thickness(10,4,8,4),Child=stack,Cursor=System.Windows.Input.Cursors.Hand,ToolTip=$"{c.Name(m)} · {m.Summary}"};
            Canvas.SetLeft(box,ox+(m.X-left)*scale+2);Canvas.SetTop(box,oy+(m.Y-top)*scale+2);box.MouseLeftButtonUp+=(_,_)=>{selectedKey=m.Key;Navigate("colors");};canvas.Children.Add(box);
        }
    }
}
