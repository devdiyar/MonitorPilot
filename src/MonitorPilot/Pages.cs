using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;

namespace MonitorPilot;
internal sealed partial class MainWindow
{
    private UIElement ProfilesPage()
    {
        var content=new StackPanel();var name=new TextBox{Text="Arbeiten",Width=300};AutomationProperties.SetName(name,"Profilname");
        content.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Aktuellen Arbeitsplatz sichern",18,null,true),Theme.Label("Speichert Monitore, Anordnung, Auflösung, Hz, Farben, Dimmung und Fensterpositionen.",13,Theme.Muted),Theme.Row(name,ActionButton("Profil speichern",()=>c.SaveProfile(name.Text),true),Theme.Button("Importieren …",ImportProfile)))));
        foreach(var entry in c.Profiles.ToArray())
        {
            var s=entry.Workspace;var row=new DockPanel();var actions=Theme.Row(ActionButton("Anwenden",async()=>{await c.ApplyWorkspace(s);},true),Theme.Button("Exportieren",()=>ExportProfile(s)),Theme.Button("Entfernen",()=>{if(MessageBox.Show(this,$"Profil „{s.Name}“ entfernen?","Profil entfernen",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)c.DeleteProfile(entry);}));actions.VerticalAlignment=VerticalAlignment.Center;actions.MaxWidth=320;DockPanel.SetDock(actions,Dock.Right);row.Children.Add(actions);
            var description=Theme.Stack(Theme.Label(s.Name,17,null,true),Theme.Label($"{s.Topology.Identities.Count} Monitore · {s.Windows.Count} Fenster · {s.CapturedAt:dd.MM.yyyy HH:mm}",12,Theme.Muted),Theme.Label(string.Join(" · ",s.Topology.Identities.Select(i=>i.Name)),11,Theme.Muted));description.Margin=new Thickness(0,0,20,0);row.Children.Add(description);content.Children.Add(Theme.Panel(row));
        }
        content.Children.Add(Theme.Label("Fensterpositionen lassen sich wiederherstellen, solange dieselben Programme noch geöffnet sind. Fehlende Monitoranschlüsse werden vor dem Anwenden geprüft.",12,Theme.Muted));return content;
    }
    private UIElement CalibrationPage()
    {
        var content=new StackPanel();var patterns=new WrapPanel();foreach(string pattern in new[]{"Graustufen","Farbfelder","Weiß","Schwarz","Rot","Grün","Blau"})patterns.Children.Add(Theme.Button(pattern,()=>c.Overlays.Patterns(c.Monitors,pattern)));
        content.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Dasselbe Testbild auf allen Monitoren",18,null,true),Theme.Label("Vergleiche Helligkeit, Graustufen und Farbstiche direkt nebeneinander.",13,Theme.Muted),patterns,Theme.Label("Esc schließt alle Testbilder. Sie enden automatisch nach 5 Minuten.",12,Theme.Muted),Theme.Row(Theme.Button("Testbilder schließen",c.Overlays.ClosePatterns),Theme.Button("Softwaredimmung entfernen",()=>{c.Overlays.Clear();c.Report("Softwaredimmung entfernt.");})))));
        var steps=new[]{("01","Helligkeit angleichen","Weiß anzeigen und die Hardware-Helligkeit pro Monitor angleichen. Unterschiedliche Bildmodi und dynamischen Kontrast im Monitormenü ausschalten."),("02","Farbstiche reduzieren","Graustufen vergleichen. Unter „Bild & Farbe“ Rot, Grün und Blau schrittweise abstimmen. Möglichst aus deinem normalen Blickwinkel vergleichen."),("03","Arbeitsprofil sichern","Farbflächen und echte Fotos prüfen. Danach die abgestimmten Werte mit deiner Anordnung als Arbeitsprofil speichern."),("04","Farben exakt vermessen","Für verbindliche Farben ein Colorimeter und passende ICC-Profile verwenden. Ein visueller Vergleich ersetzt keine Messung.")};
        for(int i=0;i<steps.Length;i+=2){Border Card(int n){var s=steps[n];return Theme.Panel(Theme.Stack(Theme.Label(s.Item1,12,Theme.Accent,true),Theme.Label(s.Item2,16,null,true),Theme.Label(s.Item3,13,Theme.Muted)));}content.Children.Add(Theme.Columns(Card(i),Card(i+1),0));}
        content.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Windows-Farbwerkzeuge",17,null,true),Theme.Row(Theme.Button("Farbverwaltung",()=>Open("colorcpl.exe")),Theme.Button("Kalibrierung",()=>Open("dccw.exe")),Theme.Button("HDR & Anzeige",()=>Open("ms-settings:display")),Theme.Button("Nachtmodus",()=>Open("ms-settings:nightlight")),Theme.Button("Farbfilter",()=>Open("ms-settings:easeofaccess-colorfilter"))),Theme.Label("ICC-Profile und Gamma-Kurven bleiben in der Windows-Farbverwaltung. Die Paneltechnik und HDR können den erreichbaren Abgleich begrenzen.",12,Theme.Muted))));return content;
    }
    private UIElement SettingsPage()
    {
        var content=new StackPanel();content.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("App-Design",17,null,true),Theme.Label("Oben rechts kannst du jederzeit Hell, Dunkel oder System wählen. Die Auswahl wird sofort gespeichert und gilt auch für Listen, Regler und Dialoge.",13,Theme.Muted))));
        var startup=new CheckBox{Content="Mit Windows starten (im Tray)",IsChecked=c.Settings.StartWithWindows};var minimize=new CheckBox{Content="Beim Minimieren im Tray weiterlaufen",IsChecked=c.Settings.MinimizeToTray};var gather=new CheckBox{Content="Fenster im Fokusmodus einsammeln",IsChecked=c.Settings.MoveWindowsInFocus};
        var background=Theme.Panel(Theme.Stack(Theme.Label("Hintergrundbetrieb",17,null,true),Theme.Label("Schließen blendet das Fenster aus. Tray-Menü und globale Tastenkürzel bleiben erreichbar.",12,Theme.Muted),startup,minimize,gather,Theme.Button("Hintergrundoptionen speichern",()=>
        {
            try{using var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");if(startup.IsChecked==true)key.SetValue("MonitorPilot","\""+Environment.ProcessPath+"\" --tray");else key.DeleteValue("MonitorPilot",false);c.Settings.StartWithWindows=startup.IsChecked==true;c.Settings.MinimizeToTray=minimize.IsChecked==true;c.Settings.MoveWindowsInFocus=gather.IsChecked==true;c.SaveSettings();c.Report("Hintergrundeinstellungen gespeichert.");}catch(Exception ex){c.Report(ex.Message,true);}
        })));
        var schedule=new CheckBox{Content="Helligkeit automatisch wechseln",IsChecked=c.Settings.BrightnessSchedule};
        TextBox Field(string text,string label,int width){var field=new TextBox{Text=text,Width=width};AutomationProperties.SetName(field,label);return field;}
        var dayTime=Field(c.Settings.DayTime,"Tag beginnt um",76);var nightTime=Field(c.Settings.NightTime,"Abend beginnt um",76);var dayValue=Field(c.Settings.DayBrightness.ToString(),"Helligkeit am Tag",60);var nightValue=Field(c.Settings.NightBrightness.ToString(),"Helligkeit am Abend",60);
        UIElement TimeRow(string label,TextBox time,TextBox value){var title=Theme.Label(label,12);title.Width=62;title.Margin=new Thickness(0,13,0,0);var percent=Theme.Label("%",12,Theme.Muted);percent.Margin=new Thickness(0,13,0,0);return Theme.Row(title,time,value,percent);}
        var timing=Theme.Panel(Theme.Stack(Theme.Label("Helligkeit nach Uhrzeit",17,null,true),Theme.Label("Lokale Uhrzeit. Die App muss dafür im Hintergrund laufen.",12,Theme.Muted),schedule,TimeRow("Tag ab",dayTime,dayValue),TimeRow("Abend ab",nightTime,nightValue),Theme.Button("Zeitplan speichern",()=>
        {
            if(!TimeSpan.TryParseExact(dayTime.Text,"hh\\:mm",CultureInfo.InvariantCulture,out var day)||!TimeSpan.TryParseExact(nightTime.Text,"hh\\:mm",CultureInfo.InvariantCulture,out var night)||day==night||!int.TryParse(dayValue.Text,out int dv)||!int.TryParse(nightValue.Text,out int nv)||dv<0||dv>100||nv<0||nv>100){c.Report("Zwei verschiedene Uhrzeiten im Format HH:mm und Helligkeit von 0 bis 100 eingeben.",true);return;}
            c.Settings.BrightnessSchedule=schedule.IsChecked==true;c.Settings.DayTime=dayTime.Text;c.Settings.NightTime=nightTime.Text;c.Settings.DayBrightness=dv;c.Settings.NightBrightness=nv;c.SaveSettings();lastScheduleNight=null;c.Report("Zeitplan gespeichert. Prüfung erfolgt spätestens in 30 Sekunden.");
        })));
        content.Children.Add(Theme.Columns(background,timing,0));var keys=Theme.Stack(Theme.Label("Globale Tastenkürzel",17,null,true),Theme.Label("Funktionieren auch im Hintergrund. Belegte Kürzel werden beim Speichern gemeldet.",12,Theme.Muted));if(!string.IsNullOrEmpty(hotkeyProblem))keys.Children.Add(Theme.Label(hotkeyProblem,12,Theme.Danger));
        foreach(var key in c.Settings.Hotkeys)
        {
            var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(245)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var title=Theme.Label(key.Label,13);title.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(title);var label=Theme.Label(HotkeyService.Format(key),12,Theme.Accent,true);label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,1);row.Children.Add(label);var edit=Theme.Button("Ändern",()=>EditHotkey(key));edit.Margin=new Thickness(8,0,0,0);edit.Padding=new Thickness(12,6,12,6);edit.MinHeight=30;AutomationProperties.SetName(edit,"Tastenkürzel ändern: "+key.Label);Grid.SetColumn(edit,2);row.Children.Add(edit);keys.Children.Add(row);
        }
        content.Children.Add(Theme.Panel(keys));content.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Daten & Diagnose",17,null,true),Theme.Label("Einstellungen, Profile und Wiederherstellung werden lokal gespeichert.",12,Theme.Muted),Theme.Row(Theme.Button("Datenordner",()=>Open(Store.Root)),Theme.Button("Aktivitätsprotokoll",()=>Open(Store.FilePath("activity.log"))),Theme.Button("Diagnose exportieren …",ExportDiagnostics)),Theme.Row(Theme.Button("Alle Bildschirme in Standby",()=>_=c.Run(async()=>{c.Report("Standby in 3 Sekunden. Maus oder Tastatur weckt die Bildschirme wieder.");await Task.Delay(3000);Native.SendMessage(new WindowInteropHelper(this).Handle,0x112,new IntPtr(0xF170),new IntPtr(2));})),Theme.Button("MonitorPilot beenden",ExitApp)),Theme.Label("Standby behält die Desktop-Anordnung bei. Der Fokusmodus deaktiviert zusätzliche Grafikausgänge.",11,Theme.Muted))));return content;
    }
}
