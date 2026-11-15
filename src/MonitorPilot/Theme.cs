using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MonitorPilot;
internal static class Theme
{
    // Bound brushes remain live in sealed templates. A theme change preserves slider drafts.
    private sealed class Colors : INotifyPropertyChanged
    {
        private Dictionary<string,Color> values=new();
        public Color this[string key]=>values.TryGetValue(key,out var color)?color:System.Windows.Media.Colors.Transparent;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Replace(Dictionary<string,string> palette)
        {
            values=new();foreach(var pair in palette)values[pair.Key]=(Color)ColorConverter.ConvertFromString(pair.Value);
            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Item[]"));
        }
    }
    private static readonly Colors colors=new();
    public static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static SolidColorBrush Live(string name)
    {
        var brush=new SolidColorBrush();BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding("["+name+"]"){Source=colors,Mode=BindingMode.OneWay});return brush;
    }
    public static readonly SolidColorBrush Background=Live("Background"),Sidebar=Live("Sidebar"),Card=Live("Card"),Border=Live("Border"),Text=Live("Text"),Muted=Live("Muted"),Accent=Live("Accent"),Blue=Live("Accent"),AccentText=Live("AccentText"),Soft=Live("Soft"),Hover=Live("Hover"),Input=Live("Input"),Track=Live("Track"),Success=Live("Success"),Danger=Live("Danger"),Map=Live("Map"),Ring=Live("Ring");
    public static string Mode {get;private set;}="System";
    public static bool IsDark {get;private set;}
    public static event Action? Changed;
    public static void Install(System.Windows.Application app)
    {
        SetMode("System");
        foreach(var item in new (string Name,Brush Brush)[]{("Background",Background),("Sidebar",Sidebar),("Card",Card),("Border",Border),("Text",Text),("Muted",Muted),("Accent",Accent),("AccentText",AccentText),("Soft",Soft),("Hover",Hover),("Input",Input),("Track",Track),("Success",Success),("Danger",Danger),("Map",Map),("Ring",Ring)})app.Resources["MP."+item.Name]=item.Brush;
        app.Resources["MP.Font"]=Font;
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/MonitorPilot;component/UI/Controls.xaml",UriKind.Relative)});
        SystemEvents.UserPreferenceChanged+=(_,_)=>app.Dispatcher.BeginInvoke(()=>{if(Mode=="System")SetMode("System");});
    }
    public static void SetMode(string mode)
    {
        Mode=mode is "Light" or "Dark"?mode:"System";IsDark=Mode=="Dark" || Mode=="System"&&SystemIsDark();colors.Replace(Palette(IsDark));
        var app=System.Windows.Application.Current;if(app!=null)foreach(Window window in app.Windows)UpdateTitleBar(window);Changed?.Invoke();
    }
    private static bool SystemIsDark()
    {
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("AppsUseLightTheme") is int value&&value==0;}catch{return false;}
    }
    // shadcn/ui "neutral" tokens: Accent = primary, Soft/Hover = accent, Track = input, Ring = ring.
    internal static Dictionary<string,string> Palette(bool dark)=>dark?new()
    {
        ["Background"]="#0A0A0A",["Sidebar"]="#171717",["Card"]="#171717",["Border"]="#2E2E2E",["Text"]="#FAFAFA",["Muted"]="#A1A1A1",["Accent"]="#E5E5E5",["AccentText"]="#171717",["Soft"]="#262626",["Hover"]="#262626",["Input"]="#1F1F1F",["Track"]="#3A3A3A",["Ring"]="#737373",["Success"]="#4ADE80",["Danger"]="#FF6467",["Map"]="#0F0F0F"
    }:new()
    {
        ["Background"]="#FFFFFF",["Sidebar"]="#FAFAFA",["Card"]="#FFFFFF",["Border"]="#E5E5E5",["Text"]="#0A0A0A",["Muted"]="#737373",["Accent"]="#171717",["AccentText"]="#FAFAFA",["Soft"]="#F5F5F5",["Hover"]="#F5F5F5",["Input"]="#FFFFFF",["Track"]="#E5E5E5",["Ring"]="#A1A1A1",["Success"]="#16A34A",["Danger"]="#DC2626",["Map"]="#FAFAFA"
    };
    public static readonly FontFamily Font=new(new Uri("pack://application:,,,/MonitorPilot;component/"),"./Fonts/#Geist, Segoe UI Variable Text, Segoe UI");
    public static TextBlock Label(string text,double size=14,Brush? color=null,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=color??Text,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6),LineHeight=size*1.45};
    public static Button Button(string text,Action click,bool primary=false,string? style=null)
    {
        var button=new Button{Content=text};style??=primary?"PrimaryButton":null;if(style!=null)button.Style=(Style)System.Windows.Application.Current.FindResource(style);button.Click+=(_,_)=>click();System.Windows.Automation.AutomationProperties.SetName(button,text);return button;
    }
    public static Border Panel(UIElement content,Thickness? margin=null)=>new(){Background=Card,BorderBrush=Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(20),Margin=margin??new Thickness(0,0,0,16),Child=content};
    public static StackPanel Stack(params UIElement[] children){var panel=new StackPanel();foreach(var child in children)panel.Children.Add(child);return panel;}
    // Controls carry no right margin of their own, so stretched ones line up; rows add the 8 px gap and centre vertically.
    public static WrapPanel Row(params UIElement[] children)
    {
        var panel=new WrapPanel();
        foreach(var child in children)
        {
            if(child is FrameworkElement element){if(element.Margin.Right==0&&child!=children[^1])element.Margin=new Thickness(element.Margin.Left,element.Margin.Top,8,element.Margin.Bottom);element.VerticalAlignment=VerticalAlignment.Center;}
            panel.Children.Add(child);
        }
        return panel;
    }
    public static Grid Columns(UIElement left,UIElement right,double rightWidth=300)
        =>new AdaptiveColumns(left,right,rightWidth);
    private sealed class AdaptiveColumns : Grid
    {
        private readonly UIElement right;
        private readonly double rightWidth;
        private bool? stacked;
        public AdaptiveColumns(UIElement left,UIElement right,double rightWidth){this.right=right;this.rightWidth=rightWidth;Children.Add(left);Children.Add(right);}
        protected override Size MeasureOverride(Size constraint)
        {
            bool stack=constraint.Width<700;
            if(stacked!=stack)
            {
                stacked=stack;ColumnDefinitions.Clear();RowDefinitions.Clear();
                ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
                if(stack)
                {
                    RowDefinitions.Add(new(){Height=GridLength.Auto});RowDefinitions.Add(new(){Height=new GridLength(4)});RowDefinitions.Add(new(){Height=GridLength.Auto});
                    SetColumn(right,0);SetRow(right,2);
                }
                else
                {
                    ColumnDefinitions.Add(new(){Width=new GridLength(16)});ColumnDefinitions.Add(new(){Width=rightWidth>0?new GridLength(rightWidth):new GridLength(1,GridUnitType.Star)});
                    SetColumn(right,2);SetRow(right,0);
                }
            }
            return base.MeasureOverride(constraint);
        }
    }
    internal static void FitToWorkArea(Window window,Rect area,double width=1440,double height=980)
    {
        window.Width=Math.Min(width,Math.Max(320,area.Width-24));window.Height=Math.Min(height,Math.Max(240,area.Height-24));
        window.MinWidth=Math.Min(860,window.Width);window.MinHeight=Math.Min(560,window.Height);
    }
    // shadcn Badge: "secondary" for highlighted facts, "outline" otherwise.
    public static Border Badge(string text,bool accent=false)=>new(){Background=accent?Soft:Brushes.Transparent,BorderBrush=accent?Soft:Border,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(8,2,8,3),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left,Child=new TextBlock{Text=text,FontSize=12,Foreground=Text,FontWeight=FontWeights.Medium}};
    public static TextBlock Icon(string glyph,double size=18)=>new(){Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=size,VerticalAlignment=VerticalAlignment.Center,Foreground=Muted};
    public static void Decorate(Window window)
    {
        window.Background=Background;window.Foreground=Text;window.FontFamily=Font;window.FontSize=14;window.UseLayoutRounding=true;window.SnapsToDevicePixels=true;window.SourceInitialized+=(_,_)=>UpdateTitleBar(window);
    }
    private static void UpdateTitleBar(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;int dark=IsDark?1:0;Native.DwmSetWindowAttribute(handle,20,ref dark,4);
    }
}
