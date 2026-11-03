using System;
using System.Runtime.InteropServices;

namespace TaskbarMini.App;

internal static class Native
{
    internal const int WsChild = 0x40000000, WsClipSiblings = 0x04000000;
    internal const int WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    internal const uint SwpNoActivate = 0x10, SwpNoZOrder = 0x4, SwpShowWindow = 0x40, SwpHideWindow = 0x80;
    internal const int WmPaint = 0xF, WmEraseBackground = 0x14, WmContextMenu = 0x7B, WmMouseActivate = 0x21;
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct PaintStruct
    {
        internal nint Hdc;
        internal int Erase;
        internal Rect Paint;
        internal int Restore, IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus
    {
        internal uint Length, Load;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct CounterValue { internal uint Status; internal double Value; }
    [StructLayout(LayoutKind.Sequential)] internal struct CounterItem { internal nint Name; internal CounterValue Value; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EndPaint(nint hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhOpenQuery(string? source, nint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhAddEnglishCounter(nint query, string path, nint data, out nint counter);
    [DllImport("pdh.dll")] internal static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] internal static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] internal static extern uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bytes, out uint count, nint buffer);
    [DllImport("pdh.dll")] internal static extern uint PdhCloseQuery(nint query);
}
