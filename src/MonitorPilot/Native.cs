using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MonitorPilot;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Region { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct TargetInfo { public Luid Adapter; public uint Id, ModeIndex; public int Output; public uint Rotation, Scaling; public Rational Refresh; public uint ScanLine; public int Available; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] public struct SignalInfo { public ulong PixelRate; public Rational HSync, VSync; public Region ActiveSize, TotalSize; public uint VideoStandard, ScanLine; }
    [StructLayout(LayoutKind.Explicit, Size=64)] public struct DisplayMode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public SourceMode Source;
        [FieldOffset(16)] public SignalInfo Signal;
    }
    [StructLayout(LayoutKind.Sequential)] public struct InfoHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct TargetName
    {
        public InfoHeader Header; public uint Flags; public int Output; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SourceName { public InfoHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name; }
    [StructLayout(LayoutKind.Sequential)] public struct ColorInfo { public InfoHeader Header; public uint Flags, Encoding, Bits; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Description; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowPlacement { public uint Length, Flags, ShowCmd; public Point MinPosition, MaxPosition; public Rect Normal; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY; public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, Width, Height, DisplayFlags, Frequency, ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect rect, IntPtr data);
    public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayPath[] paths, ref uint modeCount, [Out] DisplayMode[] modes, IntPtr topology);
    [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint pathCount, DisplayPath[]? paths, uint modeCount, DisplayMode[]? modes, uint flags);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int GetTargetName(ref TargetName info);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int GetSourceName(ref SourceName info);
    [DllImport("user32.dll", EntryPoint="DisplayConfigGetDeviceInfo")] public static extern int GetColorInfo(ref ColorInfo info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int index, ref DevMode mode);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string device, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr param);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetMonitorCapabilities(IntPtr h, out uint caps, out uint temperatures);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool SetMonitorBrightness(IntPtr h, uint value);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool SetMonitorContrast(IntPtr h, uint value);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetMonitorRedGreenOrBlueGain(IntPtr h, uint channel, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool SetMonitorRedGreenOrBlueGain(IntPtr h, uint channel, uint value);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetMonitorColorTemperature(IntPtr h, out uint temperature);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool SetMonitorColorTemperature(IntPtr h, uint temperature);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, out uint type, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError=true)] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
