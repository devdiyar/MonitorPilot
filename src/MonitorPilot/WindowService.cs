using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MonitorPilot;

internal sealed class WindowService
{
    public List<SavedWindow> Capture(List<MonitorDevice> monitors)
    {
        var result=new List<SavedWindow>();
        Native.EnumWindows((h,_)=>
        {
            try
            {
                if(!Native.IsWindowVisible(h) || Native.GetWindow(h,4)!=IntPtr.Zero || (Native.GetWindowLongPtr(h,-20).ToInt64() & 0x80)!=0) return true;
                if(Native.DwmGetWindowAttribute(h,14,out int cloaked,4)==0 && cloaked!=0) return true;
                Native.GetWindowThreadProcessId(h,out uint pid);
                if(pid==Environment.ProcessId) return true;
                var title=new StringBuilder(512); Native.GetWindowText(h,title,title.Capacity);
                if(title.Length==0) return true;
                var placement=new Native.WindowPlacement {Length=(uint)Marshal.SizeOf<Native.WindowPlacement>()};
                if(!Native.GetWindowPlacement(h,ref placement) || placement.Normal.Width<40 || placement.Normal.Height<40) return true;
                using var process=Process.GetProcessById((int)pid);
                result.Add(new() { Handle=h.ToInt64(), ProcessId=pid, ProcessStart=process.StartTime.ToUniversalTime().Ticks,Title=title.ToString(),Placement=DisplayService.Pack(new[] {placement}),MonitorKey=monitors.FirstOrDefault(m=>m.Handle==Native.MonitorFromWindow(h,2))?.Key ?? "" });
            }
            catch { }
            return true;
        },IntPtr.Zero);
        return result;
    }
    private static bool Matches(SavedWindow window)
    {
        try
        {
            var h=new IntPtr(window.Handle);
            if(!Native.IsWindow(h)) return false;
            Native.GetWindowThreadProcessId(h,out uint pid); if(pid!=window.ProcessId) return false;
            using var process=Process.GetProcessById((int)pid);
            return process.StartTime.ToUniversalTime().Ticks==window.ProcessStart;
        }
        catch { return false; }
    }
    public (int Restored,int Skipped) Restore(List<SavedWindow> windows)
    {
        var valid=windows.Where(w=>w.Placement.Length==Marshal.SizeOf<Native.WindowPlacement>()&&Matches(w)).ToList();
        // Some apps normalize maximized bounds after WM_DISPLAYCHANGE. Reapply only mismatches
        // during a short bounded settle period and verify the final placement, not merely the API return.
        for(int attempt=0;attempt<4;attempt++)
        {
            foreach(var w in valid)
            {
                if(!Matches(w)||PlacementMatches(w))continue;
                var placement=DisplayService.Unpack<Native.WindowPlacement>(w.Placement)[0];
                placement.Length=(uint)Marshal.SizeOf<Native.WindowPlacement>();
                Native.SetWindowPlacement(new IntPtr(w.Handle),ref placement);
            }
            Thread.Sleep(350);
            if(valid.All(PlacementMatches))break;
        }
        int restored=valid.Count(PlacementMatches),skipped=windows.Count-restored;
        Store.Log($"Window restore: {restored} restored, {skipped} unavailable"); return (restored,skipped);
    }
    private static bool PlacementMatches(SavedWindow w)
    {
        if(!Matches(w))return false;
        var saved=DisplayService.Unpack<Native.WindowPlacement>(w.Placement)[0];
        var current=new Native.WindowPlacement{Length=(uint)Marshal.SizeOf<Native.WindowPlacement>()};
        if(!Native.GetWindowPlacement(new IntPtr(w.Handle),ref current))return false;
        return current.ShowCmd==saved.ShowCmd && current.Normal.Left==saved.Normal.Left && current.Normal.Top==saved.Normal.Top && current.Normal.Right==saved.Normal.Right && current.Normal.Bottom==saved.Normal.Bottom;
    }
    public int Gather(MonitorDevice target,List<SavedWindow> windows)
    {
        var mi=new Native.MonitorInfo {Size=Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(target.Handle,ref mi)) throw new InvalidOperationException("Zielmonitor nicht erreichbar.");
        int moved=0;
        foreach(var w in windows)
        {
            if(!Matches(w)) continue;
            var h=new IntPtr(w.Handle);
            if(Native.MonitorFromWindow(h,2)==target.Handle) continue;
            if(!Native.GetWindowRect(h,out var rect)) continue;
            var saved=DisplayService.Unpack<Native.WindowPlacement>(w.Placement)[0];
            if(saved.ShowCmd==2) continue;
            int width=Math.Min(Math.Max(300,rect.Width),mi.Work.Width),height=Math.Min(Math.Max(200,rect.Height),mi.Work.Height);
            if(saved.ShowCmd==3) Native.ShowWindow(h,4);
            bool ok=Native.SetWindowPos(h,IntPtr.Zero,mi.Work.Left+Math.Min(24*(moved%6),Math.Max(0,mi.Work.Width-width)),mi.Work.Top+Math.Min(24*(moved%6),Math.Max(0,mi.Work.Height-height)),width,height,0x4|0x10|0x400);
            if(saved.ShowCmd==3) Native.ShowWindow(h,3);
            if(ok) moved++;
        }
        return moved;
    }
}
