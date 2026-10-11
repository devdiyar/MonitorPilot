using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MonitorPilot;

// Read-only local benchmark. No topology changes, profile writes or hardware writes.
internal static class PerformanceBenchmark
{
    public static int Run(string path)
    {
        var results=new List<object>();var displays=new DisplayService();var controller=new AppController();controller.Load();
        double Measure(string name,Action action)
        {
            var clock=Stopwatch.StartNew();action();double ms=clock.Elapsed.TotalMilliseconds;
            results.Add(new{Name=name,Milliseconds=Math.Round(ms,1)});return ms;
        }
        var monitors=new List<MonitorDevice>();
        Measure("EnumerateTopology",()=>monitors=displays.Enumerate());
        double cold=Measure("ColdRefresh",()=>controller.Refresh().GetAwaiter().GetResult());
        double warm=Measure("WarmRefresh",()=>controller.Refresh().GetAwaiter().GetResult());
        WorkspaceSnapshot? captured=null;
        double capture=Measure("CaptureWorkspace",()=>captured=controller.Capture("Performance QA").GetAwaiter().GetResult());
        var startup=new AppController();startup.Load();var startupClock=Stopwatch.StartNew();double brightnessReady=0;
        startup.HardwareDiscovered+=()=>{if(brightnessReady==0)brightnessReady=startupClock.Elapsed.TotalMilliseconds;};
        double initialization=Measure("InitializeHardware",()=>startup.Initialize().GetAwaiter().GetResult());
        results.Add(new{Name="StartupBrightnessReady",Milliseconds=Math.Round(brightnessReady,1)});
        foreach(var m in monitors.Where(m=>m.Active))
        {
            Measure("DisplayModes:"+m.Name,()=>DisplayService.Modes(m.DeviceName));
            Measure("DisplayModesRepeated:"+m.Name,()=>DisplayService.Modes(m.DeviceName));
        }
        var app=new System.Windows.Application();MainWindow? window=null;
        Measure("ConstructMainWindow",()=>{Theme.Install(app);window=new MainWindow(true,controller:new AppController((m,desired)=>{LiveControlTests.Apply(m,desired);return System.Threading.Tasks.Task.CompletedTask;}));});
        var uiWindow=window!;
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var uiController=(AppController)typeof(MainWindow).GetField("c",flags)!.GetValue(uiWindow)!;
        typeof(AppController).GetProperty(nameof(AppController.Monitors))!.SetValue(uiController,controller.Monitors);
        var currentPage=typeof(MainWindow).GetField("currentPage",flags)!;var render=typeof(MainWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null)!;
        foreach(string page in new[]{"monitors","colors","profiles","calibrate","settings","colors","monitors"})
            Measure("Render:"+page,()=>{currentPage.SetValue(uiWindow,page);render.Invoke(uiWindow,null);uiWindow.Measure(new System.Windows.Size(1440,980));uiWindow.Arrange(new System.Windows.Rect(0,0,1440,980));uiWindow.UpdateLayout();});
        var uiChecks=VerifyDiscoveryDrafts(uiWindow,uiController,flags);
        LiveUiBenchmark.Wait(uiController.Live.DrainAsync());
        using var process=Process.GetCurrentProcess();process.Refresh();
        var resource=new{WorkingSetBytes=process.WorkingSet64,PrivateBytes=process.PrivateMemorySize64,ManagedHeapBytes=GC.GetTotalMemory(false),CpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds};
        bool complete=controller.Monitors.Count(m=>m.Active)==captured!.Hardware.Count&&captured.Hardware.Values.All(h=>h.Brightness!=null);
        bool fast=warm<3000&&capture<3000&&brightnessReady>0&&brightnessReady<1000;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bool drafts=uiChecks.All(check=>check.Passed);
        File.WriteAllText(path,JsonSerializer.Serialize(new{Version=typeof(Program).Assembly.GetName().Version?.ToString(),Captured=DateTimeOffset.Now,Passed=complete&&fast&&drafts,FreshHardwareCaptured=complete,UiDraftsPreserved=drafts,UiChecks=uiChecks,WithinBudget=fast,ActiveMonitors=controller.Monitors.Count(m=>m.Active),Resources=resource,Results=results},Store.Json));
        app.Shutdown();return complete&&fast&&drafts?0:1;
    }
    private sealed record UiCheck(string Name,bool Passed);
    private static List<UiCheck> VerifyDiscoveryDrafts(MainWindow window,AppController controller,BindingFlags flags)
    {
        // In-process WPF regression on model copies: no native monitor writes.
        typeof(AppController).GetProperty(nameof(AppController.Monitors))!.SetValue(controller,Store.Clone(controller.Monitors));
        typeof(MainWindow).GetField("loaded",flags)!.SetValue(window,true);
        typeof(MainWindow).GetField("currentPage",flags)!.SetValue(window,"colors");
        typeof(MainWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null)!.Invoke(window,null);
        window.Measure(new Size(1440,980));window.Arrange(new Rect(0,0,1440,980));window.UpdateLayout();
        var monitor=controller.Main!;var slider=Find<Slider>(window,L.T("Helligkeit"));var contrast=Find<Slider>(window,L.T("Kontrast"));var naming=Find<TextBox>(window,L.T("Monitorname"));
        int draft=Math.Max(0,monitor.Hardware.Brightness!.Percent-3);slider.Value=draft;naming.Text="Unübernommener Monitorname";
        var fresh=Store.Clone(monitor.Hardware);fresh.Brightness!.Current=fresh.Brightness.FromPercent(Math.Min(100,fresh.Brightness.Percent+1));fresh.Contrast!.Current=fresh.Contrast.FromPercent(51);monitor.Hardware=fresh;
        var ready=(Action)typeof(AppController).GetField("HardwareDiscovered",flags)!.GetValue(controller)!;ready();
        var checks=new List<UiCheck>{new("Color draft survives hardware discovery",slider.Value==draft&&ReferenceEquals(slider,Find<Slider>(window,L.T("Helligkeit")))),new("Untouched control receives fresh hardware value",contrast.Value==51),new("Monitor name draft survives hardware discovery",naming.Text=="Unübernommener Monitorname")};
        var quick=new TrayQuickWindow(controller,()=>{},()=>{},()=>{});quick.Measure(new Size(430,850));quick.Arrange(new Rect(0,0,430,850));quick.UpdateLayout();
        var quickSlider=Find<Slider>(quick,controller.Name(monitor)+L.T(": Helligkeit"));quickSlider.Value=draft;
        monitor.Hardware=Store.Clone(fresh);monitor.Hardware.Brightness!.Current=monitor.Hardware.Brightness.FromPercent(Math.Min(100,fresh.Brightness.Percent+1));ready();
        checks.Add(new("Tray brightness draft survives hardware discovery",quickSlider.Value==draft&&ReferenceEquals(quickSlider,Find<Slider>(quick,controller.Name(monitor)+L.T(": Helligkeit")))));
        quick.Close();typeof(MainWindow).GetField("loaded",flags)!.SetValue(window,false);return checks;
    }
    private static T Find<T>(DependencyObject root,string name) where T:DependencyObject
        =>Descendants(root).OfType<T>().First(item=>AutomationProperties.GetName(item)==name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var element in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var child in Descendants(element))yield return child;
    }
}
