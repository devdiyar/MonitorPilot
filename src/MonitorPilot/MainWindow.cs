using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace MonitorPilot;

internal sealed partial class MainWindow : Window
{
    private readonly AppController c;
    private readonly ContentControl page=new();
    private readonly TextBlock status=Theme.Label("",13,Theme.Muted);
    private readonly TextBlock heading=Theme.Label("Dein Arbeitsplatz",30,null,true);
    private readonly TextBlock subtitle=Theme.Label("Alle Bildschirme. Ein Cockpit.",14,Theme.Muted);
    private readonly TextBlock hotkeyStatus=Theme.Label("Tastenkürzel werden geprüft",11,Theme.Muted);
    private string hotkeyProblem="";
    private readonly Dictionary<string,Button> nav=new();
    private Forms.NotifyIcon? tray;
    private HotkeyService? hotkeys;
    private string currentPage="monitors",selectedKey="";
    private bool exiting,loaded,notified;
    private Action? updateHardwareControls;
    private readonly bool startHidden,restoreOnStart;
    private readonly DispatcherTimer displayTimer=new() {Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer scheduleTimer=new() {Interval=TimeSpan.FromSeconds(30)};
    private bool? lastScheduleNight;
    public MainWindow(bool hidden=false,bool restore=false,AppController? controller=null)
    {
        c=controller??new();
        startHidden=hidden;restoreOnStart=restore;
        Title="MonitorPilot · Dein Monitor-Arbeitsplatz";Theme.FitToWorkArea(this,SystemParameters.WorkArea);WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Theme.Decorate(this);
        try {c.Load();} catch(Exception ex) {c.Report("Konfiguration konnte nicht geladen werden: "+ex.Message,true);}
        Theme.SetMode(c.Settings.Appearance);
        c.Owner=this;c.Changed+=()=>{if(loaded){Render();BuildTrayMenu();}};
        c.HardwareDiscovered+=()=>{if(loaded&&!exiting){updateHardwareControls?.Invoke();BuildTrayMenu();}};
        c.HardwareChanged+=()=>{if(loaded&&!exiting)updateHardwareControls?.Invoke();};
        c.LiveSettled+=()=>{if(loaded&&!exiting)BuildTrayMenu();};
        c.StatusChanged+=()=>{status.Text=(c.Busy?"◌  ":c.Error?"!  ":"●  ")+c.Status;status.Foreground=c.Error?Theme.Danger:Theme.Muted;page.IsEnabled=!c.Busy;};
        BuildShell();
        Theme.Changed+=ThemeChanged;
        SourceInitialized+=(_,_)=>
        {
            var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle).AddHook(Hook);
            hotkeys=new(handle);try {hotkeys.Register(c.Settings.Hotkeys);hotkeyStatus.Text="●  Globale Tastenkürzel aktiv";hotkeyStatus.Foreground=Theme.Accent;} catch(Exception ex) {hotkeyProblem=ex.Message;hotkeyStatus.Text="⚠  Tastenkürzel prüfen";hotkeyStatus.Foreground=Theme.Danger;c.Report(ex.Message,true);}
            tray=new Forms.NotifyIcon {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application,Text="MonitorPilot · Monitore steuern",Visible=true};
            tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.Invoke(ShowTrayPanel);};BuildTrayMenu();
        };
        Loaded+=async(_,_)=>
        {
            loaded=true;if(startHidden)Hide();
            try{await c.Initialize();}catch(Exception ex){c.Report("Monitorerkennung fehlgeschlagen: "+ex.Message,true);Store.Log(ex.ToString());}
            if(exiting)return;
            if(c.Profiles.Count==0)await c.Run(()=>c.SaveProfile("Mein Arbeitsplatz · Ausgangszustand"));
            scheduleTimer.Start();if(restoreOnStart)await c.Run(c.Restore);
        };
        Closing+=(_,e)=>
        {
            if(exiting)return;
            e.Cancel=true;Hide();Store.Log("Main window closed to tray; application remains running.");
            if(!notified && tray!=null){tray.ShowBalloonTip(2500,"MonitorPilot läuft weiter","Im Infobereich öffnen. Öffnen-Kürzel: "+HotkeyService.Format(c.Settings.Hotkeys.First(h=>h.Action=="show"))+". Beenden über das Tray-Menü.",Forms.ToolTipIcon.Info);notified=true;}
        };
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized && c.Settings.MinimizeToTray){Hide();Store.Log("Main window minimized to tray.");}};
        displayTimer.Tick+=async(_,_)=>{displayTimer.Stop();if(c.Busy){displayTimer.Start();return;}await c.Run(c.Refresh);};
        scheduleTimer.Tick+=async(_,_)=>
        {
            if(!c.Settings.BrightnessSchedule || c.Busy)return;
            if(!TimeSpan.TryParseExact(c.Settings.DayTime,"hh\\:mm",CultureInfo.InvariantCulture,out var day)||!TimeSpan.TryParseExact(c.Settings.NightTime,"hh\\:mm",CultureInfo.InvariantCulture,out var night))return;
            bool isNight=AppController.IsNight(DateTime.Now.TimeOfDay,day,night);
            if(lastScheduleNight==isNight)return;
            await c.Run(()=>c.Brightness(isNight?c.Settings.NightBrightness:c.Settings.DayBrightness));if(!c.Error)lastScheduleNight=isNight;
        };
    }
    private void ExportProfile(WorkspaceSnapshot snapshot)
    {
        var dialog=new SaveFileDialog {Filter="MonitorPilot-Profil (*.json)|*.json",FileName="MonitorPilot-Profil.json"};
        if(dialog.ShowDialog(this)==true)try{File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(snapshot,Store.Json));c.Report("Profil exportiert. Die Datei enthält lokale Fenster- und Monitornamen.");}catch(Exception ex){c.Report(ex.Message,true);}
    }
    private void ImportProfile()
    {
        var dialog=new OpenFileDialog {Filter="MonitorPilot-Profil (*.json)|*.json"};if(dialog.ShowDialog(this)!=true)return;
        try {if(new FileInfo(dialog.FileName).Length>2_000_000)throw new InvalidDataException("Die Profildatei ist zu groß.");c.Import(JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(dialog.FileName),Store.Json)??throw new InvalidDataException("Leeres Profil."));}catch(Exception ex){c.Report(ex.Message,true);}
    }
    private void EditHotkey(HotkeySetting original)
    {
        var dialog=Dialog("Tastenkürzel ändern",550,400);var ctrl=new CheckBox {Content="Strg",IsChecked=(original.Modifiers&2)!=0};var alt=new CheckBox {Content="Alt",IsChecked=(original.Modifiers&1)!=0};var shift=new CheckBox {Content="Umschalt",IsChecked=(original.Modifiers&4)!=0};var win=new CheckBox {Content="Win",IsChecked=(original.Modifiers&8)!=0};var key=new TextBox {Text=((Forms.Keys)original.Key).ToString()};
        var error=Theme.Label("",12,Theme.Danger);dialog.Content=new Border {Padding=new Thickness(24),Child=Theme.Stack(Theme.Label(original.Label,22,null,true),Theme.Row(ctrl,alt,shift,win),Theme.Label("Taste: z. B. M, F8, Up, Down oder D0",12,Theme.Muted),key,error,Theme.Button("Kürzel speichern",()=>
        {
            if(!Enum.TryParse<Forms.Keys>(key.Text.Trim(),true,out var parsed)||parsed==Forms.Keys.None||(uint)parsed>255){error.Text="Bitte eine einzelne gültige Taste eingeben.";return;}
            var settings=Store.Clone(c.Settings.Hotkeys);var update=settings.First(x=>x.Action==original.Action);update.Modifiers=(uint)((ctrl.IsChecked==true?2:0)|(alt.IsChecked==true?1:0)|(shift.IsChecked==true?4:0)|(win.IsChecked==true?8:0));update.Key=(uint)parsed;
            try{hotkeys!.Register(settings);hotkeyProblem="";hotkeyStatus.Text="●  Globale Tastenkürzel aktiv";hotkeyStatus.Foreground=Theme.Accent;c.Settings.Hotkeys=settings;c.SaveSettings();dialog.DialogResult=true;BuildTrayMenu();Render();}catch(Exception ex){error.Text=ex.Message;}
        },true))};dialog.ShowDialog();
    }
    private void EditLayout()
    {
        if(c.Busy)return;var snapshot=c.Displays.Capture();var modes=DisplayService.Unpack<Native.DisplayMode>(snapshot.Modes);var paths=DisplayService.Unpack<Native.DisplayPath>(snapshot.Paths);
        var dialog=Dialog("Monitoranordnung bearbeiten",760,Math.Min(720,250+c.Monitors.Count*62));var panel=Theme.Stack(Theme.Label("Positionen in Desktop-Pixeln",22,null,true),Theme.Label("Negative Koordinaten sind erlaubt. Nebeneinander: rechter X-Wert = linker X-Wert + dessen Breite. Der gewählte Hauptmonitor wird auf (0, 0) verschoben.",13,Theme.Muted));var fields=new List<(int Index,TextBox X,TextBox Y,RadioButton Main)>();
        for(int i=0;i<paths.Length;i++)
        {
            var mode=modes[paths[i].Source.ModeIndex].Source;var x=new TextBox {Text=mode.Position.X.ToString(),Width=100};var y=new TextBox {Text=mode.Position.Y.ToString(),Width=100};var main=new RadioButton {GroupName="primary",Content="Hauptmonitor",Foreground=Theme.Text,IsChecked=mode.Position.X==0&&mode.Position.Y==0,Margin=new Thickness(8,14,0,0)};
            var label=Theme.Label(snapshot.Identities[i].Name,14);label.Width=190;panel.Children.Add(Theme.Row(label,x,y,main));fields.Add((i,x,y,main));
        }
        var error=Theme.Label("",12,Theme.Danger);panel.Children.Add(error);panel.Children.Add(Theme.Button("Anordnung anwenden",()=>
        {
            foreach(var field in fields)
            {
                if(!int.TryParse(field.X.Text,out int x)||!int.TryParse(field.Y.Text,out int y)||Math.Abs((long)x)>32000||Math.Abs((long)y)>32000){error.Text="X und Y müssen ganze Zahlen zwischen -32000 und 32000 sein.";return;}
                uint index=paths[field.Index].Source.ModeIndex;modes[index].Source.Position.X=x;modes[index].Source.Position.Y=y;
            }
            var primary=fields.FirstOrDefault(f=>f.Main.IsChecked==true);if(primary.Main==null){error.Text="Bitte einen Hauptmonitor auswählen.";return;}
            var edited=new TopologySnapshot {Paths=snapshot.Paths,Modes=DisplayService.Pack(modes),Identities=snapshot.Identities};
            try{edited=DisplayService.Select(edited,edited.Identities.Select(x=>x.Key),snapshot.Identities[primary.Index].Key);c.Displays.Validate(edited);dialog.DialogResult=true;_=c.Run(()=>c.ApplyLayout(edited));}catch(Exception ex){error.Text=ex.Message;}
        },true));dialog.Content=new Border {Padding=new Thickness(24),Child=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};dialog.ShowDialog();
    }
    private Window Dialog(string title,int width,int height) {var w=new Window {Title="MonitorPilot · "+title,Owner=this,Width=width,Height=height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};Theme.Decorate(w);return w;}
    private void ExportDiagnostics()
    {
        var dialog=new SaveFileDialog {Filter="JSON (*.json)|*.json",FileName="MonitorPilot-Diagnose.json"};if(dialog.ShowDialog(this)!=true)return;
        try{File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(new{Version="1.3.0",Captured=DateTimeOffset.Now,OS=Environment.OSVersion.ToString(),Monitors=c.Monitors,Hotkeys=c.Settings.Hotkeys,HasFocusBackup=c.FocusBackup!=null},Store.Json));c.Report("Diagnose exportiert. Sie enthält Monitoridentitäten, aber keine Fensterinhalte.");}catch(Exception ex){c.Report(ex.Message,true);}
    }
    public void ShowApp() {Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);Store.Log("Main window opened.");}
    public void RestoreFromOutside() {ShowApp();_=c.Run(c.Restore);}
    public async void ExitApp()
    {
        if(exiting)return;
        if(c.Busy){c.Report("Bitte die laufende Monitoraktion abschließen, bevor die App beendet wird.");return;}
        exiting=true;loaded=false;trayQuick?.Close();scheduleTimer.Stop();displayTimer.Stop();
        await c.Run(()=>Task.CompletedTask);
        Theme.Changed-=ThemeChanged;Store.Log("Application explicitly exited after pending live changes.");hotkeys?.Dispose();c.Overlays.Clear();c.Overlays.ClosePatterns();if(tray!=null){tray.Visible=false;tray.Dispose();}Close();System.Windows.Application.Current.Shutdown();
    }
    private IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0312)
        {
            string? action=hotkeys?.Action(wParam.ToInt32());handled=action!=null;
            if(action!=null)Store.Log("Global hotkey: "+action);
            switch(action)
            {
                case "show":ShowApp();break;
                case "focus":_=c.Run(c.Focus);break;
                case "restore":_=c.Run(c.Restore);break;
                case "brighter":_=c.Run(()=>c.Brightness(5,true));break;
                case "darker":_=c.Run(()=>c.Brightness(-5,true));break;
                case "rescue":_=c.Run(c.Rescue);break;
                case "filters":c.Overlays.Clear();c.Report("Softwaredimmung entfernt.");Render();break;
            }
        }
        if(message==0x007E || message==0x0219 && wParam.ToInt32()==7){DisplayService.InvalidateModes();displayTimer.Stop();displayTimer.Start();}
        return IntPtr.Zero;
    }
    private static void Open(string path) {try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,"MonitorPilot");}}
    private static string TemperatureName(uint t)=>t switch {1=>"4000 K · Warm",2=>"5000 K",3=>"6500 K · Tageslicht",4=>"7500 K",5=>"8200 K",6=>"9300 K",7=>"10000 K",8=>"11500 K",_=>"Unbekannt"};
    private sealed record TemperatureOption(uint Value,string Name) {public override string ToString()=>Name;}
    private sealed record ModeOption(Native.DevMode Mode) {public override string ToString()=>$"{Mode.Width} × {Mode.Height} · {Mode.Frequency} Hz";}
}
