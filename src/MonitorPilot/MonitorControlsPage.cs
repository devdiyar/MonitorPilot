using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace MonitorPilot;
internal sealed partial class MainWindow
{
    private UIElement ColorsPage()
    {
        var content=new StackPanel();var monitor=c.Monitors.FirstOrDefault(m=>m.Key==selectedKey)??c.Main;
        if(monitor==null)return Theme.Label("Noch keine Monitore erkannt.");
        selectedKey=monitor.Key;
        var choice=new ComboBox{ItemsSource=c.Monitors,SelectedItem=monitor,MinWidth=290,MaxWidth=440,HorizontalAlignment=HorizontalAlignment.Left};
        AutomationProperties.SetName(choice,"Monitor auswählen");
        choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is MonitorDevice m){selectedKey=m.Key;Render();}};
        var info=Theme.Stack(Theme.Label(monitor.Summary+" · "+monitor.Connection,13,Theme.Muted),Theme.Badge(monitor.Active?"●  Verbunden und aktiv":"Deaktiviert",monitor.Active));info.Margin=new Thickness(16,4,0,12);
        content.Children.Add(Theme.Row(choice,info));
        var naming=new TextBox{Text=c.Name(monitor)};AutomationProperties.SetName(naming,"Monitorname");
        var preferred=new CheckBox{Content="Als Fokusmonitor verwenden",IsChecked=c.Settings.PreferredMain==monitor.Key||string.IsNullOrEmpty(c.Settings.PreferredMain)&&monitor.Primary};
        var identity=Theme.Panel(Theme.Stack(Theme.Label("Name & Fokus",16,null,true),naming,preferred,Theme.Button("Monitor speichern",()=>
        {
            c.Settings.Aliases[monitor.Key]=naming.Text.Trim();if(preferred.IsChecked==true)c.Settings.PreferredMain=monitor.Key;else if(c.Settings.PreferredMain==monitor.Key)c.Settings.PreferredMain="";c.SaveSettings();c.Report("Monitorname und Fokusauswahl gespeichert.");Render();
        })));
        if(!monitor.Active){content.Children.Add(identity);content.Children.Add(Theme.Label("Aktiviere diesen Bildschirm zuerst in der Monitorübersicht.",14,Theme.Muted));return content;}
        var sliders=new Dictionary<string,Slider>();
        var numbers=new Dictionary<string,TextBlock>();bool updating=false;
        (string Label,MonitorControl Control,ControlValue? Value)[] Controls()=>new[]{("Helligkeit",MonitorControl.Brightness,monitor.Hardware.Brightness),("Kontrast",MonitorControl.Contrast,monitor.Hardware.Contrast),("Rot",MonitorControl.Red,monitor.Hardware.Red),("Grün",MonitorControl.Green,monitor.Hardware.Green),("Blau",MonitorControl.Blue,monitor.Hardware.Blue)};
        var hardware=Theme.Stack(Theme.Label("Helligkeit & Kontrast",17,null,true),Theme.Label("Wird beim Verstellen live am Monitor übernommen.",12,Theme.Muted));
        foreach(var item in Controls())
        {
            if(item.Item1=="Rot"){hardware.Children.Add(new Border{Height=1,Background=Theme.Border,Margin=new Thickness(0,10,0,15)});hardware.Children.Add(Theme.Label("Farbbalance",17,null,true));}
            hardware.Children.Add(SliderRow(item.Label,c.ControlPercent(monitor,item.Control)??0,0,100,item.Value!=null,out var slider,out var number));sliders[item.Label]=slider;numbers[item.Label]=number;
            slider.ValueChanged+=(_,e)=>{if(!updating&&slider.IsEnabled)c.LiveControl(monitor,item.Control,(int)e.NewValue);};
            if(item.Value==null&&c.Discovering)number.Text="Wird erkannt …";
        }
        var tempChoices=new List<TemperatureOption>{new(0,"Individuell · aktueller Weißpunkt")};
        for(uint t=1;t<=8;t++)if((monitor.Hardware.TemperatureMask&(1u<<((int)t-1)))!=0)tempChoices.Add(new(t,TemperatureName(t)));
        var temperature=new ComboBox{ItemsSource=tempChoices,SelectedIndex=0,IsEnabled=tempChoices.Count>1,Margin=new Thickness(0,4,0,8)};AutomationProperties.SetName(temperature,"Hardware-Weißpunkt");
        temperature.SelectedItem=tempChoices.FirstOrDefault(t=>t.Value==(c.Live.DesiredTemperature(monitor.Key)??monitor.Hardware.Temperature??0))??tempChoices[0];
        temperature.SelectionChanged+=(_,_)=>{if(!updating&&temperature.SelectedItem is TemperatureOption t&&t.Value!=0)c.LiveTemperature(monitor,t.Value);};
        hardware.Children.Add(Theme.Label("Weißpunkt",12,Theme.Muted));hardware.Children.Add(temperature);
        hardware.Children.Add(Theme.Label("Ein Weißpunkt-Preset kann RGB-Werte im Monitor zurücksetzen. Die Farbbalance wird danach automatisch neu ausgelesen.",11,Theme.Muted));
        var left=Theme.Panel(hardware);left.VerticalAlignment=VerticalAlignment.Top;var right=new StackPanel();
        var modes=DisplayService.Modes(monitor.DeviceName);var modeChoice=new ComboBox{ItemsSource=modes.Select(m=>new ModeOption(m)).ToList(),Margin=new Thickness(0,4,0,14)};
        modeChoice.SelectedIndex=modes.FindIndex(m=>m.Width==monitor.Width&&m.Height==monitor.Height&&Math.Abs(m.Frequency-monitor.Hz)<1.1);if(modeChoice.SelectedIndex<0&&modes.Count>0)modeChoice.SelectedIndex=0;
        AutomationProperties.SetName(modeChoice,"Auflösung und Bildwiederholrate");
        var rotation=new ComboBox{ItemsSource=new[]{"Querformat","Hochformat · 90°","Querformat · 180°","Hochformat · 270°"},SelectedIndex=Math.Clamp((int)monitor.Rotation-1,0,3),Margin=new Thickness(0,4,0,14)};AutomationProperties.SetName(rotation,"Ausrichtung");
        right.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Auflösung & Ausrichtung",17,null,true),Theme.Label("Auflösung · Bildwiederholrate",12,Theme.Muted),modeChoice,Theme.Label("Ausrichtung",12,Theme.Muted),rotation,ActionButton("Displaymodus anwenden",async()=>{if(modeChoice.SelectedItem is ModeOption m)await c.ChangeMode(monitor,m.Mode,(uint)rotation.SelectedIndex);}),Theme.Label("Mit 30 Sekunden Rückkehrschutz bei Bildproblemen.",11,Theme.Muted))));
        int dim=c.Overlays.Levels.GetValueOrDefault(monitor.Key);var dimControl=SliderRow("Abdunklung",dim,0,80,true,out var dimSlider);
        dimSlider.ValueChanged+=(_,e)=>{if(!updating)c.SetDimming(monitor,(int)e.NewValue);};
        right.Children.Add(Theme.Panel(Theme.Stack(Theme.Label("Softwaredimmung",17,null,true),Theme.Label("Wird sofort übernommen. Der Bildschirm und seine Hintergrundbeleuchtung bleiben aktiv.",12,Theme.Muted),dimControl,Theme.Button("Dimmung entfernen",()=>c.SetDimming(monitor,0)))));
        right.Children.Add(identity);content.Children.Add(Theme.Columns(left,right,360));
        hardware.Children.Add(Theme.Row(ActionButton("Werte vor erster Änderung",()=>c.OriginalHardware(monitor)),ActionButton("Werte neu erkennen",c.Refresh)));
        var ddcStatus=Theme.Label(monitor.DdcStatus,11,Theme.Muted);hardware.Children.Add(ddcStatus);
        hardware.Children.Add(ActionButton("DDC/CI-Funktionen erneut prüfen",c.RefreshCapabilities));
        uint temperatureMask=monitor.Hardware.TemperatureMask;
        updateHardwareControls=()=>
        {
            updating=true;
            try
            {
            foreach(var item in Controls())
            {
                var slider=sliders[item.Label];var number=numbers[item.Label];
                if(item.Value!=null)slider.Value=c.ControlPercent(monitor,item.Control)??item.Value.Percent;
                slider.IsEnabled=item.Value!=null;
                number.Text=slider.IsEnabled?$"{slider.Value:0} %":c.Discovering?"Wird erkannt …":"Nicht unterstützt";number.Foreground=slider.IsEnabled?Theme.Accent:Theme.Muted;
            }
            if(temperatureMask!=monitor.Hardware.TemperatureMask)
            {
                tempChoices=new(){new(0,"Individuell · aktueller Weißpunkt")};
                for(uint t=1;t<=8;t++)if((monitor.Hardware.TemperatureMask&(1u<<((int)t-1)))!=0)tempChoices.Add(new(t,TemperatureName(t)));
                temperature.ItemsSource=tempChoices;temperature.IsEnabled=tempChoices.Count>1;temperatureMask=monitor.Hardware.TemperatureMask;
            }
            temperature.SelectedItem=tempChoices.FirstOrDefault(t=>t.Value==(c.Live.DesiredTemperature(monitor.Key)??monitor.Hardware.Temperature??0))??tempChoices[0];
            dimSlider.Value=c.Overlays.Levels.GetValueOrDefault(monitor.Key);
            ddcStatus.Text=monitor.DdcStatus;
            }
            finally{updating=false;}
        };
        return content;
    }
    private static UIElement SliderRow(string label,int value,int min,int max,bool enabled,out Slider slider)
        =>SliderRow(label,value,min,max,enabled,out slider,out _);
    private static UIElement SliderRow(string label,int value,int min,int max,bool enabled,out Slider slider,out TextBlock number)
    {
        var panel=new StackPanel{Margin=new Thickness(0,6,0,9)};var header=new DockPanel();number=Theme.Label(enabled?$"{value} %":"Nicht unterstützt",12,enabled?Theme.Accent:Theme.Muted,true);DockPanel.SetDock(number,Dock.Right);header.Children.Add(number);header.Children.Add(Theme.Label(label,13));panel.Children.Add(header);
        var labelNumber=number;
        slider=new Slider{Minimum=min,Maximum=max,Value=value,IsEnabled=enabled,IsSnapToTickEnabled=true,TickFrequency=1,SmallChange=1,LargeChange=5,Margin=new Thickness(0)};AutomationProperties.SetName(slider,label);slider.ValueChanged+=(_,e)=>labelNumber.Text=$"{e.NewValue:0} %";panel.Children.Add(slider);return panel;
    }
}
