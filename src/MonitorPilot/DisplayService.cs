using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace MonitorPilot;

internal sealed class DisplayService
{
    public static byte[] Pack<T>(T[] items) where T : unmanaged => MemoryMarshal.AsBytes(items.AsSpan()).ToArray();
    public static T[] Unpack<T>(byte[] data) where T : unmanaged
    {
        if(data.Length % Marshal.SizeOf<T>() != 0) throw new ArgumentException(L.T("Ungültige native Datenlänge."));
        return MemoryMarshal.Cast<byte,T>(data.AsSpan()).ToArray();
    }
    public static (Native.DisplayPath[] Paths, Native.DisplayMode[] Modes) Query(uint flags=2)
    {
        for(int attempt=0; attempt < 5; attempt++)
        {
            Check(Native.GetDisplayConfigBufferSizes(flags,out uint pc,out uint mc), L.T("Monitorliste"));
            var paths = new Native.DisplayPath[pc]; var modes = new Native.DisplayMode[mc];
            int result=Native.QueryDisplayConfig(flags,ref pc,paths,ref mc,modes,IntPtr.Zero);
            if(result==122) continue;
            Check(result,L.T("Bildschirmkonfiguration"));
            return (paths.Take((int)pc).ToArray(), modes.Take((int)mc).ToArray());
        }
        throw new InvalidOperationException(L.T("Die Monitore ändern sich gerade. Bitte erneut erkennen."));
    }
    public static Native.TargetName Target(Native.DisplayPath path)
    {
        var name=new Native.TargetName { Header=new() { Type=2, Size=(uint)Marshal.SizeOf<Native.TargetName>(), Adapter=path.Target.Adapter, Id=path.Target.Id } };
        Check(Native.GetTargetName(ref name), L.T("Monitorname")); return name;
    }
    public static string Source(Native.DisplayPath path)
    {
        var name=new Native.SourceName { Header=new() { Type=1, Size=(uint)Marshal.SizeOf<Native.SourceName>(), Adapter=path.Source.Adapter, Id=path.Source.Id } };
        return Native.GetSourceName(ref name)==0 ? name.Name : "";
    }
    public TopologySnapshot Capture()
    {
        var q=Query();
        return new() { Paths=Pack(q.Paths), Modes=Pack(q.Modes), Identities=q.Paths.Select(p=> {var n=Target(p); return new PathIdentity { Key=n.DevicePath, Name=n.Friendly };}).ToList() };
    }
    public List<MonitorDevice> Enumerate()
    {
        var active=Query(); var all=Query(1);
        var handles=new Dictionary<string,(IntPtr Handle,Native.MonitorInfo Info)>(StringComparer.OrdinalIgnoreCase);
        Native.EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr h,IntPtr dc,ref Native.Rect rect,IntPtr data)=>
        {
            var mi=new Native.MonitorInfo { Size=Marshal.SizeOf<Native.MonitorInfo>() };
            if(Native.GetMonitorInfo(h,ref mi)) handles[mi.Device]=(h,mi);
            return true;
        },IntPtr.Zero);
        var list=new List<MonitorDevice>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in active.Paths.Concat(all.Paths.Where(p=>p.Target.Available!=0)))
        {
            Native.TargetName name;
            try { name=Target(p); } catch { continue; }
            if(string.IsNullOrWhiteSpace(name.DevicePath) || !seen.Add(name.DevicePath)) continue;
            bool enabled=(p.Flags & 1)!=0; string source=enabled ? Source(p) : "";
            var m=new MonitorDevice { Key=name.DevicePath,Name=string.IsNullOrWhiteSpace(name.Friendly)?"Monitor":name.Friendly,DeviceName=source,Active=enabled,Rotation=p.Target.Rotation,Connection=ConnectionName(p.Target.Output) };
            m.AdapterKey=$"{p.Target.Adapter.High}:{p.Target.Adapter.Low}";
            if(enabled && p.Source.ModeIndex < active.Modes.Length)
            {
                var mode=active.Modes[p.Source.ModeIndex].Source;
                m.X=mode.Position.X; m.Y=mode.Position.Y; m.Width=(int)mode.Width; m.Height=(int)mode.Height;
                m.Hz=p.Target.Refresh.Denominator>0 ? (double)p.Target.Refresh.Numerator/p.Target.Refresh.Denominator : 0;
            }
            if(handles.TryGetValue(source,out var h))
            {
                m.Handle=h.Handle; m.Primary=(h.Info.Flags & 1)!=0;
                // CCD source dimensions are pre-rotation; the desktop rectangle is authoritative for maps/overlays.
                m.X=h.Info.Monitor.Left;m.Y=h.Info.Monitor.Top;m.Width=h.Info.Monitor.Width;m.Height=h.Info.Monitor.Height;
            }
            var color=new Native.ColorInfo {Header=new() { Type=9,Size=(uint)Marshal.SizeOf<Native.ColorInfo>(),Adapter=p.Target.Adapter,Id=p.Target.Id }};
            if(enabled && Native.GetColorInfo(ref color)==0) { m.Hdr=(color.Flags & 2)!=0; m.Bits=color.Bits; }
            list.Add(m);
        }
        list=list.OrderByDescending(m=>m.Primary).ThenByDescending(m=>m.Active).ThenBy(m=>m.X).ThenBy(m=>m.Y).ToList();
        for(int i=0;i<list.Count;i++) list[i].Number=i+1;
        return list;
    }
    public TopologySnapshot Resolve(TopologySnapshot snapshot)
    {
        var old=Unpack<Native.DisplayPath>(snapshot.Paths); var modes=Unpack<Native.DisplayMode>(snapshot.Modes);
        if(old.Length != snapshot.Identities.Count || old.Length==0) throw new InvalidOperationException(L.T("Leere oder ungültige Monitoranordnung."));
        var live=Query(1).Paths.Where(x=>x.Target.Available!=0).Select(p=>(Path:p,Name:Target(p))).ToList();
        var used=new Dictionary<string,string>();
        for(int i=0;i<old.Length;i++)
        {
            string key=snapshot.Identities[i].Key;
            var candidates=live.Where(x=>x.Name.DevicePath.Equals(key,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.Path.Source.Id==old[i].Source.Id).ThenByDescending(x=>(x.Path.Flags&1)!=0).ToList();
            if(candidates.Count==0) throw new InvalidOperationException(L.F($"{snapshot.Identities[i].Name} ist nicht mehr am gespeicherten Anschluss verfügbar. Bitte anschließen oder ein neues Profil speichern."));
            string oldSource=$"{old[i].Source.Adapter.High}:{old[i].Source.Adapter.Low}:{old[i].Source.Id}";
            var match=candidates.FirstOrDefault(x=>!used.TryGetValue(SourceKey(x.Path),out var previous) || previous==oldSource);
            if(match.Name.DevicePath==null) throw new InvalidOperationException(L.T("Keine konfliktfreie Monitorzuordnung möglich."));
            used[SourceKey(match.Path)]=oldSource;
            var path=old[i]; path.Source.Adapter=match.Path.Source.Adapter; path.Source.Id=match.Path.Source.Id;
            path.Target.Adapter=match.Path.Target.Adapter; path.Target.Id=match.Path.Target.Id; path.Target.Available=1;
            path.Source.StatusFlags=match.Path.Source.StatusFlags; path.Target.StatusFlags=match.Path.Target.StatusFlags; path.Flags=1;
            if(path.Source.ModeIndex < modes.Length) { modes[path.Source.ModeIndex].Adapter=path.Source.Adapter; modes[path.Source.ModeIndex].Id=path.Source.Id; }
            if(path.Target.ModeIndex < modes.Length) { modes[path.Target.ModeIndex].Adapter=path.Target.Adapter; modes[path.Target.ModeIndex].Id=path.Target.Id; }
            old[i]=path;
        }
        return new() { Paths=Pack(old),Modes=Pack(modes),Identities=Store.Clone(snapshot.Identities) };
    }
    private static string SourceKey(Native.DisplayPath p)=>$"{p.Source.Adapter.High}:{p.Source.Adapter.Low}:{p.Source.Id}";
    public static TopologySnapshot Select(TopologySnapshot snapshot,IEnumerable<string> selected,string primary)
    {
        var wanted=selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(wanted.Count==0 || !wanted.Contains(primary)) throw new InvalidOperationException(L.T("Mindestens ein Monitor und ein aktiver Hauptmonitor sind erforderlich."));
        var paths=Unpack<Native.DisplayPath>(snapshot.Paths); var modes=Unpack<Native.DisplayMode>(snapshot.Modes);
        var indexes=Enumerable.Range(0,paths.Length).Where(i=>wanted.Contains(snapshot.Identities[i].Key)).OrderByDescending(i=>snapshot.Identities[i].Key.Equals(primary,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(indexes.Length != wanted.Count) throw new InvalidOperationException(L.T("Ein ausgewählter Monitor fehlt in der gesicherten Anordnung. Zuerst alle erweitern."));
        uint primaryIndex=paths[indexes[0]].Source.ModeIndex;
        if(primaryIndex >= modes.Length) throw new InvalidOperationException(L.T("Die Position des Hauptmonitors ist unbekannt."));
        var origin=modes[primaryIndex].Source.Position;
        foreach(uint modeIndex in indexes.Select(i=>paths[i].Source.ModeIndex).Distinct())
        {
            if(modeIndex>=modes.Length) throw new InvalidOperationException(L.T("Ein Monitormodus fehlt."));
            modes[modeIndex].Source.Position.X-=origin.X; modes[modeIndex].Source.Position.Y-=origin.Y;
        }
        return new() {Paths=Pack(indexes.Select(i=>paths[i]).ToArray()), Modes=Pack(modes), Identities=indexes.Select(i=>Store.Clone(snapshot.Identities[i])).ToList()};
    }
    public void Validate(TopologySnapshot snapshot)
    {
        var paths=Unpack<Native.DisplayPath>(snapshot.Paths); var modes=Unpack<Native.DisplayMode>(snapshot.Modes);
        Check(Native.SetDisplayConfig((uint)paths.Length,paths,(uint)modes.Length,modes,0x20|0x40),L.T("Konfiguration prüfen"));
    }
    public void Apply(TopologySnapshot snapshot)
    {
        var resolved=Resolve(snapshot); Validate(resolved);
        var paths=Unpack<Native.DisplayPath>(resolved.Paths); var modes=Unpack<Native.DisplayMode>(resolved.Modes);
        Check(Native.SetDisplayConfig((uint)paths.Length,paths,(uint)modes.Length,modes,0x20|0x80),L.T("Monitoranordnung anwenden"));
    }
    public void Extend() => Check(Native.SetDisplayConfig(0,null,0,null,0x80|0x4),L.T("Alle Monitore erweitern"));
    private static readonly object modesGate=new();
    private static readonly Dictionary<string,List<Native.DevMode>> modeLists=new(StringComparer.OrdinalIgnoreCase);
    internal static List<Native.DevMode> ModesFor(string device,Func<List<Native.DevMode>> read)
    {
        lock(modesGate)
        {
            if(!modeLists.TryGetValue(device,out var list)){list=read().ToList();modeLists[device]=list;}
            return list.ToList();
        }
    }
    public static void InvalidateModes() {lock(modesGate)modeLists.Clear();}
    public static List<Native.DevMode> Modes(string device)=>ModesFor(device,()=>ReadModes(device));
    private static List<Native.DevMode> ReadModes(string device)
    {
        var list=new List<Native.DevMode>();
        for(int i=0;i<2048;i++)
        {
            var mode=new Native.DevMode {Size=(ushort)Marshal.SizeOf<Native.DevMode>()};
            if(!Native.EnumDisplaySettings(device,i,ref mode)) break;
            if(mode.BitsPerPel>=32 && mode.Width>=640 && mode.Height>=480 && !list.Any(m=>m.Width==mode.Width && m.Height==mode.Height && m.Frequency==mode.Frequency)) list.Add(mode);
        }
        return list.OrderByDescending(m=>m.Width*m.Height).ThenByDescending(m=>m.Frequency).ToList();
    }
    public void ChangeMode(string device,Native.DevMode mode,uint orientation)
    {
        var current=new Native.DevMode {Size=(ushort)Marshal.SizeOf<Native.DevMode>()};
        if(!Native.EnumDisplaySettings(device,-1,ref current)) throw new InvalidOperationException(L.T("Aktueller Monitormodus nicht lesbar."));
        current.Width=mode.Width; current.Height=mode.Height; current.Frequency=mode.Frequency;
        if((current.Orientation % 2)!=(orientation % 2)) (current.Width,current.Height)=(current.Height,current.Width);
        current.Orientation=orientation; current.Fields=0x80000|0x100000|0x400000|0x80;
        int result=Native.ChangeDisplaySettingsEx(device,ref current,IntPtr.Zero,2,IntPtr.Zero);
        if(result!=0) throw new InvalidOperationException(L.F($"Dieser Anzeigemodus wird abgelehnt (Windows {result})."));
        result=Native.ChangeDisplaySettingsEx(device,ref current,IntPtr.Zero,0,IntPtr.Zero);
        if(result!=0) throw new InvalidOperationException(L.F($"Anzeigemodus konnte nicht gesetzt werden (Windows {result})."));
    }
    public static string ConnectionName(int output)=>output switch { 0=>"VGA",4=>"DVI",5=>"HDMI",6=>"LVDS",10=>"DisplayPort",11=>L.T("DisplayPort intern"),15=>"Miracast",16=>L.T("Indirekt"),_=>"Display" };
    public static void Check(int code,string action) { if(code!=0) throw new Win32Exception(code,$"{action}: {new Win32Exception(code).Message} ({code})"); }
}
