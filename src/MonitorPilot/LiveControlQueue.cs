using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MonitorPilot;

internal enum MonitorControl { Brightness,Contrast,Red,Green,Blue }

internal sealed class LiveControlQueue
{
    private sealed class Request
    {
        public required MonitorDevice Monitor;
        public long Order;
        public readonly Dictionary<MonitorControl,int> Values=new();
        public uint? Temperature;
    }
    private readonly object gate=new();
    private readonly Dictionary<string,Request> pending=new(StringComparer.OrdinalIgnoreCase),inFlight=new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> workers=new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<MonitorDevice,HardwareState,Task> apply;
    private TaskCompletionSource? idle;
    private long sequence;
    public LiveControlQueue(Func<MonitorDevice,HardwareState,Task> apply)=>this.apply=apply;
    public event Action<MonitorDevice,Exception?>? Completed;
    public event Action? Settled;
    public bool IsRunning {get{lock(gate)return workers.Count>0;}}
    public static ControlValue? Control(HardwareState state,MonitorControl control)=>control switch
    {
        MonitorControl.Brightness=>state.Brightness,MonitorControl.Contrast=>state.Contrast,
        MonitorControl.Red=>state.Red,MonitorControl.Green=>state.Green,MonitorControl.Blue=>state.Blue,_=>null
    };
    private static string Bus(MonitorDevice m)=>string.IsNullOrEmpty(m.AdapterKey)?m.Key:m.AdapterKey;
    public void Enqueue(MonitorDevice monitor,MonitorControl control,int percent)
    {
        if(!monitor.Active||Control(monitor.Hardware,control)==null)return;
        lock(gate)
        {
            var request=Pending(monitor);request.Values[control]=Math.Clamp(percent,0,100);Start(Bus(monitor));
        }
    }
    public void EnqueueTemperature(MonitorDevice monitor,uint temperature)
    {
        if(!monitor.Active||temperature<1||temperature>8||(monitor.Hardware.TemperatureMask&(1u<<((int)temperature-1)))==0)return;
        lock(gate)
        {
            var request=Pending(monitor);request.Temperature=temperature;
            // A later preset replaces earlier RGB edits. Edits after it are retained.
            request.Values.Remove(MonitorControl.Red);request.Values.Remove(MonitorControl.Green);request.Values.Remove(MonitorControl.Blue);Start(Bus(monitor));
        }
    }
    private Request Pending(MonitorDevice monitor)
    {
        if(!pending.TryGetValue(monitor.Key,out var request))pending[monitor.Key]=request=new(){Monitor=monitor,Order=++sequence};
        return request;
    }
    private void Start(string bus)
    {
        if(workers.Contains(bus))return;
        if(workers.Count==0)idle=new(TaskCreationOptions.RunContinuationsAsynchronously);
        workers.Add(bus);_=Task.Run(()=>Process(bus));
    }
    public int? DesiredPercent(string key,MonitorControl control)
    {
        lock(gate)
        {
            if(pending.TryGetValue(key,out var next))
            {
                if(next.Values.TryGetValue(control,out int value))return value;
                if(next.Temperature.HasValue&&control is MonitorControl.Red or MonitorControl.Green or MonitorControl.Blue)return null;
            }
            return inFlight.TryGetValue(key,out var current)&&current.Values.TryGetValue(control,out int active)?active:null;
        }
    }
    public uint? DesiredTemperature(string key)
    {
        lock(gate)
            return pending.TryGetValue(key,out var next)&&next.Temperature.HasValue?next.Temperature:inFlight.TryGetValue(key,out var current)?current.Temperature:null;
    }
    public Task DrainAsync(){lock(gate)return workers.Count==0?Task.CompletedTask:idle!.Task;}
    private async Task Process(string bus)
    {
        long started=Stopwatch.GetTimestamp();
        while(true)
        {
            // One short leading interval groups a mouse/keyboard burst. Native DDC
            // normally takes longer; no extra delay is added after those calls.
            double wait=25-Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if(wait>0)await Task.Delay(TimeSpan.FromMilliseconds(wait)).ConfigureAwait(false);
            Request? request;bool settled=false;
            lock(gate)
            {
                request=pending.Values.Where(r=>Bus(r.Monitor)==bus).MinBy(r=>r.Order);
                if(request==null)
                {
                    workers.Remove(bus);settled=workers.Count==0;
                    if(settled)idle!.TrySetResult();
                }
                else {pending.Remove(request.Monitor.Key);inFlight[request.Monitor.Key]=request;}
            }
            if(request==null){if(settled)Settled?.Invoke();return;}
            started=Stopwatch.GetTimestamp();Exception? error=null;
            try {await apply(request.Monitor,Desired(request)).ConfigureAwait(false);}
            catch(Exception ex){error=ex;}
            lock(gate)
            {
                inFlight.Remove(request.Monitor.Key);
                if(error!=null)pending.Remove(request.Monitor.Key);
            }
            Completed?.Invoke(request.Monitor,error);
        }
    }
    private static HardwareState Desired(Request request)
    {
        ControlValue? Value(MonitorControl control)
        {
            if(!request.Values.TryGetValue(control,out int percent))return null;
            var current=Control(request.Monitor.Hardware,control);
            if(current==null)throw new InvalidOperationException("Der Monitorregler ist nicht mehr erreichbar.");
            return new(){Min=current.Min,Max=current.Max,Current=current.FromPercent(percent),ViaVcp=current.ViaVcp};
        }
        return new(){Brightness=Value(MonitorControl.Brightness),Contrast=Value(MonitorControl.Contrast),Red=Value(MonitorControl.Red),Green=Value(MonitorControl.Green),Blue=Value(MonitorControl.Blue),Temperature=request.Temperature};
    }
}
