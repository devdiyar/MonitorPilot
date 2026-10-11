using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace MonitorPilot;
internal sealed partial class MainWindow
{
    private TrayQuickWindow? trayQuick;
    private void ShowTrayPanel()
    {
        if(trayQuick!=null){trayQuick.Close();return;}
        trayQuick=new TrayQuickWindow(c,()=>{ShowApp();Navigate("colors");},()=>{ShowApp();Navigate("settings");},()=>tray?.ContextMenuStrip?.Show(Forms.Cursor.Position));
        trayQuick.Closed+=(_,_)=>trayQuick=null;trayQuick.Show();trayQuick.Activate();
    }
    private void BuildTrayMenu()
    {
        if(tray==null)return;
        var menu=CreateTrayMenu();var old=tray.ContextMenuStrip;tray.ContextMenuStrip=menu;
        // A clicked menu may still be unwinding its native callback.
        if(old!=null)Dispatcher.BeginInvoke(old.Dispose);
    }
    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu=new Forms.ContextMenuStrip{Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors()),BackColor=TrayColors.Card,ForeColor=TrayColors.Text,ShowImageMargin=false};
        Forms.ToolStripMenuItem Item(Forms.ToolStripItemCollection items,string text,Action? action=null,bool enabled=true,bool check=false)
        {
            var item=new Forms.ToolStripMenuItem(text.Replace("&","&&")){ForeColor=TrayColors.Text,Enabled=enabled,Checked=check};if(action!=null)item.Click+=(_,_)=>Dispatcher.Invoke(()=>{try{action();}catch(Exception ex){c.Report(ex.Message,true);}});items.Add(item);return item;
        }
        Action Run(Func<Task> operation)=>()=>{trayQuick?.Close();_=c.Run(operation);};
        string Shortcut(string action)=>HotkeyService.Format(c.Settings.Hotkeys.First(h=>h.Action==action));
        Item(menu.Items,"MonitorPilot · "+c.Monitors.Count(m=>m.Active)+L.T(" aktive Monitore"),enabled:false);
        Item(menu.Items,L.T("Schnellsteuerung …"),ShowTrayPanel);Item(menu.Items,L.T("Hauptfenster öffnen"),ShowApp).ShortcutKeyDisplayString=Shortcut("show");menu.Items.Add(new Forms.ToolStripSeparator());
        Item(menu.Items,c.Monitors.Count(m=>m.Active)==1?L.T("Arbeitsplatz wiederherstellen"):L.T("Nur Fokusmonitor verwenden"),Run(c.Focus),!c.Busy).ShortcutKeyDisplayString=Shortcut("focus");
        Item(menu.Items,L.T("Gesicherten Arbeitsplatz laden"),Run(c.Restore),!c.Busy&&(c.FocusBackup!=null||File.Exists(Store.FilePath("last-workspace.json")))).ShortcutKeyDisplayString=Shortcut("restore");
        Item(menu.Items,L.T("Alle Monitore erweitern"),Run(c.Extend),!c.Busy);
        var profiles=Item(menu.Items,L.T("Arbeitsprofile"),enabled:!c.Busy);
        foreach(var profile in c.Profiles)Item(profiles.DropDownItems,profile.Workspace.Name,Run(async()=>{await c.ApplyWorkspace(profile.Workspace);}));
        profiles.DropDownItems.Add(new Forms.ToolStripSeparator());Item(profiles.DropDownItems,L.T("Aktuellen Arbeitsplatz speichern"),Run(()=>c.SaveProfile(L.T("Arbeitsplatz · ")+DateTime.Now.ToString("dd.MM. HH:mm"))));Item(profiles.DropDownItems,L.T("Profile verwalten …"),()=>{ShowApp();Navigate("profiles");});
        var focus=Item(menu.Items,L.T("Fokusmonitor wählen"),enabled:!c.Busy);foreach(var m in c.Monitors.Where(m=>m.Active))Item(focus.DropDownItems,c.Name(m),()=>{c.Settings.PreferredMain=m.Key;c.SaveSettings();c.Report(L.T("Fokusmonitor: ")+c.Name(m));Render();BuildTrayMenu();},check:c.Main?.Key==m.Key);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var brightness=Item(menu.Items,L.T("Helligkeit · alle Monitore"),enabled:!c.Busy);Item(brightness.DropDownItems,L.T("Heller (+5 %)"),Run(()=>c.Brightness(5,true))).ShortcutKeyDisplayString=Shortcut("brighter");Item(brightness.DropDownItems,L.T("Dunkler (−5 %)"),Run(()=>c.Brightness(-5,true))).ShortcutKeyDisplayString=Shortcut("darker");brightness.DropDownItems.Add(new Forms.ToolStripSeparator());foreach(int p in new[]{10,25,50,75,100})Item(brightness.DropDownItems,p+" %",Run(()=>c.Brightness(p)));
        var monitors=Item(menu.Items,L.T("Einzelne Monitore"),enabled:!c.Busy);
        foreach(var m in c.Monitors)
        {
            var monitor=Item(monitors.DropDownItems,$"{m.Number:00} · {c.Name(m)}"+(m.Primary?L.T(" · Hauptmonitor"):""));
            Item(monitor.DropDownItems,m.Summary,enabled:false);
            if(m.Active)
            {
                var light=Item(monitor.DropDownItems,L.T("Helligkeit")+(m.Hardware.Brightness is {} b?$" · {b.Percent} %":L.T(" · nicht verfügbar")),enabled:m.Hardware.Brightness!=null);
                foreach(int p in new[]{10,25,50,75,100})Item(light.DropDownItems,p+" %",Run(()=>c.ApplyHardware(m,HardwareService.BrightnessOnly(m.Hardware,p),c.Overlays.Levels.GetValueOrDefault(m.Key))));
                var dim=Item(monitor.DropDownItems,L.T("Softwaredimmung · ")+c.Overlays.Levels.GetValueOrDefault(m.Key)+" %");foreach(int p in new[]{0,10,20,40,60})Item(dim.DropDownItems,p==0?L.T("Aus"):p+" %",()=>{c.Overlays.Set(m,p);c.Report(c.Name(m)+L.T(": Softwaredimmung ")+p+" %");Render();BuildTrayMenu();},check:c.Overlays.Levels.GetValueOrDefault(m.Key)==p);
                var temperature=Item(monitor.DropDownItems,L.T("Weißpunkt"),enabled:m.Hardware.TemperatureMask!=0);
                for(uint t=1;t<=8;t++)if((m.Hardware.TemperatureMask&(1u<<((int)t-1)))!=0){uint value=t;Item(temperature.DropDownItems,TemperatureName(t),Run(()=>c.ApplyHardware(m,new HardwareState{Temperature=value},c.Overlays.Levels.GetValueOrDefault(m.Key))),check:m.Hardware.Temperature==t);}
                Item(monitor.DropDownItems,L.T("Werte vor erster Änderung"),Run(()=>c.OriginalHardware(m)));
                Item(monitor.DropDownItems,L.T("Nur diesen Monitor verwenden"),Run(()=>c.SelectMonitors(new(){m.Key},m.Key)));
            }
            Item(monitor.DropDownItems,m.Active?L.T("Monitor deaktivieren"):L.T("Monitor aktivieren"),Run(async()=>
            {
                var keys=c.Monitors.Where(x=>x.Active).Select(x=>x.Key).ToList();if(m.Active)keys.Remove(m.Key);else keys.Add(m.Key);if(keys.Count==0)throw new InvalidOperationException(L.T("Mindestens ein Monitor muss aktiv bleiben."));string primary=keys.Contains(c.Main?.Key??"")?c.Main!.Key:keys[0];await c.SelectMonitors(keys,primary);
            }),!m.Active||c.Monitors.Count(x=>x.Active)>1);
            Item(monitor.DropDownItems,L.T("Bild & Farbe öffnen …"),()=>{selectedKey=m.Key;ShowApp();Navigate("colors");});
        }
        Item(menu.Items,L.T("Alle Softwaredimmungen entfernen"),()=>{c.Overlays.Clear();c.Report(L.T("Softwaredimmung entfernt."));Render();BuildTrayMenu();},!c.Busy);
        var windows=Item(menu.Items,L.T("Fenster & Werkzeuge"),enabled:!c.Busy);Item(windows.DropDownItems,L.T("Fenster zum Fokusmonitor holen"),Run(c.Rescue));Item(windows.DropDownItems,L.T("Fensterpositionen zurückholen"),Run(()=>{c.RestoreWindows();return Task.CompletedTask;}),File.Exists(Store.FilePath("window-backup.json")));windows.DropDownItems.Add(new Forms.ToolStripSeparator());Item(windows.DropDownItems,L.T("Monitore identifizieren"),()=>c.Overlays.Identify(c.Monitors));Item(windows.DropDownItems,L.T("Monitore neu erkennen"),Run(c.Refresh));Item(windows.DropDownItems,L.T("Testbilder schließen"),c.Overlays.ClosePatterns);Item(windows.DropDownItems,L.T("Bildschirme in Standby"),Run(async()=>{await Task.Delay(1000);Native.SendMessage(new WindowInteropHelper(this).Handle,0x112,new IntPtr(0xF170),new IntPtr(2));}));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var design=Item(menu.Items,L.T("App-Design"));foreach(var option in new[]{("Light",L.T("Hell")),("Dark",L.T("Dunkel")),("System","System")})Item(design.DropDownItems,option.Item2,()=>SetAppearance(option.Item1),check:Theme.Mode==option.Item1);
        Item(windows.DropDownItems,L.T("DDC/CI-Funktionen erneut prüfen"),Run(c.RefreshCapabilities));
        var language=Item(menu.Items,L.T("Sprache"));foreach(var option in new[]{("de","Deutsch"),("en","English")})Item(language.DropDownItems,option.Item2,()=>SetLanguage(option.Item1),check:c.Settings.Language==option.Item1);
        var background=Item(menu.Items,L.T("Hintergrund & Zeitplan"));
        Item(background.DropDownItems,L.T("Mit Windows starten"),()=>{SetStartWithWindows(!c.Settings.StartWithWindows);Render();BuildTrayMenu();},check:c.Settings.StartWithWindows);
        Item(background.DropDownItems,L.T("Beim Minimieren ins Tray"),()=>{c.Settings.MinimizeToTray=!c.Settings.MinimizeToTray;c.SaveSettings();Render();BuildTrayMenu();},check:c.Settings.MinimizeToTray);
        Item(background.DropDownItems,L.T("Fenster im Fokusmodus einsammeln"),()=>{c.Settings.MoveWindowsInFocus=!c.Settings.MoveWindowsInFocus;c.SaveSettings();Render();BuildTrayMenu();},check:c.Settings.MoveWindowsInFocus);
        background.DropDownItems.Add(new Forms.ToolStripSeparator());Item(background.DropDownItems,L.T("Helligkeitszeitplan aktiv"),()=>{c.Settings.BrightnessSchedule=!c.Settings.BrightnessSchedule;lastScheduleNight=null;c.SaveSettings();c.Report(c.Settings.BrightnessSchedule?L.T("Zeitplan aktiviert. Prüfung spätestens in 30 Sekunden."):L.T("Zeitplan deaktiviert."));Render();BuildTrayMenu();},check:c.Settings.BrightnessSchedule);Item(background.DropDownItems,$"{c.Settings.DayTime} → {c.Settings.DayBrightness} % · {c.Settings.NightTime} → {c.Settings.NightBrightness} %",enabled:false);Item(background.DropDownItems,L.T("Zeiten & Tastenkürzel …"),()=>{ShowApp();Navigate("settings");});
        Item(menu.Items,L.T("Einstellungen …"),()=>{ShowApp();Navigate("settings");});menu.Items.Add(new Forms.ToolStripSeparator());Item(menu.Items,L.T("MonitorPilot beenden"),ExitApp,!c.Busy);
        TrayRenderer.Apply(menu);return menu;
    }
    private void SetStartWithWindows(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");if(enabled)key.SetValue("MonitorPilot","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("MonitorPilot",false);c.Settings.StartWithWindows=enabled;c.SaveSettings();c.Report(enabled?L.T("Windows-Autostart aktiviert."):L.T("Windows-Autostart deaktiviert."));
    }
}
