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
        Test("Restore never re-applies a reduced focus layout",()=>{Check(AppController.ShouldRestore(5,1,5));Check(!AppController.ShouldRestore(1,1,5));Check(AppController.ShouldRestore(5,5,5));Check(!AppController.ShouldRestore(4,5,5));Check(!AppController.ShouldRestore(3,3,5));});
        Test("EDID image size gives the panel diagonal",()=>{var edid=new byte[128];edid[66]=0xB9;edid[67]=0x88;edid[68]=0x21;var size=DisplayService.EdidSize(edid);Check(size==(697,392));var m=new MonitorDevice{WidthMm=size.Width,HeightMm=size.Height};Check(Math.Abs(m.DiagonalInches!.Value-31.5)<0.05);edid[66]=edid[67]=edid[68]=0;edid[21]=60;edid[22]=34;Check(DisplayService.EdidSize(edid)==(600,340));Check(DisplayService.EdidSize(new byte[10])==(0,0));});
        Test("Photo match lowers a too bright and too blue display",()=>{var reference=new PatchSample(0.40,0.40,0.40,180,180);var bright=new PatchSample(0.60,0.60,0.72,200,200);var plan=PhotoMatch.Plan("b",bright,reference,50,50,50,50,0,false);Check(plan.Brightness<50);Check(plan.Blue<50&&plan.Red==50&&plan.Green==50);Check(plan.Cooler>15);Check(!plan.Matched);});
        Test("Photo match uses software dimming below zero backlight",()=>{var reference=new PatchSample(0.20,0.20,0.20,120,120);var plan=PhotoMatch.Plan("b",new PatchSample(0.40,0.40,0.40,170,170),reference,0,null,null,null,0,false);Check(plan.Brightness==0&&plan.Dimming>=40&&plan.Dimming<=55);var none=PhotoMatch.Plan("c",new PatchSample(0.40,0.40,0.40,170,170),reference,null,null,null,null,0,false);Check(none.Brightness==null&&none.Dimming==50);});
        Test("Photo match limits one round and suggests a central reference",()=>{var gray=new PatchSample(0.4,0.4,0.4,170,170);Check(PhotoMatch.Plan("b",new PatchSample(0.1,0.1,0.1,90,90),gray,10,50,50,50,0,false).Brightness==22);var blue=PhotoMatch.Plan("c",new PatchSample(0.4,0.4,0.8,170,170),gray,50,50,50,50,0,false);Check(blue.Blue==40&&blue.Red==50&&blue.Green==50);Check(PhotoMatch.Plan("d",new PatchSample(0.4,0.4,0.8,170,170),gray,50,50,50,50,0,false,false).Blue==50);var set=new Dictionary<string,PatchSample>{["a"]=gray,["b"]=new(0.4,0.4,0.44,170,170),["c"]=new(0.4,0.4,0.36,170,170),["x"]=new(0.5,0.4,0.2,170,170)};Check(PhotoMatch.Central(set)=="a");});
        Test("Photo screens are found and paired with the Windows layout",()=>
        {
            int w=400,h=240;var px=new byte[w*h*4];for(int i=3;i<px.Length;i+=4)px[i]=255;
            void Fill(int x0,int y0,int x1,int y1){for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){int i=(y*w+x)*4;px[i]=px[i+1]=px[i+2]=200;}}
            // Portrait screen on the left, two landscape screens separated only by a 3 px bezel, plus a small LED.
            Fill(20,40,90,200);Fill(100,100,250,190);Fill(253,100,380,190);Fill(300,220,304,224);
            var photo=System.Windows.Media.Imaging.BitmapSource.Create(w,h,96,96,System.Windows.Media.PixelFormats.Bgra32,null,px,w*4);
            var spots=PhotoMatch.FindScreens(photo,3);Check(spots.Count==3);
            var map=PhotoMatch.Assign(spots,new[]{("main",1280.0,720.0,16/9.0),("side",-720.0,500.0,9/16.0),("right",3520.0,700.0,16/9.0)});
            Check(map!=null&&map["side"].X<0.25&&map["main"].X>0.3&&map["main"].X<0.6&&map["right"].X>0.7);
            Check(PhotoMatch.Assign(spots.Take(2).ToList(),new[]{("main",0.0,0.0,1.0),("side",1.0,0.0,1.0),("right",2.0,0.0,1.0)})==null);
        });
        Test("A saved match follows shared brightness changes",()=>
        {
            var match=new MatchCalibration{Reference="r",Levels={["r"]=new(){Brightness=0},["dark"]=new(){Brightness=43},["bright"]=new(){Brightness=0,Dimming=20}}};
            var same=PhotoMatch.Follow(match,0);Check(same["r"]==(0,0)&&same["dark"]==(43,0)&&same["bright"]==(0,20));
            var up=PhotoMatch.Follow(match,25);Check(up["r"]==(25,0)&&up["dark"]==(100,0)&&up["bright"]==(15,0));
            var settings=new AppSettings{Match=new(){Reference="missing",Levels={["r"]=new()}}};settings.Normalize();Check(settings.Match==null);
        });
        Test("Photo match flags displays that cannot reach the reference",()=>{var plan=PhotoMatch.Plan("b",new PatchSample(0.10,0.10,0.10,90,90),new PatchSample(0.40,0.40,0.40,170,170),95,null,null,null,0,false);Check(plan.TooDim&&plan.Brightness==100);Check(PhotoMatch.Plan("r",new PatchSample(0.4,0.4,0.4,170,170),new PatchSample(0.4,0.4,0.4,170,170),20,50,50,50,0,true).Reference);});
        Test("Photo sampling averages linear light and ignores outliers",()=>{var pixels=new byte[100*100*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=176;pixels[i+1]=176;pixels[i+2]=176;pixels[i+3]=255;}for(int i=0;i<400;i+=4){pixels[i]=pixels[i+1]=pixels[i+2]=255;}var image=System.Windows.Media.Imaging.BitmapSource.Create(100,100,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,400);var sample=PhotoMatch.Sample(image,0.5,0.5,0.2);Check(Math.Abs(sample.CodeMean-176)<0.5&&!sample.Clipped&&!sample.Dark);Check(Math.Abs(sample.Y-0.4342)<0.002);var corner=PhotoMatch.Sample(image,0,0,0.05);Check(corner.CodeMean<=255);});
        Test("Phone upload page serves the form and receives a JPEG",()=>TemporaryStore(()=>
        {
            using var server=new PhotoUploadServer(System.Net.IPAddress.Loopback,new[]{0});Check(server.Running);
            var pixels=new byte[64*48*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=pixels[i+1]=pixels[i+2]=176;pixels[i+3]=255;}
            var encoder=new System.Windows.Media.Imaging.JpegBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(64,48,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,256)));
            using var jpeg=new MemoryStream();encoder.Save(jpeg);byte[]? received=null;using var arrived=new ManualResetEventSlim();server.Received=data=>{received=data;arrived.Set();return Task.FromResult("Runde 1");};
            using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(10)};string root=$"http://127.0.0.1:{server.Port}/{server.Token}";
            Check(http.GetStringAsync(root).GetAwaiter().GetResult().Contains("capture=\"environment\""));
            Check(http.GetAsync($"http://127.0.0.1:{server.Port}/wrong").GetAwaiter().GetResult().StatusCode==System.Net.HttpStatusCode.NotFound);
            var response=http.PostAsync(root+"/upload",new System.Net.Http.ByteArrayContent(jpeg.ToArray())).GetAwaiter().GetResult();Check(response.IsSuccessStatusCode&&arrived.Wait(5000)&&response.Content.ReadAsStringAsync().GetAwaiter().GetResult()=="Runde 1");
            var photo=PhotoMatch.Load(received!);Check(photo.PixelWidth==64&&photo.PixelHeight==48);Check(Math.Abs(PhotoMatch.Sample(photo,0.5,0.5).CodeMean-176)<4);
            using var again=new PhotoUploadServer(System.Net.IPAddress.Loopback,new[]{0});Check(again.Token==server.Token&&server.Token.Length==8);
        }));
        Test("Repeated restore names do not grow",()=>{Check(AppController.BaseName("Vor Vor Vor Fokusmodus")=="Fokusmodus");Check(AppController.BaseName("Before Before Focus mode")=="Focus mode");Check(AppController.BaseName("Vor ")=="Vor ");});
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
