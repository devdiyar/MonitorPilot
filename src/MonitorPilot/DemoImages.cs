using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MonitorPilot;

// Render the actual WPF components using anonymous sample data. No display APIs,
// native windows, real user data or hardware writes are used by this command.
internal static class DemoImages
{
    public static int Export(string directory)
    {
        string previous=Store.Root;Store.Root=Path.Combine(Path.GetTempPath(),"MonitorPilot-demo",Guid.NewGuid().ToString("N"));
        var app=new System.Windows.Application();Theme.Install(app);MainWindow? window=null;
        try
        {
            Directory.CreateDirectory(directory);
            var controller=new AppController((m,state)=>throw new InvalidOperationException("Screenshot mode cannot change hardware"));
            window=new MainWindow(true,controller:controller);
            var samples=new[]{("Hauptmonitor",0,0,2560,1440,165,72),("Studio links",-2560,0,2560,1440,144,68),("Studio rechts",2560,0,2560,1440,144,70),("Hochformat",5120,-480,1440,2560,60,65),("Referenz",0,-1440,2560,1440,60,74)};
            var monitors=samples.Select((s,i)=>new MonitorDevice
            {
                Key="demo-display-"+i,DeviceName="demo-device-"+i,AdapterKey="demo-gpu",Name=s.Item1,Number=i+1,Active=true,Primary=i==0,
                X=s.Item2,Y=s.Item3,Width=s.Item4,Height=s.Item5,Hz=s.Item6,Rotation=i==3?2u:1u,Connection="DisplayPort",Bits=8,Hdr=false,DdcStatus="DDC/CI verbunden · {0} Regler",DdcControlCount=5,
                Hardware=new(){Brightness=Value((uint)s.Item7),Contrast=Value(70),Red=Value(50),Green=Value(50),Blue=Value(50),Temperature=3,TemperatureMask=36}
            }).ToList();
            foreach(var m in monitors)DisplayService.ModesFor(m.DeviceName,()=>new(){new(){Width=(uint)m.Width,Height=(uint)m.Height,Frequency=(uint)m.Hz},new(){Width=(uint)m.Width,Height=(uint)m.Height,Frequency=60}});
            typeof(AppController).GetProperty(nameof(AppController.Monitors))!.SetValue(controller,monitors);
            controller.Profiles.Add(new(){Workspace=new(){Name="Studio · alle fünf Displays",Topology=new(){Identities=monitors.Select(m=>new PathIdentity{Key=m.Key,Name=m.Name}).ToList()},CapturedAt=new DateTimeOffset(2026,10,11,10,0,0,TimeSpan.Zero)}});
            controller.Report(L.T("●  Bereit · Beispieldaten für die Dokumentation"));
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Page(string page,string mode,string file)
            {
                Theme.SetMode(mode);typeof(MainWindow).GetField("currentPage",flags)!.SetValue(window,page);
                typeof(MainWindow).GetMethod("Render",flags,null,Type.EmptyTypes,null)!.Invoke(window,null);
                Render((FrameworkElement)window.Content,1440,1140,Path.Combine(directory,file));
            }
            Page("monitors","Dark","overview-dark.png");Page("monitors","Light","overview-light.png");Page("colors","Dark","color-controls.png");
            Theme.SetMode("Light");var quick=new TrayQuickWindow(controller,()=>{},()=>{},()=>{});
            Render((FrameworkElement)quick.Content,430,830,Path.Combine(directory,"tray-controls.png"));quick.Close();
            typeof(MainWindow).GetMethod("SetLanguage",flags)!.Invoke(window,new object[]{"en"});
            var englishNames=new[]{"Primary display","Studio left","Studio right","Portrait","Reference"};for(int i=0;i<monitors.Count;i++)monitors[i].Name=englishNames[i];
            controller.Profiles[0].Workspace.Name="Studio · All five displays";controller.Report(L.T("●  Bereit · Beispieldaten für die Dokumentation"));
            Page("monitors","Dark","overview-dark-en.png");Page("monitors","Light","overview-light-en.png");Page("colors","Dark","color-controls-en.png");Page("settings","Light","language-settings-en.png");
            Theme.SetMode("Light");quick=new TrayQuickWindow(controller,()=>{},()=>{},()=>{});Render((FrameworkElement)quick.Content,430,830,Path.Combine(directory,"tray-controls-en.png"));quick.Close();
            return 0;
        }
        finally{window?.ExitApp();app.Shutdown();Store.Root=previous;}
    }
    private static ControlValue Value(uint percent)=>new(){Min=0,Max=100,Current=percent};
    private static void Render(FrameworkElement content,int width,int height,string path)
    {
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(Theme.Background,null,new Rect(0,0,width,height));
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(background);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
