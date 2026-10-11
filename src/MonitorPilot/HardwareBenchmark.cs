using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MonitorPilot;

// Explicit opt-in hardware regression: small changes, guaranteed restoration attempt,
// and an independent full read after each round trip. Never runs at app startup.
internal static class HardwareBenchmark
{
    public static int Run(string path)
    {
        var hardware=new HardwareService();var results=new List<object>();bool correct=true,fast=true;
        var monitors=new DisplayService().Enumerate().Where(m=>m.Active).ToList();
        if(monitors.Count==0)throw new InvalidOperationException("No active monitor available for the hardware benchmark.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        foreach(var monitor in monitors)
        {
            var clock=Stopwatch.StartNew();hardware.Probe(monitor);double probeMs=clock.Elapsed.TotalMilliseconds;
            var original=Store.Clone(monitor.Hardware);
            File.WriteAllText(path+".before-"+monitor.Number+".json",JsonSerializer.Serialize(original,Store.Json));
            var operations=new List<(string Name,HardwareState Change,HardwareState Restore)>();
            if(original.Brightness!=null) operations.Add(("Brightness",new(){Brightness=Step(original.Brightness)},new(){Brightness=original.Brightness}));
            if(original.Red!=null) operations.Add(("Red",new(){Red=Step(original.Red)},new(){Red=original.Red}));
            foreach(var operation in operations)
            {
                string? error=null;double applyMs=0,restoreMs=0;HardwareState? changed=null;
                try
                {
                    clock.Restart();changed=hardware.Apply(monitor,operation.Change,false);applyMs=clock.Elapsed.TotalMilliseconds;
                    monitor.Hardware=changed;
                }
                catch(Exception ex){error=ex.Message;correct=false;}
                finally
                {
                    try{clock.Restart();monitor.Hardware=hardware.Apply(monitor,operation.Restore,false);restoreMs=clock.Elapsed.TotalMilliseconds;}
                    catch(Exception ex){error+=" RESTORE: "+ex.Message;correct=false;}
                }
                hardware.Probe(monitor);
                bool restored=monitor.DdcStatus.StartsWith("DDC/CI verbunden",StringComparison.Ordinal)&&Same(original,monitor.Hardware);
                if(!restored)
                {
                    // A gain change can affect a monitor preset. Restore the full captured state.
                    try{monitor.Hardware=hardware.Apply(monitor,original,false);hardware.Probe(monitor);restored=monitor.DdcStatus.StartsWith("DDC/CI verbunden",StringComparison.Ordinal)&&Same(original,monitor.Hardware);}
                    catch(Exception ex){error+=" FULL RESTORE: "+ex.Message;}
                }
                bool withinBudget=applyMs>0&&applyMs<1500;
                bool applied=operation.Name=="Brightness"?changed?.Brightness?.Current==operation.Change.Brightness!.Current:changed?.Red?.Current==operation.Change.Red!.Current;
                correct &= restored&&applied&&error==null;fast &= withinBudget;
                results.Add(new{Monitor=monitor.Name,Number=monitor.Number,Operation=operation.Name,ProbeMs=Math.Round(probeMs,1),ApplyMs=Math.Round(applyMs,1),RestoreMs=Math.Round(restoreMs,1),Before=operation.Restore,Requested=operation.Change,Confirmed=changed,AppliedExactly=applied,ExactHardwareRestored=restored,Within1500ms=withinBudget,Error=error});
                File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=correct&&fast,HardwareCorrect=correct,WithinBudget=fast,Results=results},Store.Json));
                Console.WriteLine($"{monitor.Name} {operation.Name}: {applyMs:0} ms, restored={restored}");
                if(!restored) return 1;
            }
        }
        return correct&&fast?0:1;
    }
    public static int VerifyAdjustments(string path)
    {
        var hardware=new HardwareService();var monitor=new DisplayService().Enumerate().First(m=>m.Active);
        hardware.Probe(monitor);var original=Store.Clone(monitor.Hardware);
        if(original.Brightness==null)throw new InvalidOperationException("Brightness is required for the control regression.");
        string oldRoot=Store.Root;var results=new List<object>();bool passed=true,restored=false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".before.json",JsonSerializer.Serialize(original,Store.Json));
        void Check(string name,bool value){results.Add(new{Name=name,Passed=value});passed &= value;}
        // Exercise first-write backups without altering the user's real original values.
        Store.Root=Path.GetFullPath(path)+".data-"+Guid.NewGuid().ToString("N");
        string? error=null;
        try
        {
            var invalid=Store.Clone(original.Brightness);invalid.Current=invalid.Max+1;
            bool rejected=false;
            try{hardware.Apply(monitor,new(){Brightness=invalid},false);}catch(InvalidOperationException){rejected=true;}
            hardware.Probe(monitor);Check("Out-of-range write rejected without hardware change",rejected&&Same(original,monitor.Hardware));

            int step=original.Brightness.Percent<100?1:-1;
            monitor.Hardware.Brightness!.Current=original.Brightness.FromPercent(20);
            var actual=hardware.ApplyBrightness(monitor,step,true);
            Check("Relative brightness uses live OSD value instead of stale cache",actual.Brightness?.Current==original.Brightness.FromPercent(original.Brightness.Percent+step));
            var firstBackup=Store.Read<Dictionary<string,HardwareState>>("hardware-original.json");
            Check("First change saves complete fresh hardware state",firstBackup!=null&&firstBackup.TryGetValue(monitor.Key,out var saved)&&Same(saved,original));
            monitor.Hardware=actual;
            monitor.Hardware=hardware.ApplyBrightness(monitor,original.Brightness.Percent);
            Check("Absolute brightness restores requested value",monitor.Hardware.Brightness?.Current==original.Brightness.Current);
            var secondBackup=Store.Read<Dictionary<string,HardwareState>>("hardware-original.json");
            Check("Subsequent adjustments preserve the original backup",secondBackup!=null&&Same(secondBackup[monitor.Key],original));

            if(original.Red!=null&&original.Green!=null&&original.Blue!=null)
            {
                var desired=new HardwareState{Red=Step(original.Red),Green=Step(original.Green),Blue=Step(original.Blue)};
                actual=hardware.Apply(monitor,desired,false);monitor.Hardware=actual;
                Check("RGB channels can be adjusted together",actual.Red?.Current==desired.Red.Current&&actual.Green?.Current==desired.Green.Current&&actual.Blue?.Current==desired.Blue.Current);
                hardware.Probe(monitor);
                Check("RGB changes preserve brightness and contrast",monitor.Hardware.Brightness?.Current==original.Brightness.Current&&monitor.Hardware.Contrast?.Current==original.Contrast?.Current);
            }
        }
        catch(Exception ex){passed=false;error=ex.ToString();}
        finally
        {
            try{monitor.Hardware=hardware.Apply(monitor,original,false);hardware.Probe(monitor);restored=monitor.DdcStatus.StartsWith("DDC/CI verbunden",StringComparison.Ordinal)&&Same(original,monitor.Hardware);}
            catch(Exception ex){error+=" RESTORE: "+ex;}
            Store.Root=oldRoot;
        }
        Check("Complete hardware state independently confirmed after restoration",restored);
        File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=passed,Monitor=monitor.Name,Results=results,Error=error},Store.Json));
        return passed?0:1;
    }
    private static bool Same(HardwareState left,HardwareState right)
    {
        // A fallback transport flag describes the read path, not a physical setting.
        var a=Store.Clone(left);var b=Store.Clone(right);
        foreach(var control in new[]{a.Brightness,a.Contrast,a.Red,a.Green,a.Blue,b.Brightness,b.Contrast,b.Red,b.Green,b.Blue})if(control!=null)control.ViaVcp=false;
        return JsonSerializer.Serialize(a,Store.Json)==JsonSerializer.Serialize(b,Store.Json);
    }
    public static async Task<int> VerifyParallel(string path)
    {
        var controller=new AppController();controller.Load();await controller.Refresh();
        var monitors=controller.Monitors.Where(m=>m.Active&&m.Hardware.Brightness!=null).ToList();
        if(monitors.Count<2)throw new InvalidOperationException("At least two monitors are required for the parallel hardware regression.");
        var originals=monitors.ToDictionary(m=>m.Key,m=>Store.Clone(m.Hardware));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".before.json",JsonSerializer.Serialize(originals,Store.Json));
        string oldRoot=Store.Root;Store.Root=Path.GetFullPath(path)+".data-"+Guid.NewGuid().ToString("N");
        double firstMs=0,repeatedMs=0;bool applied=false,backupComplete=false,restored=false;string? error=null;
        try
        {
            var clock=Stopwatch.StartNew();await controller.Brightness(-1,true);firstMs=clock.Elapsed.TotalMilliseconds;
            applied=!controller.Error&&monitors.All(m=>m.Hardware.Brightness?.Current==originals[m.Key].Brightness!.FromPercent(Math.Max(0,originals[m.Key].Brightness!.Percent-1)));
            var backup=Store.Read<Dictionary<string,HardwareState>>("hardware-original.json");
            backupComplete=backup!=null&&monitors.All(m=>backup.TryGetValue(m.Key,out var saved)&&Same(saved,originals[m.Key]));
            clock.Restart();await controller.Brightness(1,true);repeatedMs=clock.Elapsed.TotalMilliseconds;
        }
        catch(Exception ex){error=ex.ToString();}
        finally
        {
            var restoreErrors=await Task.WhenAll(monitors.Select(m=>Task.Run(()=>
            {
                try{m.Hardware=controller.Hardware.Apply(m,originals[m.Key],false);return (string?)null;}
                catch(Exception ex){return m.Name+": "+ex.Message;}
            })));
            await Task.WhenAll(monitors.Select(m=>Task.Run(()=>controller.Hardware.Probe(m))));
            restored=restoreErrors.All(e=>e==null)&&monitors.All(m=>Same(originals[m.Key],m.Hardware));
            if(!restored)error+=" RESTORE: "+string.Join("; ",restoreErrors.Where(e=>e!=null));
            Store.Root=oldRoot;
        }
        bool fast=repeatedMs>0&&repeatedMs<1000;
        File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=applied&&backupComplete&&restored&&fast,AppliedExactly=applied,CompleteFirstBackups=backupComplete,ExactHardwareRestored=restored,Within1000ms=fast,FirstChangeMs=Math.Round(firstMs,1),RepeatedBatchMs=Math.Round(repeatedMs,1),MonitorCount=monitors.Count,Error=error},Store.Json));
        return applied&&backupComplete&&restored&&fast?0:1;
    }
    public static async Task<int> VerifyLive(string path)
    {
        var hardware=new HardwareService();var monitors=new DisplayService().Enumerate().Where(m=>m.Active).ToList();
        await Task.WhenAll(monitors.Select(m=>Task.Run(()=>hardware.Probe(m))));
        monitors=monitors.Where(m=>m.Hardware.Brightness!=null).ToList();
        if(monitors.Count<2)throw new InvalidOperationException("At least two DDC monitors are required for the live regression.");
        var originals=monitors.ToDictionary(m=>m.Key,m=>Store.Clone(m.Hardware));var errors=new System.Collections.Concurrent.ConcurrentBag<string>();
        var calls=new System.Collections.Concurrent.ConcurrentDictionary<string,int>();var timings=new System.Collections.Concurrent.ConcurrentBag<object>();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller=new AppController(async(m,desired)=>
        {
            calls.AddOrUpdate(m.Key,1,(_,count)=>count+1);entered.TrySetResult();var clock=Stopwatch.StartNew();
            await Task.Run(()=>hardware.Apply(m,desired));
            timings.Add(new{Monitor=m.Name,Milliseconds=Math.Round(clock.Elapsed.TotalMilliseconds,1),Target=desired.Brightness?.Percent});
            Console.WriteLine($"Live {m.Name}: {clock.Elapsed.TotalMilliseconds:0} ms, target {desired.Brightness?.Percent} %");
        });
        typeof(AppController).GetProperty(nameof(AppController.Monitors))!.SetValue(controller,monitors);
        controller.Live.Completed+=(m,error)=>{if(error!=null)errors.Add(m.Name+": "+error.Message);};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".before.json",JsonSerializer.Serialize(originals,Store.Json));
        string oldRoot=Store.Root;Store.Root=Path.GetFullPath(path)+".data-"+Guid.NewGuid().ToString("N");
        // Simulate existing first-write backups in isolated QA storage.
        Store.Save("hardware-original.json",originals);
        double burstMs=0,flushMs=0;bool targets=false,barrier=false,restored=false;string? errorText=null;
        var levels=monitors.ToDictionary(m=>m.Key,m=>originals[m.Key].Brightness!.Percent+(originals[m.Key].Brightness!.Percent<=98?2:-2));
        try
        {
            foreach(var m in monitors)controller.LiveControl(m,MonitorControl.Brightness,(originals[m.Key].Brightness!.Percent+levels[m.Key])/2);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var clock=Stopwatch.StartNew();
            for(int i=0;i<200;i++)foreach(var m in monitors)controller.LiveControl(m,MonitorControl.Brightness,i%2==0?originals[m.Key].Brightness!.Percent:levels[m.Key]);
            foreach(var m in monitors)controller.LiveControl(m,MonitorControl.Brightness,levels[m.Key]);
            burstMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
            await controller.Run(()=>{barrier=!controller.Live.IsRunning&&monitors.All(m=>m.Hardware.Brightness?.Current==originals[m.Key].Brightness!.FromPercent(levels[m.Key]));return Task.CompletedTask;});
            flushMs=clock.Elapsed.TotalMilliseconds;
            // Independent physical reads verify every final slider target.
            await Task.WhenAll(monitors.Select(m=>Task.Run(()=>hardware.Probe(m))));
            targets=monitors.All(m=>m.Hardware.Brightness?.Current==originals[m.Key].Brightness!.FromPercent(levels[m.Key])&&m.Hardware.Contrast?.Current==originals[m.Key].Contrast?.Current&&m.Hardware.Red?.Current==originals[m.Key].Red?.Current&&m.Hardware.Green?.Current==originals[m.Key].Green?.Current&&m.Hardware.Blue?.Current==originals[m.Key].Blue?.Current);
        }
        catch(Exception ex){errorText=ex.ToString();}
        finally
        {
            await controller.Live.DrainAsync();
            var restoreErrors=await Task.WhenAll(monitors.Select(m=>Task.Run(()=>
            {
                try{hardware.Apply(m,originals[m.Key],false);return (string?)null;}catch(Exception ex){return m.Name+": "+ex.Message;}
            })));
            await Task.WhenAll(monitors.Select(m=>Task.Run(()=>hardware.Probe(m))));
            restored=restoreErrors.All(e=>e==null)&&monitors.All(m=>Same(originals[m.Key],m.Hardware));
            if(!restored)errorText+=" RESTORE: "+string.Join("; ",restoreErrors.Where(e=>e!=null));Store.Root=oldRoot;
        }
        bool coalesced=calls.Values.All(count=>count<=2),fastInput=burstMs<100,fastFlush=flushMs>0&&flushMs<2000;
        bool passed=targets&&barrier&&restored&&coalesced&&fastInput&&fastFlush&&errors.IsEmpty&&errorText==null;
        File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=passed,MonitorCount=monitors.Count,SliderChanges=201*monitors.Count,NativeTransactions=calls,TransactionTimings=timings.ToArray(),ProcessorCount=Environment.ProcessorCount,InputBurstMs=Math.Round(burstMs,1),FinalTargetsConfirmed=targets,WorkspaceBarrierWaited=barrier,FinalValueFlushMs=Math.Round(flushMs,1),Coalesced=coalesced,ExactHardwareRestored=restored,HardwareErrors=errors.ToArray(),Error=errorText},Store.Json));return passed?0:1;
    }
    private static ControlValue Step(ControlValue source)
    {
        var value=Store.Clone(source);value.Current=value.Current<value.Max?value.Current+1:Math.Max(value.Min,value.Current-1);return value;
    }
}
