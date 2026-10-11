using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms=System.Windows.Forms;

namespace MonitorPilot;
internal sealed class TrayQuickWindow : Window
{
    private readonly AppController c;
    private readonly Action openColors,openSettings,openMenu;
    private readonly StackPanel body=new();
    private readonly TextBlock status=Theme.Label("",11,Theme.Muted);
    private readonly Dictionary<string,Slider> brightness=new();
    private bool rebuilding;
    private Action? updateHardware;
    public TrayQuickWindow(AppController controller,Action colors,Action settings,Action menu)
    {
        c=controller;openColors=colors;openSettings=settings;openMenu=menu;Title=L.T("MonitorPilot · Schnellsteuerung");Width=430;SizeToContent=SizeToContent.Height;MaxHeight=850;WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=true;Topmost=true;Theme.Decorate(this);
        var root=new DockPanel();var header=new DockPanel{Margin=new Thickness(0,0,0,14)};header.Children.Add(Theme.Stack(Theme.Label("MonitorPilot",19,null,true),Theme.Label(L.T("Schnellsteuerung"),11,Theme.Muted)));DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer=Theme.Stack(new Border{Height=1,Background=Theme.Border,Margin=new Thickness(0,8,0,10)},status);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,7,0)});
        Content=new Border{Background=Theme.Background,BorderBrush=Theme.Border,BorderThickness=new Thickness(1),Padding=new Thickness(20),Child=root};
        c.Changed+=Refresh;c.StatusChanged+=UpdateStatus;c.HardwareDiscovered+=HardwareReady;c.HardwareChanged+=HardwareReady;Closed+=(_,_)=>{c.Changed-=Refresh;c.StatusChanged-=UpdateStatus;c.HardwareDiscovered-=HardwareReady;c.HardwareChanged-=HardwareReady;};PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        Loaded+=(_,_)=>PlaceNearTray();Refresh();
    }
    private void PlaceNearTray()
    {
        var area=Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);var scale=source.CompositionTarget.TransformFromDevice;var topLeft=scale.Transform(new Point(area.Left,area.Top));var bottomRight=scale.Transform(new Point(area.Right,area.Bottom));MaxHeight=Math.Max(180,bottomRight.Y-topLeft.Y-24);Width=Math.Min(430,Math.Max(280,bottomRight.X-topLeft.X-24));UpdateLayout();Left=bottomRight.X-ActualWidth-12;Top=Math.Max(topLeft.Y+12,bottomRight.Y-ActualHeight-12);
    }
    private void UpdateStatus(){status.Text=c.Busy?L.T("Wird angewendet …"):c.Status;status.Foreground=c.Error?Theme.Danger:Theme.Muted;body.IsEnabled=!c.Busy;}
    private void HardwareReady()=>updateHardware?.Invoke();
    private void Refresh()
    {
        rebuilding=true;body.Children.Clear();brightness.Clear();var monitorList=c.Monitors.Where(m=>m.Active).ToList();
        var focus=Theme.Button(monitorList.Count==1?L.T("Arbeitsplatz zurück"):L.T("Nur Fokusmonitor"),()=>RunAndClose(c.Focus),true);var restore=Theme.Button(L.T("Wiederherstellen"),()=>RunAndClose(c.Restore));restore.IsEnabled=c.FocusBackup!=null||System.IO.File.Exists(Store.FilePath("last-workspace.json"));body.Children.Add(Theme.Row(focus,restore));
        body.Children.Add(Theme.Label(L.T("HELLIGKEIT"),10,Theme.Muted,true));
        double Average()=>Math.Round(monitorList.Where(m=>m.Hardware.Brightness!=null).Select(m=>(double)c.ControlPercent(m,MonitorControl.Brightness)!).DefaultIfEmpty(50).Average());
        var master=new Slider{Minimum=0,Maximum=100,Value=Average(),IsSnapToTickEnabled=true,TickFrequency=1,SmallChange=1,LargeChange=5,IsEnabled=monitorList.Any(m=>m.Hardware.Brightness!=null)};AutomationProperties.SetName(master,L.T("Alle Monitore: Helligkeit"));
        var masterLabel=Theme.Label(L.T("Alle gemeinsam"),12,Theme.Accent,true);body.Children.Add(Theme.Stack(masterLabel,master,new Border{Height=1,Background=Theme.Border,Margin=new Thickness(0,10,0,10)}));
        var updates=new List<Action>();
        foreach(var m in monitorList)
        {
            var value=m.Hardware.Brightness;var row=new DockPanel();var number=Theme.Label(value!=null?value.Percent+" %":L.T("Kein DDC/CI"),12,value!=null?Theme.Accent:Theme.Muted);DockPanel.SetDock(number,Dock.Right);row.Children.Add(number);row.Children.Add(Theme.Label(m.Number.ToString("00")+" · "+c.Name(m),12,null,true));body.Children.Add(row);
            var slider=new Slider{Minimum=0,Maximum=100,Value=c.ControlPercent(m,MonitorControl.Brightness)??0,IsEnabled=value!=null,IsSnapToTickEnabled=true,TickFrequency=1,SmallChange=1,LargeChange=5,Margin=new Thickness(0,0,0,10)};AutomationProperties.SetName(slider,c.Name(m)+L.T(": Helligkeit"));slider.ValueChanged+=(_,e)=>{if(!rebuilding){number.Text=$"{e.NewValue:0} %";c.LiveControl(m,MonitorControl.Brightness,(int)e.NewValue);}};brightness[m.Key]=slider;body.Children.Add(slider);
            number.Text=value!=null?$"{slider.Value:0} %":c.Discovering?L.T("Wird erkannt …"):L.T("Kein DDC/CI");
            updates.Add(()=>
            {
                var actual=m.Hardware.Brightness;
                if(actual!=null)slider.Value=c.ControlPercent(m,MonitorControl.Brightness)??actual.Percent;
                slider.IsEnabled=actual!=null;
                number.Text=actual!=null?$"{slider.Value:0} %":c.Discovering?L.T("Wird erkannt …"):L.T("Kein DDC/CI");number.Foreground=actual!=null?Theme.Accent:Theme.Muted;
            });
        }
        master.ValueChanged+=(_,e)=>{if(!rebuilding)c.LiveBrightness((int)e.NewValue);};
        body.Children.Add(Theme.Label(L.T("Alle Regler werden beim Verstellen live übernommen."),10,Theme.Muted));
        if(c.Profiles.Count>0)
        {
            body.Children.Add(new Border{Height=1,Background=Theme.Border,Margin=new Thickness(0,8,0,12)});var profiles=new ComboBox{ItemsSource=c.Profiles,SelectedIndex=0,Margin=new Thickness(0,3,0,8)};AutomationProperties.SetName(profiles,L.T("Arbeitsprofil auswählen"));body.Children.Add(Theme.Label(L.T("Arbeitsprofil"),12,null,true));body.Children.Add(profiles);body.Children.Add(Theme.Button(L.T("Profil anwenden"),()=>{if(profiles.SelectedItem is ProfileEntry p)RunAndClose(async()=>{await c.ApplyWorkspace(p.Workspace);});}));
        }
        body.Children.Add(Theme.Row(Theme.Button(L.T("Bild & Farbe"),()=>{Close();openColors();}),Theme.Button(L.T("Tray-Menü …"),()=>openMenu()),Theme.Button(L.T("Einstellungen"),()=>{Close();openSettings();})));body.Children.Add(Theme.Label(L.T("Rechtsklick auf das Tray-Symbol: weitere Einstellungen."),10,Theme.Muted));rebuilding=false;UpdateStatus();
        updateHardware=()=>{rebuilding=true;try{foreach(var update in updates)update();master.IsEnabled=monitorList.Any(m=>m.Hardware.Brightness!=null);master.Value=Average();masterLabel.Text=L.F($"Alle gemeinsam · {master.Value:0} %");}finally{rebuilding=false;}};
    }
    private void RunAndClose(Func<Task> operation){Close();_=c.Run(operation);}
}
