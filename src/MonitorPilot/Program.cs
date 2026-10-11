using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MonitorPilot;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if(args.Contains("--install")||Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("MonitorPilot-Setup",StringComparison.OrdinalIgnoreCase))return Installer.Install(args.Contains("--silent"));
            if(args.Length>=3 && args[0]=="--watchdog")return RecoveryGuard.Watchdog(args[1],args[2]);
            if(args.Length>=2 && args[0]=="--self-test")return SelfTests.Run(args[1]);
            if(args.Length>=2 && args[0]=="--diagnose")return Diagnose(args[1]);
            if(args.Length>=2 && args[0]=="--test-ddc")return TestDdc(args[1]);
            if(args.Length>=2 && args[0]=="--benchmark-ddc")return HardwareBenchmark.Run(args[1]);
            if(args.Length>=2 && args[0]=="--test-ddc-controls")return HardwareBenchmark.VerifyAdjustments(args[1]);
            if(args.Length>=2 && args[0]=="--test-ddc-parallel")return HardwareBenchmark.VerifyParallel(args[1]).GetAwaiter().GetResult();
            if(args.Length>=2 && args[0]=="--benchmark-app")return PerformanceBenchmark.Run(args[1]);
            if(args.Length>=2 && args[0]=="--test-live-ui")return LiveUiBenchmark.Run(args[1]);
            if(args.Length>=2 && args[0]=="--export-demo-images")return DemoImages.Export(args[1]);
            if(args.Length>=2 && args[0]=="--test-live-ddc")return HardwareBenchmark.VerifyLive(args[1]).GetAwaiter().GetResult();
            if(args.Length>=2 && args[0]=="--test-focus")return TestFocus(args[1]).GetAwaiter().GetResult();
            if(args.Length>=2 && args[0]=="--test-timeout")return TestTimeout(args[1]).GetAwaiter().GetResult();
            if(args.Length>=3 && args[0]=="--test-windows")return TestWindows(args[1],args[2]);
            using var mutex=new Mutex(true,"Local\\MonitorPilot.Instance",out bool first);
            if(!first)
            {
                using var signal=EventWaitHandle.OpenExisting(args.Contains("--exit")?"Local\\MonitorPilot.Exit":args.Contains("--restore")?"Local\\MonitorPilot.Restore":"Local\\MonitorPilot.Show");signal.Set();return 0;
            }
            if(args.Contains("--exit"))return 0;
            using var show=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\MonitorPilot.Show");
            using var restore=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\MonitorPilot.Restore");
            using var quit=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\MonitorPilot.Exit");
            var app=new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Theme.Install(app);
            app.DispatcherUnhandledException+=(_,e)=>{Store.Log(e.Exception.ToString());MessageBox.Show(e.Exception.Message,"MonitorPilot",MessageBoxButton.OK,MessageBoxImage.Error);e.Handled=true;};
            var main=new MainWindow(args.Contains("--tray"),args.Contains("--restore"));
            var showRegistration=ThreadPool.RegisterWaitForSingleObject(show,(_,_)=>app.Dispatcher.BeginInvoke(main.ShowApp),null,-1,false);
            var restoreRegistration=ThreadPool.RegisterWaitForSingleObject(restore,(_,_)=>app.Dispatcher.BeginInvoke(main.RestoreFromOutside),null,-1,false);
            var quitRegistration=ThreadPool.RegisterWaitForSingleObject(quit,(_,_)=>app.Dispatcher.BeginInvoke(main.ExitApp),null,-1,false);
            int result=app.Run(main);showRegistration.Unregister(null);restoreRegistration.Unregister(null);quitRegistration.Unregister(null);mutex.ReleaseMutex();return result;
        }
        catch(Exception ex)
        {
            Store.Log("Fatal: "+ex);
            if(args.Length>=2 && args[0].StartsWith("--",StringComparison.Ordinal))File.WriteAllText(args[1]+".error",ex.ToString());
            else MessageBox.Show(ex.Message,"MonitorPilot konnte nicht starten",MessageBoxButton.OK,MessageBoxImage.Error);
            return 1;
        }
    }
    private static void Write(string path,object data) {Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(data,Store.Json));}
    private static int Diagnose(string path)
    {
        var displays=new DisplayService();var monitors=displays.Enumerate();var hardware=new HardwareService();foreach(var m in monitors.Where(m=>m.Active))hardware.Probe(m);
        var snapshot=displays.Capture();displays.Validate(displays.Resolve(snapshot));
        var focus=new List<object>();
        foreach(var m in monitors.Where(m=>m.Active))
        {
            try {var subset=DisplayService.Select(snapshot,new[]{m.Key},m.Key);displays.Validate(displays.Resolve(subset));focus.Add(new{Monitor=m.Name,Valid=true});}
            catch(Exception ex) {focus.Add(new{Monitor=m.Name,Valid=false,Error=ex.Message});}
        }
        Write(path,new {Version="1.3.0",Captured=DateTimeOffset.Now,OS=Environment.OSVersion.ToString(),Monitors=monitors,ActiveSnapshotValid=true,FocusValidation=focus,OpenWindowCount=new WindowService().Capture(monitors).Count,InteropSizes=new{Path=Marshal.SizeOf<Native.DisplayPath>(),Mode=Marshal.SizeOf<Native.DisplayMode>(),DevMode=Marshal.SizeOf<Native.DevMode>(),TargetName=Marshal.SizeOf<Native.TargetName>()}});return 0;
    }
    private static int TestDdc(string path)
    {
        var hardware=new HardwareService();var monitors=new DisplayService().Enumerate();var results=new List<object>();bool failed=false;
        foreach(var monitor in monitors.Where(m=>m.Active))
        {
            hardware.Probe(monitor);var original=Store.Clone(monitor.Hardware);if(original.Brightness==null){results.Add(new{Monitor=monitor.Name,Supported=false});continue;}
            Store.Save("qa-hardware-before-"+monitor.Number+".json",original);
            HardwareState? changed=null,restored=null;string? error=null;
            try
            {
                var desired=Store.Clone(original.Brightness);desired.Current=desired.Current+2<=desired.Max?desired.Current+2:Math.Max(desired.Min,desired.Current-2);
                changed=hardware.Apply(monitor,new(){Brightness=desired},false);
            }
            catch(Exception ex){error=ex.Message;failed=true;}
            finally
            {
                try{restored=hardware.Apply(monitor,new(){Brightness=original.Brightness},false);}catch(Exception ex){error+=" RESTORE: "+ex.Message;failed=true;}
            }
            results.Add(new{Monitor=monitor.Name,Supported=true,Before=original.Brightness.Current,Changed=changed?.Brightness?.Current,Restored=restored?.Brightness?.Current,Error=error});
        }
        Write(path,new{Passed=!failed,Results=results});return failed?1:0;
    }
    private static async Task<int> TestFocus(string path)
    {
        var displays=new DisplayService();var windows=new WindowService();var monitors=displays.Enumerate();var main=monitors.First(m=>m.Primary);
        var before=new WorkspaceSnapshot{Name="QA Fokus-Rückkehr",Topology=displays.Capture(),Windows=windows.Capture(monitors)};
        var target=DisplayService.Select(before.Topology,new[]{main.Key},main.Key);displays.Validate(displays.Resolve(target));
        using var guard=new RecoveryGuard();await guard.Arm(before);
        int focused=0;RecoveryResult? rollback=null;string? error=null;
        try {await Task.Run(()=>displays.Apply(target));await Task.Delay(1600);focused=displays.Enumerate().Count(m=>m.Active);}
        catch(Exception ex){error=ex.Message;}
        finally{rollback=await guard.Rollback();}
        var after=displays.Capture();bool same=Fingerprint(before.Topology)==Fingerprint(after);
        var now=windows.Capture(displays.Enumerate());
        int matched=before.Windows.Count(w=>now.Any(n=>n.Handle==w.Handle&&n.ProcessId==w.ProcessId&&PlacementEqual(n,w)));
        bool passed=focused==1&&rollback.Restored&&same&&error==null&&matched==before.Windows.Count;
        Write(path,new {Passed=passed,FocusedActiveCount=focused,RestoredActiveCount=after.Identities.Count,ExactLayoutRestored=same,BeforeWindows=before.Windows.Count,ExactWindowPlacements=matched,Recovery=rollback,Error=error,WindowDifferences=WindowDifferences(before.Windows,now),BeforeLayout=Fingerprint(before.Topology),AfterLayout=Fingerprint(after)});return passed?0:1;
    }
    private static object[] WindowDifferences(List<SavedWindow> before,List<SavedWindow> after)=>before.Where(w=>!after.Any(n=>n.Handle==w.Handle&&n.ProcessId==w.ProcessId&&PlacementEqual(n,w))).Select(w=>
    {
        var n=after.FirstOrDefault(x=>x.Handle==w.Handle&&x.ProcessId==w.ProcessId);string process="unavailable";try{using var p=Process.GetProcessById((int)w.ProcessId);process=p.ProcessName;}catch{}
        return (object)new{Process=process,Before=DisplayService.Unpack<Native.WindowPlacement>(w.Placement)[0],After=n==null?(Native.WindowPlacement?)null:DisplayService.Unpack<Native.WindowPlacement>(n.Placement)[0]};
    }).ToArray();
    private static int TestWindows(string snapshotPath,string resultPath)
    {
        var snapshot=JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(snapshotPath),Store.Json)!;var windows=new WindowService();var monitors=new DisplayService().Enumerate();
        var before=windows.Capture(monitors);var result=windows.Restore(snapshot.Windows);Thread.Sleep(1500);var after=windows.Capture(monitors);
        Write(resultPath,new{Restore=result,BeforeDifferences=WindowDifferences(snapshot.Windows,before),AfterDifferences=WindowDifferences(snapshot.Windows,after)});return 0;
    }
    private static bool PlacementEqual(SavedWindow a,SavedWindow b)
    {
        var x=DisplayService.Unpack<Native.WindowPlacement>(a.Placement)[0];var y=DisplayService.Unpack<Native.WindowPlacement>(b.Placement)[0];
        return x.ShowCmd==y.ShowCmd && x.Normal.Left==y.Normal.Left && x.Normal.Top==y.Normal.Top && x.Normal.Right==y.Normal.Right && x.Normal.Bottom==y.Normal.Bottom;
    }
    private static async Task<int> TestTimeout(string path)
    {
        var displays=new DisplayService();var monitors=displays.Enumerate();var main=monitors.First(m=>m.Primary);
        var before=new WorkspaceSnapshot{Name="QA automatischer Fokus-Timeout",Topology=displays.Capture(),Windows=new WindowService().Capture(monitors)};
        var target=DisplayService.Select(before.Topology,new[]{main.Key},main.Key);displays.Validate(target);
        using var guard=new RecoveryGuard();await guard.Arm(before);await Task.Run(()=>displays.Apply(target));
        int focused=displays.Enumerate().Count(m=>m.Active);
        await Task.Delay(TimeSpan.FromSeconds(RecoveryGuard.TimeoutSeconds+1));
        var result=await guard.Rollback();bool exact=Fingerprint(before.Topology)==Fingerprint(displays.Capture());Write(path,new{Passed=result.Restored&&exact&&focused==1,FocusedActiveCount=focused,TimedOutAfterSeconds=guard.Elapsed.Elapsed.TotalSeconds,ExactLayoutRestored=exact,Recovery=result});return result.Restored&&exact&&focused==1?0:1;
    }
    internal static string Fingerprint(TopologySnapshot topology)
    {
        var paths=DisplayService.Unpack<Native.DisplayPath>(topology.Paths);var modes=DisplayService.Unpack<Native.DisplayMode>(topology.Modes);
        return string.Join("\n",paths.Select((p,i)=>{var m=modes[p.Source.ModeIndex].Source;return $"{topology.Identities[i].Key}|{m.Width}x{m.Height}@{m.Position.X},{m.Position.Y}|{p.Target.Refresh.Numerator}/{p.Target.Refresh.Denominator}|{p.Target.Rotation}";}).OrderBy(x=>x));
    }
}
