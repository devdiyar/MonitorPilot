using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MonitorPilot;
internal static class SelfTests
{
    public static int Run(string path)
    {
        var results=new List<object>();int failed=0;
        void Test(string name,Action action){try{action();results.Add(new{Name=name,Passed=true});}catch(Exception ex){failed++;results.Add(new{Name=name,Passed=false,Error=ex.Message});}}
        void Check(bool condition,string message="Assertion failed") {if(!condition)throw new Exception(message);}
        void Reject(Action action){try{action();}catch{return;}throw new Exception("Expected rejection");}
        void TemporaryStore(Action action)
        {
            string previous=Store.Root;Store.Root=Path.Combine(Path.GetTempPath(),"MonitorPilot-self-tests",Guid.NewGuid().ToString("N"));
            try{action();}finally{Store.Root=previous;}
        }
        Test("Win32 x64 structure sizes",()=>{Check(Marshal.SizeOf<Native.DisplayPath>()==72);Check(Marshal.SizeOf<Native.DisplayMode>()==64);Check(Marshal.SizeOf<Native.DevMode>()==220);Check(Marshal.SizeOf<Native.TargetName>()==420);Check(Marshal.SizeOf<Native.SourceName>()==84);Check(Marshal.SizeOf<Native.WindowPlacement>()==44);});
        var paths=new[]{new Native.DisplayPath {Source=new(){Id=1,ModeIndex=0},Target=new(){Id=11,ModeIndex=2,Refresh=new(){Numerator=144000,Denominator=1000},Rotation=1},Flags=1},new Native.DisplayPath {Source=new(){Id=2,ModeIndex=1},Target=new(){Id=12,ModeIndex=3,Refresh=new(){Numerator=60000,Denominator=1000},Rotation=2},Flags=1}};
        var modes=new[]{new Native.DisplayMode {Type=1,Source=new(){Width=2560,Height=1440,Position=new(){X=0,Y=0}}},new Native.DisplayMode {Type=1,Source=new(){Width=1920,Height=1080,Position=new(){X=-1920,Y=200}}},new Native.DisplayMode {Type=2,Signal=new(){PixelRate=578340000,ActiveSize=new(){Width=2560,Height=1440}}},new Native.DisplayMode {Type=2,Signal=new(){PixelRate=148500000,ActiveSize=new(){Width=1920,Height=1080}}}};
        var topology=new TopologySnapshot {Paths=DisplayService.Pack(paths),Modes=DisplayService.Pack(modes),Identities=new(){new(){Key="a",Name="A"},new(){Key="b",Name="B"}}};
        Test("Binary union snapshot survives JSON roundtrip",()=>{var copy=Store.Clone(topology);Check(copy.Modes.SequenceEqual(topology.Modes));Check(DisplayService.Unpack<Native.DisplayMode>(copy.Modes)[2].Signal.PixelRate==578340000);Check(Program.Fingerprint(copy)==Program.Fingerprint(topology));});
        Test("Focus on secondary reanchors source and preserves Hz",()=>{var one=DisplayService.Select(topology,new[]{"b"},"b");var p=DisplayService.Unpack<Native.DisplayPath>(one.Paths)[0];var m=DisplayService.Unpack<Native.DisplayMode>(one.Modes)[p.Source.ModeIndex].Source;Check(m.Position.X==0&&m.Position.Y==0&&m.Width==1920);Check(p.Target.Refresh.Numerator==60000&&p.Target.Rotation==2);});
        Test("Selecting primary shifts all monitors without changing saved baseline",()=>{var changed=DisplayService.Select(topology,new[]{"a","b"},"b");var m=DisplayService.Unpack<Native.DisplayMode>(changed.Modes);Check(m[0].Source.Position.X==1920&&m[0].Source.Position.Y==-200);Check(DisplayService.Unpack<Native.DisplayMode>(topology.Modes)[0].Source.Position.X==0);});
        Test("Reject zero active monitors",()=>Reject(()=>DisplayService.Select(topology,Array.Empty<string>(),"a")));
        Test("Reject inactive primary",()=>Reject(()=>DisplayService.Select(topology,new[]{"a"},"b")));
        Test("Reject missing monitor",()=>Reject(()=>DisplayService.Select(topology,new[]{"missing"},"missing")));
        Test("Hardware percent mapping respects nonzero device minimum",()=>{var control=new ControlValue{Min=10,Max=210,Current=110};Check(control.Percent==50);Check(control.FromPercent(0)==10&&control.FromPercent(100)==210&&control.FromPercent(50)==110);Check(control.FromPercent(-5)==10&&control.FromPercent(900)==210);});
        Test("Brightness draft does not alter source or other controls",()=>{var state=new HardwareState{Brightness=new(){Min=0,Max=100,Current=60},Contrast=new(){Min=0,Max=100,Current=75}};var draft=HardwareService.BrightnessOnly(state,25);Check(draft.Brightness!.Current==25&&state.Brightness.Current==60&&draft.Contrast==null);});
        Test("Software-only change needs no physical monitor access",()=>
        {
            var monitor=new MonitorDevice{Hardware=new(){Brightness=new(){Min=0,Max=100,Current=60},Red=new(){Min=0,Max=100,Current=91}}};
            var actual=new HardwareService().Apply(monitor,new());
            Check(actual.Brightness?.Current==60&&actual.Red?.Current==91);
            actual.Brightness!.Current=25;Check(monitor.Hardware.Brightness!.Current==60,"Returned state must not mutate cached values");
        });
        Test("Temporary DDC read failure recovers within bounded retries",()=>
        {
            int reads=0;var value=HardwareService.ReadWithRetry(()=>++reads<3?null:new ControlValue{Min=0,Max=100,Current=61});
            Check(value?.Current==61,"A transient unavailable response must be retried");
        });
        Test("Permanent DDC read failure stops retrying",()=>
        {
            int reads=0;var value=HardwareService.ReadWithRetry(()=>{if(++reads>3)throw new Exception("Retry limit exceeded");return null;});
            Check(value==null);
        });
        Test("Readback retries a delayed monitor acknowledgement",()=>
        {
            int reads=0;var desired=new HardwareState{Brightness=new(){Min=0,Max=100,Current=60}};
            var actual=HardwareService.ReadBack(desired,()=>new(){Brightness=new(){Min=0,Max=100,Current=++reads<3?59u:60u},Contrast=new(){Min=0,Max=100,Current=73}});
            Check(actual.Brightness?.Current==60&&actual.Contrast?.Current==73&&reads==3,"A stale response was accepted before the monitor acknowledged the target");
        });
        Test("Readback does not endlessly retry an unavailable monitor",()=>
        {
            int reads=0;var actual=HardwareService.ReadBack(new(){Red=new(){Min=0,Max=100,Current=40}},()=>{if(++reads>3)throw new Exception("Readback retry limit exceeded");return new();});
            Check(actual.Red==null&&reads==3,"An unreachable monitor was not bounded to three readbacks");
        });
        Test("Independent monitors do not block one another",()=>
        {
            var hardware=new HardwareService();using var bothEntered=new CountdownEvent(2);
            var tasks=new[]{"monitor-a","monitor-b"}.Select(key=>Task.Run(()=>hardware.ForDevice(key,()=>
            {
                bothEntered.Signal();Check(bothEntered.Wait(3000),"Different monitor operations were globally serialized");return true;
            }))).ToArray();Task.WaitAll(tasks);
        });
        Test("Operations on the same monitor remain serialized",()=>
        {
            var hardware=new HardwareService();int active=0,overlap=0;
            var tasks=Enumerable.Range(0,4).Select(_=>Task.Run(()=>hardware.ForDevice("same-monitor",()=>
            {
                if(Interlocked.Increment(ref active)>1)Interlocked.Increment(ref overlap);
                Thread.Sleep(25);Interlocked.Decrement(ref active);return true;
            }))).ToArray();Task.WaitAll(tasks);Check(overlap==0);
        });
        Test("Static capabilities avoid repeated expensive device queries",()=>TemporaryStore(()=>
        {
            var hardware=new HardwareService();int calls=0;
            DdcCapabilities Read(){calls++;return new(true,true,36);}
            var first=hardware.CapabilitiesFor("monitor-a",Read);var second=hardware.CapabilitiesFor("monitor-a",Read);
            Check(first.TemperatureMask==36&&second.TemperatureMask==36&&calls==1);
        }));
        Test("Monitor commands sharing an adapter do not interleave",()=>
        {
            var hardware=new HardwareService();int active=0,overlap=0;
            var tasks=Enumerable.Range(0,4).Select(i=>Task.Run(()=>hardware.ForMonitor(new(){Key="monitor-"+i,AdapterKey="shared-gpu"},()=>
            {
                if(Interlocked.Increment(ref active)>1)Interlocked.Increment(ref overlap);
                Thread.Sleep(25);Interlocked.Decrement(ref active);return true;
            }))).ToArray();Task.WaitAll(tasks);Check(overlap==0,"Commands on the same graphics adapter were interleaved");
        });
        Test("Separate adapters can progress at the same time",()=>
        {
            var hardware=new HardwareService();using var bothEntered=new CountdownEvent(2);
            var tasks=new[]{"gpu-a","gpu-b"}.Select(key=>Task.Run(()=>hardware.ForMonitor(new(){Key="monitor-"+key,AdapterKey=key},()=>
            {
                bothEntered.Signal();Check(bothEntered.Wait(3000),"Unrelated adapters were serialized");return true;
            }))).ToArray();Task.WaitAll(tasks);
        });
        Test("Capabilities survive restart without reusing live brightness values",()=>TemporaryStore(()=>
        {
            new HardwareService().CapabilitiesFor("monitor-a",()=>new(true,true,36));
            var cached=new HardwareService().CapabilitiesFor("monitor-a",()=>throw new Exception("Unnecessary capability query after restart"));
            Check(cached.TemperatureMask==36);
        }));
        Test("Explicit capability refresh replaces stale feature data",()=>TemporaryStore(()=>
        {
            var hardware=new HardwareService();hardware.CapabilitiesFor("monitor-a",()=>new(true,true,36));
            var fresh=hardware.CapabilitiesFor("monitor-a",()=>new(true,false,0),true);
            Check(fresh.Available&&!fresh.SupportsTemperature&&fresh.TemperatureMask==0);
        }));
        Test("Expired capabilities are read from the monitor again",()=>TemporaryStore(()=>
        {
            Store.Save("ddc-capabilities.json",new Dictionary<string,DdcCapabilities>{{"monitor-a",new(true,true,36,DateTimeOffset.UtcNow.AddDays(-31))}});
            var fresh=new HardwareService().CapabilitiesFor("monitor-a",()=>new(true,false,0));
            Check(!fresh.SupportsTemperature&&fresh.TemperatureMask==0);
        }));
        Test("Unavailable capability query is not repeated on every value read",()=>TemporaryStore(()=>
        {
            var hardware=new HardwareService();int calls=0;
            DdcCapabilities Read(){calls++;return new(false,false,0);}
            hardware.CapabilitiesFor("monitor-a",Read);hardware.CapabilitiesFor("monitor-a",Read);
            Check(calls==1,"Failed capability queries must have a cooldown");
        }));
        Test("Overnight schedule boundaries",()=>{TimeSpan d=new(8,0,0),n=new(21,0,0);Check(AppController.IsNight(new(23,0,0),d,n));Check(AppController.IsNight(new(0,0,0),d,n));Check(!AppController.IsNight(new(8,0,0),d,n));Check(AppController.IsNight(new(21,0,0),d,n));Check(!AppController.IsNight(new(15,0,0),d,n));});
        Test("Reversed shift schedule",()=>{TimeSpan d=new(20,0,0),n=new(7,0,0);Check(AppController.IsNight(new(12,0,0),d,n));Check(!AppController.IsNight(new(23,0,0),d,n));});
        Test("Reject corrupt profile data",()=>{Reject(()=>Store.Validate(new()));var w=new WorkspaceSnapshot{Topology=Store.Clone(topology)};w.Topology.Modes=new byte[65];Reject(()=>Store.Validate(w));});
        Test("Reject excessive blackout and invalid hardware ranges",()=>{var w=new WorkspaceSnapshot{Topology=Store.Clone(topology),Dimming=new(){{"a",100}}};Reject(()=>Store.Validate(w));w.Dimming.Clear();w.Hardware["a"]=new(){Brightness=new(){Min=10,Max=100,Current=200}};Reject(()=>Store.Validate(w));});
        Test("Profiles retain windows, display modes and colors",()=>{var w=new WorkspaceSnapshot{Topology=topology,Windows=new(){new(){Handle=123,ProcessId=5,ProcessStart=456,Placement=DisplayService.Pack(new[]{new Native.WindowPlacement {Length=44,ShowCmd=3,Normal=new(){Left=-1800,Top=50,Right=-200,Bottom=1000}}})}},Hardware=new(){{"a",new(){Red=new(){Min=0,Max=100,Current=91}}}},Dimming=new(){{"a",17}}};Store.Validate(w);var back=Store.Clone(w);Check(back.Windows[0].Placement.SequenceEqual(w.Windows[0].Placement)&&back.Hardware["a"].Red!.Current==91&&back.Dimming["a"]==17);});
        Test("Display modes are reused without repeated driver enumeration",()=>
        {
            DisplayService.InvalidateModes();int reads=0;
            List<Native.DevMode> Read(){reads++;return new(){new(){Width=1920,Height=1080,Frequency=60}};}
            var first=DisplayService.ModesFor("qa-display",Read);var second=DisplayService.ModesFor("qa-display",Read);
            Check(reads==1&&first.Count==1&&second[0].Frequency==60,"Mode list was queried again while unchanged");
        });
        Test("Display mode results cannot mutate the cached list",()=>
        {
            DisplayService.InvalidateModes();var original=new List<Native.DevMode>{new(){Width=1920,Height=1080,Frequency=60}};
            var first=DisplayService.ModesFor("qa-display",()=>original);first.Clear();
            var second=DisplayService.ModesFor("qa-display",()=>original);Check(second.Count==1,"Returned list leaked cached storage");
        });
        Test("Monitor changes invalidate cached display modes",()=>
        {
            DisplayService.InvalidateModes();DisplayService.ModesFor("qa-display",()=>new(){new(){Frequency=60}});
            DisplayService.InvalidateModes();var fresh=DisplayService.ModesFor("qa-display",()=>new(){new(){Frequency=144}});Check(fresh[0].Frequency==144);
        });
        Test("Default global hotkeys are unique",()=>{var keys=AppSettings.Defaults();Check(keys.Count==keys.Select(k=>(k.Modifiers,k.Key)).Distinct().Count());Check(keys.Any(k=>k.Action=="restore")&&keys.Any(k=>k.Action=="filters"));});
        LiveControlTests.Register(Test);
        CompatibilityTests.Register(Test,topology);
        LocalizationTests.Register(Test);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=failed==0,Total=results.Count,Failed=failed,Results=results},Store.Json));return failed==0?0:1;
    }
}
