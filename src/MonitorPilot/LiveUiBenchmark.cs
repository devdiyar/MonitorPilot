using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MonitorPilot;

// Exercises the actual WPF pages and tray window with a simulated DDC boundary.
// Real monitor settings and saved user data are never changed.
internal static class LiveUiBenchmark
{
    public static int Run(string path)
    {
        var checks=new List<object>();bool passed=true;
        void Check(string name,bool value){checks.Add(new{Name=name,Passed=value});passed&=value;}
        string previous=Store.Root;Store.Root=Path.Combine(Path.GetTempPath(),"MonitorPilot-live-ui",Guid.NewGuid().ToString("N"));
        var app=new System.Windows.Application();Theme.Install(app);MainWindow? window=null;TrayQuickWindow? quick=null;
        var previousContext=System.Threading.SynchronizationContext.Current;System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int writes=0;
        var controller=new AppController(async(m,desired)=>{await release.Task;LiveControlTests.Apply(m,desired);System.Threading.Interlocked.Increment(ref writes);});
        try
        {
            window=new MainWindow(true,controller:controller);
            var a=LiveControlTests.Monitor("ui-a");a.Primary=true;a.Name="QA A";a.Number=1;a.X=-30000;a.Y=-30000;a.Width=200;a.Height=200;
            var b=LiveControlTests.Monitor("ui-b");b.Name="QA B";b.Number=2;b.AdapterKey="other-gpu";
            typeof(AppController).GetProperty(nameof(AppController.Monitors))!.SetValue(controller,new List<MonitorDevice>{a,b});
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;typeof(MainWindow).GetField("loaded",flags)!.SetValue(window,true);
            void Page(string name){typeof(MainWindow).GetField("currentPage",flags)!.SetValue(window,name);typeof(MainWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null)!.Invoke(window,null);window.Measure(new Size(1440,980));window.Arrange(new Rect(0,0,1440,980));window.UpdateLayout();}
            Page("colors");var brightness=Find<Slider>(window,"Helligkeit");var red=Find<Slider>(window,"Rot");var contrast=Find<Slider>(window,"Kontrast");
            brightness.Value=20;brightness.Value=40;brightness.Value=67;contrast.Value=73;red.Value=20;Find<Slider>(window,"Grün").Value=55;Find<Slider>(window,"Blau").Value=80;
            Check("Moving color controls starts live work without disabling the page",controller.Live.IsRunning&&!controller.Busy&&brightness.IsEnabled&&red.IsEnabled);
            Check("Color page has no manual values-apply button",!Descendants(window).OfType<Button>().Any(x=>x.Content is string s&&s=="Werte übernehmen"));
            quick=new TrayQuickWindow(controller,()=>{},()=>{},()=>{});var traySlider=Find<Slider>(quick,"QA A: Helligkeit");
            Check("Tray opens at the pending brightness target",traySlider.Value==67);
            traySlider.Value=35;
            Check("Tray slider updates the color page immediately",brightness.Value==35&&controller.Live.DesiredPercent(a.Key,MonitorControl.Brightness)==35);
            Check("Tray has no manual brightness-apply button",!Descendants(quick).OfType<Button>().Any(x=>x.Content is string s&&s=="Helligkeit anwenden"));
            release.TrySetResult();Wait(controller.Live.DrainAsync());Pump();
            Check("Live values reach hardware boundary and preserve control instances",a.Hardware.Brightness!.Current==80&&a.Hardware.Contrast!.Current==73&&a.Hardware.Red!.Current==20&&a.Hardware.Green!.Current==55&&a.Hardware.Blue!.Current==80&&ReferenceEquals(brightness,Find<Slider>(window,"Helligkeit")));
            var master=Find<Slider>(quick,"Alle Monitore: Helligkeit");master.Value=45;master.Value=61;Wait(controller.Live.DrainAsync());Pump();
            Check("Tray master changes every monitor live",a.Hardware.Brightness!.Current==132&&b.Hardware.Brightness!.Current==132);
            Page("monitors");Find<Slider>(window,"Alle Displays").Value=72;Wait(controller.Live.DrainAsync());Pump();
            Check("Overview master changes every monitor live",a.Hardware.Brightness!.Current==154&&b.Hardware.Brightness!.Current==154);
            Page("colors");var dim=Find<Slider>(window,"Abdunklung");dim.Value=1;
            var dimWindows=(Dictionary<string,Window>)typeof(OverlayService).GetField("dimWindows",flags)!.GetValue(controller.Overlays)!;
            var overlay=dimWindows.GetValueOrDefault(a.Key);dim.Value=2;
            Check("Dimming updates live using the existing overlay window",overlay!=null&&ReferenceEquals(overlay,dimWindows.GetValueOrDefault(a.Key))&&Math.Abs(overlay.Opacity-.02)<.0001);
            dim.Value=0;Check("Zero dimming removes the live overlay",!dimWindows.ContainsKey(a.Key)&&controller.Overlays.Levels.GetValueOrDefault(a.Key)==0);
            var temperature=Find<ComboBox>(window,"Hardware-Weißpunkt");temperature.SelectedIndex=1;Wait(controller.Live.DrainAsync());Pump();
            Check("White point applies live and refreshes RGB controls",a.Hardware.Temperature==3&&Find<Slider>(window,"Rot").Value==50&&Find<Slider>(window,"Grün").Value==50&&Find<Slider>(window,"Blau").Value==50);
            controller.LiveControl(a,MonitorControl.Brightness,81);bool captured=false;
            Wait(controller.Run(()=>{captured=!controller.Live.IsRunning&&a.Hardware.Brightness!.Current==172;return Task.CompletedTask;}));
            Check("Workspace operations wait for the final live value",captured);
            var left=new System.Windows.Controls.Border{Height=80};var right=new System.Windows.Controls.Border{Height=60};var columns=Theme.Columns(left,right,300);
            columns.Measure(new Size(560,double.PositiveInfinity));columns.Arrange(new Rect(0,0,560,columns.DesiredSize.Height));columns.UpdateLayout();
            Check("Narrow pages stack both panels without squeezing the controls",right.TranslatePoint(new Point(0,0),columns).Y>=80&&left.ActualWidth>=550&&right.ActualWidth>=550);
            var small=new Window();Theme.FitToWorkArea(small,new Rect(0,0,910,500));
            Check("Main window fits a small scaled work area",small.Width<=910&&small.Height<=500&&small.MinWidth<=small.Width&&small.MinHeight<=small.Height);small.Close();
            Store.Save("profiles.json",new List<ProfileEntry>());var profileBytes=File.ReadAllBytes(Store.FilePath("profiles.json"));
            var aliasesBefore=JsonSerializer.Serialize(controller.Settings.Aliases,Store.Json);
            controller.LiveControl(a,MonitorControl.Brightness,82);
            Page("settings");var language=Descendants(window).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="AppLanguage");language.SelectedIndex=1;
            Page("colors");
            Check("Switching language preserves the pending live brightness target",L.Language=="en"&&Find<Slider>(window,"Brightness").Value==82&&controller.ControlPercent(a,MonitorControl.Brightness)==82);
            Wait(controller.Live.DrainAsync());Pump();
            Check("English controls complete the same live operation",a.Hardware.Brightness!.Current==174&&!controller.Busy);
            quick.Close();quick=new TrayQuickWindow(controller,()=>{},()=>{},()=>{});
            Check("English tray exposes live controls and translated actions",Find<Slider>(quick,"QA A: Brightness").Value==82&&Descendants(quick).OfType<Button>().Any(x=>Equals(x.Content,"Settings"))&&quick.Title=="MonitorPilot · Quick controls");
            Find<Slider>(quick,"All monitors: Brightness").Value=63;Wait(controller.Live.DrainAsync());Pump();
            Check("English tray changes both monitors live without an apply button",a.Hardware.Brightness!.Current==136&&b.Hardware.Brightness!.Current==136&&!Descendants(quick).OfType<Button>().Any(x=>Equals(x.Content,"Apply brightness")));
            bool pagesEnglish=true;
            foreach(var expected in new[]{("monitors","Monitors"),("colors","Picture & color"),("profiles","Workspace profiles"),("calibrate","Color comparison"),("settings","Settings")})
            {
                Page(expected.Item1);pagesEnglish&=Descendants(window).OfType<TextBlock>().Any(x=>x.Text==expected.Item2);
            }
            Check("All five English pages render translated headings",pagesEnglish);
            using(var menu=(System.Windows.Forms.ContextMenuStrip)typeof(MainWindow).GetMethod("CreateTrayMenu",flags)!.Invoke(window,null)!)
            {
                var items=menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToList();
                Check("English native tray menu contains language and translated monitor actions",items.Any(x=>x.Text=="Language"&&x.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Any(i=>i.Text=="English"&&i.Checked))&&items.Any(x=>x.Text=="Extend all monitors")&&items.Any(x=>x.Text=="Exit MonitorPilot"));
                items.Single(x=>x.Text=="Language").DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(x=>x.Text=="Deutsch").PerformClick();
                Check("Tray language selection switches immediately and persists",L.Language=="de"&&Store.Read<AppSettings>("settings.json")!.Language=="de");
            }
            typeof(MainWindow).GetMethod("SetLanguage",flags)!.Invoke(window,new object[]{"en"});Page("settings");
            Check("Language selection persists without rewriting profiles or aliases",Store.Read<AppSettings>("settings.json")!.Language=="en"&&File.ReadAllBytes(Store.FilePath("profiles.json")).SequenceEqual(profileBytes)&&JsonSerializer.Serialize(controller.Settings.Aliases,Store.Json)==aliasesBefore);
            var german=Descendants(window).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="AppLanguage");german.SelectedIndex=0;Page("colors");
            Check("Switching back to German preserves confirmed hardware values",L.Language=="de"&&Find<Slider>(window,"Helligkeit").Value==63&&controller.Settings.Language=="de");
        }
        catch(Exception ex){checks.Add(new{Name="UI execution",Passed=false,Error=ex.ToString()});passed=false;}
        finally
        {
            release.TrySetResult();Wait(controller.Live.DrainAsync());controller.Overlays.Clear();quick?.Close();
            if(window!=null){typeof(MainWindow).GetField("loaded",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,false);window.ExitApp();}else app.Shutdown();Store.Root=previous;System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(new{Passed=passed,Version=typeof(Program).Assembly.GetName().Version?.ToString(),WritesAtSimulatedBoundary=writes,Checks=checks},Store.Json));return passed?0:1;
    }
    internal static void Wait(Task task)
    {
        var clock=Stopwatch.StartNew();while(!task.IsCompleted){if(clock.Elapsed.TotalSeconds>5)throw new TimeoutException("Live UI operation did not finish");Pump();System.Threading.Thread.Sleep(1);}task.GetAwaiter().GetResult();Pump();
    }
    internal static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static T Find<T>(DependencyObject root,string name) where T:DependencyObject=>Descendants(root).OfType<T>().First(x=>AutomationProperties.GetName(x)==name);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Descendants(child))yield return item;
    }
}
