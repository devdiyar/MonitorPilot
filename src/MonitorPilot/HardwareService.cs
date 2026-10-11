using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MonitorPilot;

internal sealed record DdcCapabilities(bool Available,bool SupportsTemperature,uint TemperatureMask,DateTimeOffset CheckedAt=default);

internal sealed class HardwareService
{
    private readonly ConcurrentDictionary<string,object> deviceGates=new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string,object> adapterGates=new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string,DdcCapabilities> capabilities=new(StringComparer.OrdinalIgnoreCase);
    private static readonly object capabilityFileGate=new(),originalFileGate=new();
    private delegate bool Getter(IntPtr h,out uint min,out uint value,out uint max);
    private delegate bool Setter(IntPtr h,uint value);
    public HardwareService()
    {
        try
        {
            var saved=Store.Read<Dictionary<string,DdcCapabilities>>("ddc-capabilities.json");
            if(saved!=null)foreach(var pair in saved)capabilities[pair.Key]=pair.Value;
        }
        catch(Exception ex){Store.Log("Optional DDC capability cache ignored: "+ex.Message);}
    }
    internal DdcCapabilities CapabilitiesFor(string key,Func<DdcCapabilities> read,bool force=false)
    {
        var now=DateTimeOffset.UtcNow;
        if(!force&&capabilities.TryGetValue(key,out var known)&&known.CheckedAt>now.AddDays(known.Available?-30:-1)&&known.CheckedAt<now.AddMinutes(5))return known;
        var fresh=read() with {CheckedAt=now};
        capabilities[key]=fresh;
        try{lock(capabilityFileGate)Store.Save("ddc-capabilities.json",capabilities.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase));}
        catch(Exception ex){Store.Log("Optional DDC capability cache could not be saved: "+ex.Message);}
        return fresh;
    }
    public void Probe(MonitorDevice monitor,bool forceCapabilities=false)
    {
        if(!monitor.Active || monitor.Handle==IntPtr.Zero) {monitor.Hardware=new();monitor.DdcStatus="Für ausgeschaltete Displays nicht erreichbar";return;}
        try
        {
            WithMonitor(monitor,h=>monitor.Hardware=Read(monitor,h,cached:monitor.Hardware,forceCapabilities:forceCapabilities));
            int count=new[] {monitor.Hardware.Brightness,monitor.Hardware.Contrast,monitor.Hardware.Red,monitor.Hardware.Green,monitor.Hardware.Blue}.Count(x=>x!=null);
            monitor.DdcStatus=count>0 ? $"DDC/CI verbunden · {count} Regler" : "DDC/CI nicht verfügbar · Im Bildschirmmenü aktivieren";
        }
        catch(Exception ex) {monitor.Hardware=new();monitor.DdcStatus="DDC/CI nicht erreichbar";Store.Log($"DDC probe {monitor.Name}: {ex.Message}");}
    }
    public void ProbeBrightness(MonitorDevice monitor)
    {
        if(!monitor.Active||monitor.Handle==IntPtr.Zero)return;
        try
        {
            WithMonitor(monitor,h=>monitor.Hardware=Read(monitor,h,new(){Brightness=new()},monitor.Hardware));
            monitor.DdcStatus=monitor.Hardware.Brightness!=null?"Hardware-Helligkeit verbunden · weitere Regler werden erkannt":"Weitere Monitorfunktionen werden erkannt …";
        }
        catch(Exception ex){monitor.DdcStatus="DDC/CI nicht erreichbar";Store.Log($"Brightness probe {monitor.Name}: {ex.Message}");}
    }
    private HardwareState Read(MonitorDevice monitor,IntPtr h,HardwareState? selection=null,HardwareState? cached=null,bool refreshColors=false,bool forceCapabilities=false)
    {
        var state=cached==null?new HardwareState():Store.Clone(cached);
        bool full=selection==null,supportsTemperature=state.TemperatureMask!=0||state.Temperature.HasValue;
        if(full)
        {
            var caps=CapabilitiesFor(monitor.Key,()=>
            {
                var elapsed=Stopwatch.StartNew();
                bool available=Native.GetMonitorCapabilities(h,out uint flags,out uint temperatures);
                int error=available?0:Marshal.GetLastWin32Error();
                Store.Log($"Capabilities {monitor.Name}: available={available}, error=0x{error:X8}, {elapsed.Elapsed.TotalMilliseconds:0} ms");
                return new(available,(flags&8)!=0,temperatures);
            },forceCapabilities);
            supportsTemperature=caps.SupportsTemperature;state.TemperatureMask=supportsTemperature?caps.TemperatureMask:0;
        }
        if(full||selection!.Brightness!=null)state.Brightness=ReadControl(h,Native.GetMonitorBrightness,0x10,state.Brightness!=null);
        if(full||selection!.Contrast!=null)state.Contrast=ReadControl(h,Native.GetMonitorContrast,0x12,state.Contrast!=null);
        if(full||refreshColors||selection!.Red!=null)state.Red=ReadControl(h,(IntPtr p,out uint a,out uint b,out uint c)=>Native.GetMonitorRedGreenOrBlueGain(p,0,out a,out b,out c),0x16,state.Red!=null);
        if(full||refreshColors||selection!.Green!=null)state.Green=ReadControl(h,(IntPtr p,out uint a,out uint b,out uint c)=>Native.GetMonitorRedGreenOrBlueGain(p,1,out a,out b,out c),0x18,state.Green!=null);
        if(full||refreshColors||selection!.Blue!=null)state.Blue=ReadControl(h,(IntPtr p,out uint a,out uint b,out uint c)=>Native.GetMonitorRedGreenOrBlueGain(p,2,out a,out b,out c),0x1A,state.Blue!=null);
        if(full||refreshColors||selection!.Temperature.HasValue)
            state.Temperature=supportsTemperature && Native.GetMonitorColorTemperature(h,out uint temp) && temp>=1 && temp<=8?temp:null;
        return state;
    }
    private static ControlValue? ReadControl(IntPtr h,Getter get,byte code,bool retry=false)
    {
        int attempts=0;
        ControlValue? Once()
        {
            attempts++;
            if(get(h,out uint min,out uint cur,out uint max) && max>min && cur>=min && cur<=max) return new() {Min=min,Current=cur,Max=max};
            if(Native.GetVCPFeatureAndVCPFeatureReply(h,code,out _,out cur,out max) && max>0 && cur<=max) return new() {Min=0,Current=cur,Max=max,ViaVcp=true};
            return null;
        }
        var value=retry?ReadWithRetry(Once):Once();
        if(attempts>1)Store.Log($"DDC control 0x{code:X2}: {attempts} read attempts, available={value!=null}");
        return value;
    }
    internal static ControlValue? ReadWithRetry(Func<ControlValue?> read)
    {
        for(int attempt=0;attempt<3;attempt++)
        {
            var value=read();if(value!=null)return value;
            if(attempt<2)Thread.Sleep(75*(attempt+1));
        }
        return null;
    }
    internal T ForDevice<T>(string key,Func<T> action)
    {
        lock(deviceGates.GetOrAdd(key,_=>new object()))return action();
    }
    internal T ForMonitor<T>(MonitorDevice monitor,Func<T> action)=>ForDevice(monitor.Key,()=>
    {
        // Some graphics drivers serialize DDC internally. Keep each transaction together
        // on a shared adapter, while different adapters may work independently.
        string bus=string.IsNullOrEmpty(monitor.AdapterKey)?monitor.Key:monitor.AdapterKey;
        lock(adapterGates.GetOrAdd(bus,_=>new object()))return action();
    });
    private T WithMonitor<T>(MonitorDevice monitor,Func<IntPtr,T> action)
    {
        return ForMonitor(monitor,()=>
        {
            if(!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle,out uint count) || count==0) throw new InvalidOperationException("Kein physischer Monitor erreichbar.");
            if(count>16) throw new InvalidOperationException("Ungültige Anzahl physischer Monitore.");
            var handles=new Native.PhysicalMonitor[count];
            if(!Native.GetPhysicalMonitorsFromHMONITOR(monitor.Handle,count,handles)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if(count!=1) throw new InvalidOperationException("Gespiegelte physische Monitore können nicht eindeutig einzeln angesteuert werden.");
                return action(handles[0].Handle);
            }
            finally { Native.DestroyPhysicalMonitors(count,handles); }
        });
    }
    public HardwareState Apply(MonitorDevice monitor,HardwareState desired,bool remember=true)
    {
        // Software dimming and unchanged drafts must not open a DDC connection.
        if(desired.Brightness==null&&desired.Contrast==null&&desired.Red==null&&desired.Green==null&&desired.Blue==null&&!desired.Temperature.HasValue)
            return Store.Clone(monitor.Hardware);
        return WithMonitor(monitor,h=>monitor.Hardware=Apply(monitor,h,desired,remember));
    }
    public HardwareState ApplyBrightness(MonitorDevice monitor,int percent,bool relative=false)
    {
        return WithMonitor(monitor,h=>
        {
            // Relative hotkeys must start from the live brightness, including OSD changes.
            var before=Read(monitor,h,new(){Brightness=new()},monitor.Hardware);
            if(before.Brightness==null)throw new InvalidOperationException("Hardware-Helligkeit ist nicht erreichbar. DDC/CI im Bildschirmmenü prüfen.");
            int target=relative?Math.Clamp(before.Brightness.Percent+percent,0,100):percent;
            return monitor.Hardware=Apply(monitor,h,BrightnessOnly(before,target),true,before);
        });
    }
    private HardwareState Apply(MonitorDevice monitor,IntPtr h,HardwareState desired,bool remember,HardwareState? before=null)
    {
        bool firstChange=false;
        if(remember)lock(originalFileGate)firstChange=!(Store.Read<Dictionary<string,HardwareState>>("hardware-original.json")?.ContainsKey(monitor.Key)??false);
        // Only the first change needs a fresh complete backup. Normal adjustments read
        // the requested controls; untouched cached values are retained, never written.
        before=firstChange||desired.Temperature.HasValue?Read(monitor,h,cached:monitor.Hardware):before??Read(monitor,h,desired,monitor.Hardware);
        if(firstChange)lock(originalFileGate)
        {
            var originals=Store.Read<Dictionary<string,HardwareState>>("hardware-original.json")??new(StringComparer.OrdinalIgnoreCase);
            if(!originals.ContainsKey(monitor.Key)){originals[monitor.Key]=Store.Clone(before);Store.Save("hardware-original.json",originals);}
        }
        bool temperatureChanged=desired.Temperature.HasValue&&desired.Temperature!=before.Temperature;
        if(temperatureChanged)
        {
            uint t=desired.Temperature!.Value;
            if(t<1 || t>8 || (before.TemperatureMask & (1u << ((int)t-1)))==0) throw new InvalidOperationException("Diese Farbtemperatur wird vom Monitor nicht unterstützt.");
            if(!Native.SetMonitorColorTemperature(h,t)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Farbtemperatur abgelehnt.");
            Thread.Sleep(80);before=Read(monitor,h,cached:before);
        }
        Set(h,"Helligkeit",before.Brightness,desired.Brightness,Native.SetMonitorBrightness,0x10);
        Set(h,"Kontrast",before.Contrast,desired.Contrast,Native.SetMonitorContrast,0x12);
        Set(h,"Rot",before.Red,desired.Red,(p,v)=>Native.SetMonitorRedGreenOrBlueGain(p,0,v),0x16);
        Set(h,"Grün",before.Green,desired.Green,(p,v)=>Native.SetMonitorRedGreenOrBlueGain(p,1,v),0x18);
        Set(h,"Blau",before.Blue,desired.Blue,(p,v)=>Native.SetMonitorRedGreenOrBlueGain(p,2,v),0x1A);
        // Gain/preset changes can affect the other color channels and the white point.
        bool colors=temperatureChanged||desired.Red!=null||desired.Green!=null||desired.Blue!=null;
        var actual=ReadBack(desired,()=>Read(monitor,h,desired,before,colors));
        Verify("Helligkeit",desired.Brightness,actual.Brightness);Verify("Kontrast",desired.Contrast,actual.Contrast);
        Verify("Rot",desired.Red,actual.Red);Verify("Grün",desired.Green,actual.Green);Verify("Blau",desired.Blue,actual.Blue);
        if(desired.Temperature.HasValue && actual.Temperature!=desired.Temperature) throw new InvalidOperationException("Der Monitor bestätigt die Farbtemperatur nicht.");
        Store.Log($"Hardware applied and read back: {monitor.Name}");return actual;
    }
    private static void Verify(string label,ControlValue? expected,ControlValue? actual)
    {
        if(expected!=null && (actual==null || Math.Abs((long)expected.Current-actual.Current)>1)) throw new InvalidOperationException($"{label} wurde vom Monitor nicht bestätigt. Bitte neu erkennen; bereits übernommene Regler bleiben gesetzt.");
    }
    internal static HardwareState ReadBack(HardwareState desired,Func<HardwareState> read)
    {
        bool Matches(ControlValue? expected,ControlValue? actual)=>expected==null||actual?.Current==expected.Current;
        HardwareState actual=new();
        for(int attempt=0;attempt<3;attempt++)
        {
            actual=read();
            if(Matches(desired.Brightness,actual.Brightness)&&Matches(desired.Contrast,actual.Contrast)&&Matches(desired.Red,actual.Red)&&Matches(desired.Green,actual.Green)&&Matches(desired.Blue,actual.Blue)&&(!desired.Temperature.HasValue||desired.Temperature==actual.Temperature))break;
            if(attempt<2)Thread.Sleep(50*(attempt+1));
        }
        return actual;
    }
    private static void Set(IntPtr h,string label,ControlValue? current,ControlValue? desired,Setter set,byte code)
    {
        if(desired==null) return;
        if(current==null) throw new InvalidOperationException($"{label} ist an diesem Monitor nicht verfügbar.");
        if(desired.Current<current.Min || desired.Current>current.Max) throw new InvalidOperationException($"{label} liegt außerhalb des Monitorregelbereichs.");
        if(current.Current==desired.Current) return;
        bool result=current.ViaVcp ? Native.SetVCPFeature(h,code,desired.Current) : set(h,desired.Current);
        if(!result) throw new Win32Exception(Marshal.GetLastWin32Error(),$"{label} konnte nicht gesetzt werden. DDC/CI im Bildschirmmenü prüfen.");
        // Both Windows setters already wait for the DDC transaction (~50 ms).
        // Readback adds a bounded wait only if this monitor has not acknowledged it.
    }
    public static HardwareState BrightnessOnly(HardwareState current,int percent)
    {
        if(current.Brightness==null) return new();
        var value=Store.Clone(current.Brightness); value.Current=value.FromPercent(percent);
        return new() {Brightness=value};
    }
}
