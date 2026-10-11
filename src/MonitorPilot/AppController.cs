using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MonitorPilot;

internal sealed class AppController
{
    public readonly DisplayService Displays=new();
    public readonly HardwareService Hardware=new();
    public readonly WindowService Windows=new();
    public readonly OverlayService Overlays=new();
    public readonly LiveControlQueue Live;
    public AppSettings Settings {get;set;}=new();
    public List<MonitorDevice> Monitors {get;private set;}=new();
    public List<ProfileEntry> Profiles {get;private set;}=new();
    public WorkspaceSnapshot? FocusBackup {get;private set;}
    public bool Busy {get;private set;}
    public bool Discovering {get;private set;}
    public Window? Owner {get;set;}
    public string Status {get;private set;}=L.T("MonitorPilot wird gestartet …");
    public bool Error {get;private set;}
    public event Action? Changed;
    public event Action? StatusChanged;
    public event Action? HardwareDiscovered;
    public event Action? HardwareChanged;
    public event Action? LiveSettled;
    public AppController(Func<MonitorDevice,HardwareState,Task>? applyLive=null)
    {
        Live=new(applyLive??(async(m,desired)=>
        {
            try{await Task.Run(()=>Hardware.Apply(m,desired));}
            catch{await Task.Run(()=>Hardware.Probe(m));throw;}
        }));
        Live.Completed+=(m,error)=>Dispatch(()=>
        {
            if(error!=null)Report(Name(m)+": "+error.Message,true);
            HardwareChanged?.Invoke();
        });
        Live.Settled+=()=>Dispatch(()=>
        {
            if(Live.IsRunning)return;
            if(!Busy&&!Error){Status=L.T("Live übernommen · Monitorwerte bestätigt.");StatusChanged?.Invoke();}
            LiveSettled?.Invoke();
        });
    }
    private static void Dispatch(Action action)
    {
        var dispatcher=System.Windows.Application.Current?.Dispatcher;
        if(dispatcher==null||dispatcher.CheckAccess())action();
        else if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(action);
    }
    public int? ControlPercent(MonitorDevice m,MonitorControl control)=>Live.DesiredPercent(m.Key,control)??LiveControlQueue.Control(m.Hardware,control)?.Percent;
    public void LiveControl(MonitorDevice m,MonitorControl control,int percent)
    {
        if(Busy)return;bool idle=!Live.IsRunning;Live.Enqueue(m,control,percent);LiveQueued(idle);
    }
    public void LiveTemperature(MonitorDevice m,uint temperature)
    {
        if(Busy)return;bool idle=!Live.IsRunning;Live.EnqueueTemperature(m,temperature);LiveQueued(idle);
    }
    public void LiveBrightness(int percent)
    {
        if(Busy)return;bool idle=!Live.IsRunning;
        foreach(var m in Monitors.Where(m=>m.Active))Live.Enqueue(m,MonitorControl.Brightness,percent);
        LiveQueued(idle);
    }
    private void LiveQueued(bool idle)
    {
        if((idle||Error)&&Live.IsRunning){Status=L.T("Wird live übernommen …");Error=false;StatusChanged?.Invoke();}
        HardwareChanged?.Invoke();
    }
    public void SetDimming(MonitorDevice m,int percent)
    {
        if(Busy)return;Overlays.Set(m,percent);HardwareChanged?.Invoke();
    }
    public MonitorDevice? Main => Monitors.FirstOrDefault(m=>m.Active && m.Key==Settings.PreferredMain) ?? Monitors.FirstOrDefault(m=>m.Primary) ?? Monitors.FirstOrDefault(m=>m.Active);
    public void Load()
    {
        var recovered=new List<string>();
        Settings=Store.ReadRecoverable<AppSettings>("settings.json",recovered:recovered.Add)??new();Settings.Normalize();L.SetLanguage(Settings.Language);
        Profiles=Store.ReadRecoverable<List<ProfileEntry>>("profiles.json",profiles=>
        {
            if(profiles.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Id))||profiles.Select(p=>p.Id).Distinct().Count()!=profiles.Count)throw new InvalidDataException(L.T("Ungültige Profilübersicht."));
            foreach(var profile in profiles)Store.Validate(profile.Workspace);
        },recovered.Add)??new();
        FocusBackup=Store.ReadRecoverable<WorkspaceSnapshot>("focus-workspace.json",Store.Validate,recovered.Add);
        if(recovered.Count>0)Report(L.T("Beschädigte Daten wurden im Datenordner als .bak gesichert. Andere Einstellungen und Profile bleiben erhalten."),true);
    }
    public void SaveSettings()=>Store.Save("settings.json",Settings);
    public string Name(MonitorDevice monitor)=>Settings.Aliases.TryGetValue(monitor.Key,out var alias)&&!string.IsNullOrWhiteSpace(alias)?alias:monitor.Name;
    public void Report(string message,bool error=false) {Status=message;Error=error;Store.Log(message);StatusChanged?.Invoke();}
    public async Task Run(Func<Task> operation)
    {
        if(Busy) {Report(L.T("Eine Monitoraktion läuft bereits. Bitte kurz warten."));return;}
        var elapsed=Stopwatch.StartNew();
        Busy=true;StatusChanged?.Invoke();
        try {await Live.DrainAsync();await operation();}
        catch(Exception ex) {Report(ex.Message,true);Store.Log(ex.ToString());}
        finally {Busy=false;StatusChanged?.Invoke();Changed?.Invoke();Store.Log($"Action finished including UI refresh: {elapsed.Elapsed.TotalMilliseconds:0} ms");}
    }
    public async Task Refresh()
        =>await Refresh(false);
    public async Task RefreshCapabilities()=>await Refresh(true);
    private async Task Refresh(bool forceCapabilities)
    {
        Report(forceCapabilities?L.T("DDC/CI-Funktionslisten werden erneut geprüft. Manche Monitore brauchen dafür mehrere Sekunden …"):L.T("Aktuelle Monitorwerte werden ausgelesen …"));
        DisplayService.InvalidateModes();
        var list=await Task.Run(Displays.Enumerate);
        foreach(var monitor in list)
        {
            var previous=Monitors.FirstOrDefault(m=>m.Key==monitor.Key);
            if(previous!=null)monitor.Hardware=Store.Clone(previous.Hardware);
        }
        await Task.WhenAll(list.Where(m=>m.Active).Select(m=>Task.Run(()=>Hardware.Probe(m,forceCapabilities))));
        Monitors=list;
        Overlays.Reposition(Monitors);
        Report(L.F($"{Monitors.Count(m=>m.Active)} aktive Monitore · {Monitors.Count(m=>m.Hardware.Brightness!=null)} mit Hardware-Helligkeit · Lokal verbunden"));
    }
    public async Task Initialize()
    {
        var elapsed=Stopwatch.StartNew();Discovering=true;
        try
        {
            Monitors=await Task.Run(Displays.Enumerate);var list=Monitors;
            Overlays.Reposition(list);Report(L.T("Monitore verbunden. Helligkeit wird erkannt …"));Changed?.Invoke();
            var active=list.Where(m=>m.Active).ToList();
            await Task.WhenAll(active.Select(m=>Task.Run(()=>Hardware.ProbeBrightness(m))));
            if(!ReferenceEquals(list,Monitors))return;
            HardwareDiscovered?.Invoke();Store.Log($"Startup brightness ready: {elapsed.Elapsed.TotalMilliseconds:0} ms");
            if(!Busy)Report(L.T("Helligkeit bereit. Weitere Monitorfunktionen werden im Hintergrund erkannt …"));
            var main=Main;
            var modes=Task.Run(()=>{if(main!=null)DisplayService.Modes(main.DeviceName);});
            await Task.WhenAll(active.Select(m=>Task.Run(()=>Hardware.Probe(m))).Append(modes));
            if(!ReferenceEquals(list,Monitors))return;
            Discovering=false;HardwareDiscovered?.Invoke();
            Store.Log($"Startup hardware ready: {elapsed.Elapsed.TotalMilliseconds:0} ms");
            if(!Busy)Report(L.F($"{active.Count} aktive Monitore · {active.Count(m=>m.Hardware.Brightness!=null)} mit Hardware-Helligkeit · Lokal verbunden"));
        }
        finally{if(Discovering){Discovering=false;HardwareDiscovered?.Invoke();}}
    }
    public async Task<WorkspaceSnapshot> Capture(string name)
    {
        await Live.DrainAsync();
        var levels=new Dictionary<string,int>(Overlays.Levels);
        var known=Monitors.ToDictionary(m=>m.Key,m=>Store.Clone(m.Hardware));
        return await Task.Run(async()=>
        {
            var monitors=Displays.Enumerate();
            foreach(var monitor in monitors)if(known.TryGetValue(monitor.Key,out var state))monitor.Hardware=state;
            var snapshot=new WorkspaceSnapshot {Name=name,Topology=Displays.Capture(),Windows=Windows.Capture(monitors),Dimming=levels};
            var activeKeys=snapshot.Topology.Identities.Select(i=>i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            snapshot.Dimming=levels.Where(p=>activeKeys.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
            var active=monitors.Where(m=>m.Active).ToList();
            await Task.WhenAll(active.Select(m=>Task.Run(()=>Hardware.Probe(m))));
            foreach(var m in active)snapshot.Hardware[m.Key]=m.Hardware;
            Store.Validate(snapshot);return snapshot;
        });
    }
    private async Task<bool> Switch(WorkspaceSnapshot before,Func<Task> action)
    {
        Store.Save("last-workspace.json",before);
        using var guard=new RecoveryGuard();
        await guard.Arm(before);
        try
        {
            await action();
            bool keep=new ConfirmWindow(guard) {Owner=Owner}.ShowDialog()==true;
            if(keep && await guard.Accept()) return true;
            var rollback=await guard.Rollback();
            if(!rollback.Restored) throw new InvalidOperationException(L.T("Wiederherstellung fehlgeschlagen: ")+rollback.Error+L.T(". Über Windows + P die Anzeige erweitern."));
            Report(L.T("Vorherige Monitore und Fenster wurden wiederhergestellt."));return false;
        }
        catch
        {
            var rollback=await guard.Rollback();
            if(!rollback.Restored) Store.Log("Rollback failed: "+rollback.Error);
            throw;
        }
    }
    public async Task Focus()
    {
        var main=Main ?? throw new InvalidOperationException(L.T("Kein aktiver Hauptmonitor vorhanden."));
        if(Monitors.Count(m=>m.Active)==1) {await Restore();return;}
        Report(L.T("Arbeitsplatz, Fenster und Monitorwerte werden gesichert …"));
        var before=await Capture(L.T("Vor Fokusmodus"));
        var target=DisplayService.Select(before.Topology,new[]{main.Key},main.Key);
        Displays.Validate(Displays.Resolve(target));
        var prior=FocusBackup;
        if(FocusBackup==null || before.Topology.Identities.Count>=FocusBackup.Topology.Identities.Count) {FocusBackup=before;Store.Save("focus-workspace.json",before);}
        bool kept=false;
        try
        {
            kept=await Switch(before,async()=>
            {
                if(Settings.MoveWindowsInFocus) await Task.Run(()=>Windows.Gather(main,before.Windows));
                await Task.Run(()=>Displays.Apply(target));
            });
        }
        finally
        {
            if(!kept) SetFocusBackup(prior);
            await Refresh();
        }
        if(kept) Report(L.F($"Fokusmodus aktiv auf {Name(main)}. {before.Windows.Count} Fensterpositionen und {before.Topology.Identities.Count} Monitore sind gesichert."));
    }
    private void SetFocusBackup(WorkspaceSnapshot? snapshot)
    {
        FocusBackup=snapshot;
        if(snapshot==null) {var path=Store.FilePath("focus-workspace.json");if(File.Exists(path))File.Delete(path);}
        else Store.Save("focus-workspace.json",snapshot);
    }
    public async Task Restore()
    {
        var saved=FocusBackup??Store.Read<WorkspaceSnapshot>("last-workspace.json");
        if(saved==null) throw new InvalidOperationException(L.T("Noch keine Arbeitsplatzsicherung vorhanden. Zum ersten Aktivieren bitte „Alle erweitern“ verwenden."));
        bool restored=await ApplyWorkspace(saved);
        if(restored) SetFocusBackup(null);
    }
    public async Task<bool> ApplyWorkspace(WorkspaceSnapshot saved)
    {
        Store.Validate(saved);Displays.Validate(Displays.Resolve(saved.Topology));
        var before=await Capture(L.T("Vor ")+saved.Name);
        (int Restored,int Skipped) windowResult=(0,0);
        bool keep=await Switch(before,async()=>
        {
            await Task.Run(()=>Displays.Apply(saved.Topology));await Task.Delay(1000);
            windowResult=await Task.Run(()=>Windows.Restore(saved.Windows));
        });
        await Refresh();
        if(!keep) return false;
        var results=await Task.WhenAll(Monitors.Where(m=>m.Active&&saved.Hardware.ContainsKey(m.Key)).Select(async m=>
        {
            try {await Task.Run(()=>Hardware.Apply(m,saved.Hardware[m.Key]));return (string?)null;}
            catch(Exception ex) {return Name(m)+": "+ex.Message;}
        }));
        var failures=results.Where(e=>e!=null).ToList();
        Overlays.Restore(saved.Dimming,Monitors);
        Report(L.F($"{saved.Name} wiederhergestellt · {windowResult.Restored} Fenster zugeordnet")+(windowResult.Skipped>0?L.F($" · {windowResult.Skipped} inzwischen geschlossen/nicht erreichbar"):"")+(failures.Count>0?L.T(". Monitorwerte teilweise offen: ")+string.Join("; ",failures):L.T(". Auflösung, Anordnung und Monitorwerte geladen.")),failures.Count>0);
        return failures.Count==0;
    }
    public async Task SelectMonitors(List<string> keys,string primary)
    {
        var before=await Capture(L.T("Vor Monitorauswahl"));
        var baseline=keys.All(k=>before.Topology.Identities.Any(i=>i.Key==k))?before.Topology:FocusBackup?.Topology;
        if(baseline==null) throw new InvalidOperationException(L.T("Zuerst „Alle erweitern“ verwenden, damit die Anordnung dieses Monitors bekannt ist."));
        var target=DisplayService.Select(baseline,keys,primary);Displays.Validate(Displays.Resolve(target));
        var prior=FocusBackup;bool kept=false;
        if(keys.Count<before.Topology.Identities.Count && (FocusBackup==null||before.Topology.Identities.Count>=FocusBackup.Topology.Identities.Count))SetFocusBackup(before);
        try {kept=await Switch(before,()=>Task.Run(()=>Displays.Apply(target)));}
        finally {if(!kept)SetFocusBackup(prior);await Refresh();}
        if(kept) Report(L.T("Monitorauswahl übernommen. Vorheriger Arbeitsplatz bleibt wiederherstellbar."));
    }
    public async Task Extend()
    {
        var before=await Capture(L.T("Vor Erweitern"));
        bool kept=await Switch(before,()=>Task.Run(()=>Displays.Extend()));await Refresh();
        if(kept)Report(L.T("Alle verfügbaren Monitore sind erweitert. Für die alte Anordnung und Fenster „Arbeitsplatz wiederherstellen“ verwenden."));
    }
    public async Task ApplyLayout(TopologySnapshot layout)
    {
        Displays.Validate(Displays.Resolve(layout));var before=await Capture(L.T("Vor Anordnung"));
        bool kept=await Switch(before,()=>Task.Run(()=>Displays.Apply(layout)));await Refresh();if(kept)Report(L.T("Monitoranordnung übernommen."));
    }
    public async Task ChangeMode(MonitorDevice monitor,Native.DevMode mode,uint rotation)
    {
        var before=await Capture(L.T("Vor Displaymodus"));
        bool kept=await Switch(before,()=>Task.Run(()=>Displays.ChangeMode(monitor.DeviceName,mode,rotation)));await Refresh();if(kept)Report(L.T("Auflösung, Bildwiederholrate und Ausrichtung übernommen."));
    }
    public async Task ApplyHardware(MonitorDevice monitor,HardwareState desired,int dim)
    {
        Report(L.T("Monitorwerte werden gesetzt und zurückgelesen …"));
        try {await Task.Run(()=>Hardware.Apply(monitor,desired));Overlays.Set(monitor,dim);Report(L.F($"{Name(monitor)}: Monitorwerte bestätigt · Softwaredimmung {dim} %"));}
        catch {await Task.Run(()=>Hardware.Probe(monitor));throw;}
    }
    public async Task Brightness(int value,bool relative=false)
        =>await SetBrightness(Monitors.Where(m=>m.Active&&m.Hardware.Brightness!=null).Select(m=>(Monitor:m,Value:value)).ToArray(),relative);
    public async Task Brightness(IReadOnlyDictionary<string,int> levels)
        =>await SetBrightness(Monitors.Where(m=>m.Active&&m.Hardware.Brightness!=null&&levels.ContainsKey(m.Key)).Select(m=>(Monitor:m,Value:levels[m.Key])).ToArray(),false);
    private async Task SetBrightness((MonitorDevice Monitor,int Value)[] targets,bool relative)
    {
        Report(L.T("Helligkeit wird gesetzt und bestätigt …"));
        var results=await Task.WhenAll(targets.Select(async target=>
        {
            try {await Task.Run(()=>Hardware.ApplyBrightness(target.Monitor,target.Value,relative));return (string?)null;}
            catch(Exception ex) {return Name(target.Monitor)+": "+ex.Message;}
        }));
        int done=results.Count(e=>e==null);var failed=results.Where(e=>e!=null).ToList();
        if(done==0 && failed.Count==0) throw new InvalidOperationException(L.T("Kein Monitor mit erreichbarer Hardware-Helligkeit. DDC/CI im Bildschirmmenü aktivieren."));
        Report(L.F($"Helligkeit an {done} Monitoren bestätigt.")+(failed.Count>0?L.T(" Offen: ")+string.Join("; ",failed):""),failed.Count>0);
    }
    public async Task SaveProfile(string name)
    {
        if(string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(L.T("Bitte einen Profilnamen eingeben."));
        var snapshot=await Capture(name.Trim());Profiles.Add(new() {Workspace=snapshot});Store.Save("profiles.json",Profiles);Report(L.F($"Profil „{name.Trim()}“ gespeichert: {snapshot.Topology.Identities.Count} Monitore und {snapshot.Windows.Count} Fenster."));
    }
    public void Import(WorkspaceSnapshot workspace)
    {
        Store.Validate(workspace);Profiles.Add(new(){Workspace=workspace});Store.Save("profiles.json",Profiles);Changed?.Invoke();Report(L.T("Profil importiert. Es wird erst beim Anwenden aktiviert."));
    }
    public void DeleteProfile(ProfileEntry profile) {Profiles.Remove(profile);Store.Save("profiles.json",Profiles);Changed?.Invoke();Report(L.T("Profil entfernt."));}
    public async Task Rescue()
    {
        var main=Main??throw new InvalidOperationException(L.T("Kein Hauptmonitor verfügbar."));
        var windows=Windows.Capture(Monitors);Store.Save("window-backup.json",windows);
        int count=await Task.Run(()=>Windows.Gather(main,windows));Report(L.F($"{count} Fenster auf {Name(main)} geholt. Ursprüngliche Positionen separat gesichert."));
    }
    public void RestoreWindows()
    {
        var backup=Store.Read<List<SavedWindow>>("window-backup.json")??throw new InvalidOperationException(L.T("Noch keine Fenstersicherung vorhanden."));
        var result=Windows.Restore(backup);Report(L.F($"{result.Restored} Fensterpositionen wiederhergestellt · {result.Skipped} nicht mehr verfügbar."));
    }
    public async Task OriginalHardware(MonitorDevice monitor)
    {
        var originals=Store.Read<Dictionary<string,HardwareState>>("hardware-original.json");
        if(originals==null || !originals.TryGetValue(monitor.Key,out var original)) throw new InvalidOperationException(L.T("Für diesen Monitor gibt es noch keine Hardwareänderung von MonitorPilot."));
        await ApplyHardware(monitor,original,0);
    }
    public static bool IsNight(TimeSpan now,TimeSpan day,TimeSpan night)=>day<night ? now<day||now>=night : now>=night && now<day;
}

internal sealed class ConfirmWindow : Window
{
    public ConfirmWindow(RecoveryGuard guard)
    {
        Title=L.T("MonitorPilot · Änderung bestätigen");Width=570;Height=290;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;Topmost=true;Theme.Decorate(this);
        var counter=Theme.Label("",16,Theme.Accent,true);
        Content=new Border {Padding=new Thickness(28),Child=Theme.Stack(Theme.Label(L.T("Ist alles gut sichtbar?"),25,null,true),Theme.Label(L.T("Bestätige die neue Bildschirmkonfiguration. Ohne Bestätigung stellt ein separater Prozess die vorherigen Monitore und Fenster wieder her."),14,Theme.Muted),counter,Theme.Row(Theme.Button(L.T("Änderung behalten"),()=>DialogResult=true,true),Theme.Button(L.T("Sofort zurücksetzen"),()=>DialogResult=false)))};
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(200)};
        void Update() {int left=Math.Max(0,RecoveryGuard.TimeoutSeconds-1-(int)guard.Elapsed.Elapsed.TotalSeconds);counter.Text=L.F($"Automatische Wiederherstellung in {left} Sekunden");if(left==0){timer.Stop();DialogResult=false;}}
        timer.Tick+=(_,_)=>Update();Loaded+=(_,_)=>{Update();timer.Start();Activate();};Closed+=(_,_)=>timer.Stop();PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)DialogResult=false;};
    }
}
