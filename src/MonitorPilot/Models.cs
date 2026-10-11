using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MonitorPilot;

public sealed class MonitorDevice
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AdapterKey { get; set; } = "";
    public int Number { get; set; }
    public bool Active { get; set; }
    public bool Primary { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Hz { get; set; }
    public uint Rotation { get; set; }
    public bool? Hdr { get; set; }
    public uint Bits { get; set; }
    public string Connection { get; set; } = "";
    public HardwareState Hardware { get; set; } = new();
    private string ddcStatus="Noch nicht geprüft";
    public int DdcControlCount {get;set;}
    [System.Text.Json.Serialization.JsonIgnore]
    public string DdcStatus {get=>ddcStatus=="DDC/CI verbunden · {0} Regler"?L.Format(ddcStatus,DdcControlCount):L.T(ddcStatus);set=>ddcStatus=value;}
    [System.Text.Json.Serialization.JsonPropertyName("DdcStatus")]
    public string DdcStatusText {get=>ddcStatus;set=>ddcStatus=value;}
    [System.Text.Json.Serialization.JsonIgnore] public IntPtr Handle { get; set; }
    public string Summary => Active ? $"{Width} × {Height} · {Hz:0.##} Hz{(Hdr == true ? " · HDR" : "")}" : L.T("In Windows deaktiviert");
    public override string ToString() => $"{Number:00} · {Name}{(Primary ? L.T(" · Hauptmonitor") : "")}";
}
public sealed class ControlValue
{
    public uint Min { get; set; }
    public uint Max { get; set; }
    public uint Current { get; set; }
    public bool ViaVcp { get; set; }
    public int Percent => Max > Min ? (int)Math.Round((Current - (double)Min) * 100 / (Max - Min)) : 0;
    public uint FromPercent(double value) => Min + (uint)Math.Round(Math.Clamp(value, 0, 100) * (Max - Min) / 100);
}
public sealed class HardwareState
{
    public ControlValue? Brightness { get; set; }
    public ControlValue? Contrast { get; set; }
    public ControlValue? Red { get; set; }
    public ControlValue? Green { get; set; }
    public ControlValue? Blue { get; set; }
    public uint? Temperature { get; set; }
    public uint TemperatureMask { get; set; }
}
public sealed class PathIdentity { public string Key { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class TopologySnapshot
{
    public byte[] Paths { get; set; } = Array.Empty<byte>();
    public byte[] Modes { get; set; } = Array.Empty<byte>();
    public List<PathIdentity> Identities { get; set; } = new();
}
public sealed class SavedWindow
{
    public long Handle { get; set; }
    public uint ProcessId { get; set; }
    public long ProcessStart { get; set; }
    public string Title { get; set; } = "";
    public byte[] Placement { get; set; } = Array.Empty<byte>();
    public string MonitorKey { get; set; } = "";
}
public sealed class WorkspaceSnapshot
{
    public int Schema { get; set; } = 1;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string Name { get; set; } = L.T("Arbeitsplatz");
    public TopologySnapshot Topology { get; set; } = new();
    public List<SavedWindow> Windows { get; set; } = new();
    public Dictionary<string, HardwareState> Hardware { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Dimming { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class ProfileEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public WorkspaceSnapshot Workspace { get; set; } = new();
    public override string ToString() => Workspace.Name;
}
public sealed class HotkeySetting
{
    public string Action { get; set; } = "";
    public uint Modifiers { get; set; } = 7;
    public uint Key { get; set; }
    public string Label { get; set; } = "";
}
public sealed class AppSettings
{
    public string Language {get;set;}="de";
    public string Appearance { get; set; } = "System";
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool MoveWindowsInFocus { get; set; } = true;
    public string PreferredMain { get; set; } = "";
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HotkeySetting> Hotkeys { get; set; } = Defaults();
    public bool BrightnessSchedule { get; set; }
    public string DayTime { get; set; } = "08:00";
    public string NightTime { get; set; } = "21:00";
    public int DayBrightness { get; set; } = 70;
    public int NightBrightness { get; set; } = 30;
    public void Normalize()
    {
        Language=L.Normalize(Language);
        Appearance=Appearance is "Light" or "Dark"?Appearance:"System";
        PreferredMain??="";
        var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in Aliases??new())if(!string.IsNullOrWhiteSpace(pair.Key)&&!string.IsNullOrWhiteSpace(pair.Value))aliases[pair.Key]=pair.Value;
        Aliases=aliases;
        var existing=Hotkeys??new();
        Hotkeys=Defaults().Select(defaultKey=>
        {
            var saved=existing.FirstOrDefault(h=>h!=null&&h.Action==defaultKey.Action&&h.Key>0&&h.Key<255&&h.Modifiers>0&&h.Modifiers<=15);
            if(saved==null)return defaultKey;
            saved.Label=defaultKey.Label;return saved;
        }).ToList();
        DayBrightness=Math.Clamp(DayBrightness,0,100);NightBrightness=Math.Clamp(NightBrightness,0,100);
        if(!TimeSpan.TryParseExact(DayTime,"hh\\:mm",CultureInfo.InvariantCulture,out var day)||day.TotalHours>=24)DayTime="08:00";
        if(!TimeSpan.TryParseExact(NightTime,"hh\\:mm",CultureInfo.InvariantCulture,out var night)||night.TotalHours>=24)NightTime="21:00";
        if(DayTime==NightTime){BrightnessSchedule=false;DayTime="08:00";NightTime="21:00";}
    }
    public static List<HotkeySetting> Defaults() => new()
    {
        new() { Action="show", Key=0x4D, Label="App öffnen" },
        new() { Action="focus", Key=0x46, Label="Fokusmodus umschalten" },
        new() { Action="restore", Key=0x52, Label="Arbeitsplatz wiederherstellen" },
        new() { Action="brighter", Key=0x26, Label="Alle heller" },
        new() { Action="darker", Key=0x28, Label="Alle dunkler" },
        new() { Action="rescue", Key=0x57, Label="Fenster zum Hauptmonitor" },
        new() { Action="filters", Key=0x30, Label="Softwaredimmung entfernen" }
    };
}
public static class Store
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, IncludeFields = true };
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonitorPilot");
    public static string FilePath(string name) => Path.Combine(Root, name);
    public static void Save<T>(string name, T data)
    {
        Directory.CreateDirectory(Root);
        string path = FilePath(name), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(data, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static T? Read<T>(string name)
    {
        string path=FilePath(name);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default;
    }
    public static T? ReadRecoverable<T>(string name,Action<T>? validate=null,Action<string>? recovered=null)
    {
        if(!File.Exists(FilePath(name)))return default;
        try
        {
            var data=Read<T>(name);
            if(data==null)throw new InvalidDataException(L.T("Leere Datendatei."));
            validate?.Invoke(data);return data;
        }
        catch(Exception ex) when(ex is JsonException or InvalidDataException)
        {
            // A backup must succeed before a later startup save may replace the file.
            var backup=name+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".bak";
            File.Copy(FilePath(name),FilePath(backup));
            Log($"Recovered invalid {name}; original preserved as {backup}: {ex.Message}");recovered?.Invoke(backup);return default;
        }
    }
    public static T Clone<T>(T item) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item, Json), Json)!;
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(Root); var path=FilePath("activity.log"); if(File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path+".previous", true); File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }
    public static void Validate(WorkspaceSnapshot snapshot)
    {
        if(snapshot==null||snapshot.Topology==null||snapshot.Topology.Paths==null||snapshot.Topology.Modes==null||snapshot.Topology.Identities==null||snapshot.Windows==null||snapshot.Hardware==null||snapshot.Dimming==null)
            throw new InvalidDataException(L.T("Die Arbeitsplatzdatei ist unvollständig."));
        if(snapshot.Schema != 1 || snapshot.Topology.Paths.Length == 0 || snapshot.Topology.Paths.Length % 72 != 0 || snapshot.Topology.Modes.Length % 64 != 0 || snapshot.Topology.Identities.Count != snapshot.Topology.Paths.Length / 72 || snapshot.Topology.Identities.Count > 32)
            throw new InvalidDataException(L.T("Die Arbeitsplatzdatei enthält keine gültige Bildschirmkonfiguration."));
        if(snapshot.Topology.Identities.Any(x => x==null||string.IsNullOrWhiteSpace(x.Key))||snapshot.Topology.Identities.Select(x=>x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=snapshot.Topology.Identities.Count) throw new InvalidDataException(L.T("Eine Monitoridentität fehlt oder ist doppelt vorhanden."));
        var keys=snapshot.Topology.Identities.Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(snapshot.Hardware.Keys.Concat(snapshot.Dimming.Keys).Any(k=>!keys.Contains(k)))throw new InvalidDataException(L.T("Monitorwerte gehören nicht zur gespeicherten Anordnung."));
        if(snapshot.Windows.Count>2000||snapshot.Windows.Any(w=>w==null||w.Placement==null||w.Placement.Length!=44||w.Handle==0||w.ProcessId==0||w.ProcessStart<=0))throw new InvalidDataException(L.T("Ungültige gespeicherte Fensterposition."));
        var paths=DisplayService.Unpack<Native.DisplayPath>(snapshot.Topology.Paths);
        var modes=DisplayService.Unpack<Native.DisplayMode>(snapshot.Topology.Modes);
        foreach(var p in paths)
        {
            if((p.Flags&1)==0 || p.Source.ModeIndex>=modes.Length || modes[p.Source.ModeIndex].Type!=1 || p.Target.Rotation<1 || p.Target.Rotation>4)
                throw new InvalidDataException(L.T("Ungültiger Monitorpfad oder Quellmodus."));
            if(p.Target.ModeIndex!=uint.MaxValue && (p.Target.ModeIndex>=modes.Length || modes[p.Target.ModeIndex].Type!=2))throw new InvalidDataException(L.T("Ungültiger Zielmodus."));
            var mode=modes[p.Source.ModeIndex].Source;
            if(mode.Width==0||mode.Height==0||mode.Width>32768||mode.Height>32768||Math.Abs((long)mode.Position.X)>100000||Math.Abs((long)mode.Position.Y)>100000)throw new InvalidDataException(L.T("Ungültige Bildschirmgröße oder Position."));
        }
        foreach(var v in snapshot.Hardware.Values)
        {
            if(v==null||v.Temperature is <1 or >8)throw new InvalidDataException(L.T("Ungültiger Hardware-Weißpunkt."));
            foreach(var c in new[] { v.Brightness,v.Contrast,v.Red,v.Green,v.Blue })
                if(c != null && (c.Max <= c.Min || c.Current < c.Min || c.Current > c.Max || c.Max > 65535)) throw new InvalidDataException(L.T("Ungültiger Monitorregelbereich."));
        }
        if(snapshot.Dimming.Values.Any(v => v < 0 || v > 80)) throw new InvalidDataException(L.T("Ungültige Softwaredimmung."));
    }
}
