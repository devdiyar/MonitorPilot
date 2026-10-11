using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MonitorPilot;
internal sealed class OverlayService
{
    private readonly Dictionary<string,Window> dimWindows=new();
    private readonly List<Window> testWindows=new();
    public Dictionary<string,int> Levels {get;private set;}=new(StringComparer.OrdinalIgnoreCase);
    public void Set(MonitorDevice monitor,int percent)
    {
        percent=Math.Clamp(percent,0,80); Levels[monitor.Key]=percent;
        if(percent==0 || !monitor.Active){if(dimWindows.Remove(monitor.Key,out var old))old.Close();return;}
        if(dimWindows.TryGetValue(monitor.Key,out var existing)){existing.Opacity=percent/100.0;return;}
        var window=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Black,Opacity=percent/100.0,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Focusable=false};
        window.SourceInitialized+=(_,_)=>
        {
            var h=new WindowInteropHelper(window).Handle;
            Native.SetWindowLongPtr(h,-20,new IntPtr(Native.GetWindowLongPtr(h,-20).ToInt64()|0x20|0x80|0x08000000));
        };
        Place(window,monitor); dimWindows[monitor.Key]=window;
    }
    public void Restore(Dictionary<string,int> levels,List<MonitorDevice> monitors)
    {
        Clear(); Levels=new(levels,StringComparer.OrdinalIgnoreCase);
        foreach(var m in monitors.Where(m=>m.Active)) if(levels.TryGetValue(m.Key,out int value) && value>0) Set(m,value);
    }
    public void Reposition(List<MonitorDevice> monitors) => Restore(new(Levels),monitors);
    public void Clear() {foreach(var w in dimWindows.Values)w.Close();dimWindows.Clear();Levels.Clear();}
    public void ClosePatterns() {foreach(var w in testWindows.ToArray())w.Close();testWindows.Clear();}
    private static void Place(Window window,MonitorDevice m)
    {
        window.Left=m.X;window.Top=m.Y;window.Width=Math.Max(100,m.Width);window.Height=Math.Max(100,m.Height);
        window.Show();Native.SetWindowPos(new WindowInteropHelper(window).Handle,new IntPtr(-1),m.X,m.Y,m.Width,m.Height,0x10|0x40);
    }
    public void Identify(List<MonitorDevice> monitors)
    {
        foreach(var m in monitors.Where(m=>m.Active))
        {
            var label=Theme.Stack(Theme.Label(m.Number.ToString("00"),60,Theme.Accent,true),Theme.Label(m.Name,20),Theme.Label(m.Primary?L.T("Hauptmonitor"):m.Connection,14,Theme.Muted));
            var w=new Window {Width=330,Height=200,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,Background=Theme.Background,Content=Theme.Panel(label),Left=m.X+60,Top=m.Y+60};
            w.Show();Native.SetWindowPos(new WindowInteropHelper(w).Handle,new IntPtr(-1),m.X+60,m.Y+60,340,210,0x10|0x40);
            var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};timer.Tick+=(_,_)=>{timer.Stop();w.Close();};timer.Start();
        }
    }
    public void Patterns(List<MonitorDevice> monitors,string pattern)
    {
        ClosePatterns();
        foreach(var m in monitors.Where(m=>m.Active))
        {
            var root=new Grid {Background=Brushes.Black};
            if(pattern=="Graustufen")
            {
                var columns=new Grid();
                for(int i=0;i<17;i++){columns.ColumnDefinitions.Add(new());byte v=(byte)(i*255/16);var block=new Border {Background=new SolidColorBrush(Color.FromRgb(v,v,v))};Grid.SetColumn(block,i);columns.Children.Add(block);}
                root.Children.Add(columns);
            }
            else if(pattern=="Farbfelder")
            {
                var grid=new System.Windows.Controls.Primitives.UniformGrid {Rows=4,Columns=6};
                foreach(string color in new[]{"#FFFFFF","#C8C8C8","#909090","#606060","#303030","#000000","#FF0000","#00FF00","#0000FF","#FFFF00","#00FFFF","#FF00FF","#C86450","#E8B894","#7298BB","#69805A","#9690BD","#C29439","#491F22","#204C36","#223A72","#808040","#408080","#804080"}) grid.Children.Add(new Border {Background=Theme.Brush(color)});
                root.Children.Add(grid);
            }
            else
            {
                string color=pattern switch {"Weiß"=>"#FFFFFF","Rot"=>"#FF0000","Grün"=>"#00FF00","Blau"=>"#0000FF",_=>"#000000"};root.Background=Theme.Brush(color);
            }
            var close=Theme.Button(L.F($"{m.Number:00} · {m.Name}    |    {L.T(pattern)}    |    Alle Testbilder schließen (Esc)"),ClosePatterns);
            close.HorizontalAlignment=HorizontalAlignment.Center;close.VerticalAlignment=VerticalAlignment.Bottom;close.Margin=new Thickness(30);root.Children.Add(close);
            var w=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Content=root};
            w.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)ClosePatterns();};testWindows.Add(w);Place(w,m);
        }
        var timeout=new DispatcherTimer {Interval=TimeSpan.FromMinutes(5)};
        timeout.Tick+=(_,_)=>{timeout.Stop();ClosePatterns();};timeout.Start();
    }
}
