using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonitorPilot;

internal static class LiveControlTests
{
    public static void Register(Action<string,Action> test)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Async(string name,Func<Task> action)=>test(name,()=>Task.Run(action).GetAwaiter().GetResult());
        Async("Live sliders deliver the last position without a backlog",async()=>
        {
            var monitor=Monitor("a");var entered=Signal();var release=Signal();int calls=0,active=0,overlap=0;
            var queue=new LiveControlQueue(async(m,desired)=>
            {
                if(Interlocked.Increment(ref active)>1)overlap++;
                if(Interlocked.Increment(ref calls)==1){entered.TrySetResult();await release.Task;}
                Apply(m,desired);Interlocked.Decrement(ref active);
            });
            queue.Enqueue(monitor,MonitorControl.Brightness,20);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for(int i=0;i<200;i++)queue.Enqueue(monitor,MonitorControl.Brightness,i%101);
            queue.Enqueue(monitor,MonitorControl.Contrast,73);queue.Enqueue(monitor,MonitorControl.Red,20);queue.Enqueue(monitor,MonitorControl.Brightness,67);
            Check(queue.DesiredPercent(monitor.Key,MonitorControl.Brightness)==67,"UI target was replaced by an older in-flight value");
            var drained=queue.DrainAsync();Check(!drained.IsCompleted,"Snapshot barrier skipped an in-flight monitor write");release.TrySetResult();await drained.WaitAsync(TimeSpan.FromSeconds(3));
            Check(monitor.Hardware.Brightness!.Current==144,"Last brightness position did not respect the 10..210 monitor range");
            Check(monitor.Hardware.Contrast!.Current==73&&monitor.Hardware.Red!.Current==20,"Pending adjustments to different controls were lost");
            Check(calls==2&&overlap==0,"Slider movements created a backlog or overlapping writes");
            Check(!queue.IsRunning&&queue.DesiredPercent(monitor.Key,MonitorControl.Brightness)==null,"Confirmed state remained marked as pending");
        });
        Async("Shared adapter keeps other monitors pending until their latest value",async()=>
        {
            var a=Monitor("a");var b=Monitor("b");var entered=Signal();var release=Signal();int active=0,overlap=0,bCalls=0;
            var queue=new LiveControlQueue(async(m,desired)=>
            {
                if(Interlocked.Increment(ref active)>1)overlap++;
                if(m.Key=="a"){entered.TrySetResult();await release.Task;}else bCalls++;
                Apply(m,desired);Interlocked.Decrement(ref active);
            });
            queue.Enqueue(a,MonitorControl.Brightness,25);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            queue.Enqueue(b,MonitorControl.Brightness,10);queue.Enqueue(b,MonitorControl.Brightness,35);queue.Enqueue(b,MonitorControl.Brightness,86);
            release.TrySetResult();await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(b.Hardware.Brightness!.Current==182&&bCalls==1&&overlap==0,"Shared GPU processed stale values or interleaved monitor writes");
        });
        Async("Live controls on different adapters progress independently",async()=>
        {
            var a=Monitor("a");var b=Monitor("b");a.AdapterKey="gpu-a";b.AdapterKey="gpu-b";
            using var entered=new CountdownEvent(2);var release=Signal();
            var queue=new LiveControlQueue(async(m,desired)=>{entered.Signal();await release.Task;Apply(m,desired);});
            queue.Enqueue(a,MonitorControl.Brightness,30);queue.Enqueue(b,MonitorControl.Brightness,40);
            Check(entered.Wait(3000),"Independent adapters blocked each other");release.TrySetResult();await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(a.Hardware.Brightness!.Current==70&&b.Hardware.Brightness!.Current==90,"Independent monitor target was lost");
        });
        Async("Live queue stops failed work and accepts a later correction",async()=>
        {
            var m=Monitor("a");int calls=0,failures=0;
            var queue=new LiveControlQueue((monitor,desired)=>{if(++calls==1)throw new InvalidOperationException("Disconnected");Apply(monitor,desired);return Task.CompletedTask;});
            queue.Completed+=(_,error)=>{if(error!=null)failures++;};
            queue.Enqueue(m,MonitorControl.Brightness,90);await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(failures==1&&!queue.IsRunning,"Failed command was hidden or retried without an end");
            queue.Enqueue(m,MonitorControl.Brightness,23);await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(calls==2&&m.Hardware.Brightness!.Current==56,"Queue stopped accepting values after a device failure");
        });
        Async("A white point preset supersedes earlier RGB drafts",async()=>
        {
            var m=Monitor("a");var entered=Signal();var release=Signal();int calls=0;
            var queue=new LiveControlQueue(async(monitor,desired)=>{if(++calls==1){entered.TrySetResult();await release.Task;}Apply(monitor,desired);});
            queue.Enqueue(m,MonitorControl.Brightness,25);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            queue.Enqueue(m,MonitorControl.Red,15);queue.Enqueue(m,MonitorControl.Green,20);queue.EnqueueTemperature(m,3);queue.Enqueue(m,MonitorControl.Blue,35);
            release.TrySetResult();await queue.DrainAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(m.Hardware.Temperature==3&&m.Hardware.Red!.Current==50&&m.Hardware.Green!.Current==50&&m.Hardware.Blue!.Current==35,"Preset ordering restored obsolete RGB values or dropped the later RGB adjustment");
        });
        Async("Unsupported live controls never open a monitor connection",async()=>
        {
            var m=new MonitorDevice{Key="unsupported",Active=true};int calls=0;
            var queue=new LiveControlQueue((_,_)=>{calls++;return Task.CompletedTask;});
            queue.Enqueue(m,MonitorControl.Brightness,25);await queue.DrainAsync();
            Check(calls==0&&!queue.IsRunning,"Unsupported slider attempted hardware access");
        });
    }
    internal static MonitorDevice Monitor(string key)=>new(){Key=key,AdapterKey="shared-gpu",Active=true,Hardware=new(){Brightness=new(){Min=10,Max=210,Current=110},Contrast=new(){Min=0,Max=100,Current=50},Red=new(){Min=0,Max=100,Current=50},Green=new(){Min=0,Max=100,Current=50},Blue=new(){Min=0,Max=100,Current=50},TemperatureMask=36}};
    internal static void Apply(MonitorDevice m,HardwareState desired)
    {
        var state=Store.Clone(m.Hardware);
        if(desired.Temperature.HasValue){state.Temperature=desired.Temperature;state.Red!.Current=50;state.Green!.Current=50;state.Blue!.Current=50;}
        if(desired.Brightness!=null)state.Brightness=Store.Clone(desired.Brightness);
        if(desired.Contrast!=null)state.Contrast=Store.Clone(desired.Contrast);
        if(desired.Red!=null)state.Red=Store.Clone(desired.Red);
        if(desired.Green!=null)state.Green=Store.Clone(desired.Green);
        if(desired.Blue!=null)state.Blue=Store.Clone(desired.Blue);
        m.Hardware=state;
    }
    private static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
}
