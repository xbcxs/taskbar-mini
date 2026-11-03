using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using TaskbarMini.Core;

namespace TaskbarMini.App;

internal sealed record TaskbarState(nint Handle, PixelRect Bounds, PixelRect Tray, int ButtonsRight, int Dpi);

internal sealed class TaskbarLocator : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private TaskbarState? _latest;
    internal TaskbarState? Latest => Volatile.Read(ref _latest);
    internal TaskbarLocator() => _worker = Task.Run(LocateLoop);

    private async Task LocateLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { Volatile.Write(ref _latest, Read()); }
            catch (Exception) { Volatile.Write(ref _latest, null); }
            try { await Task.Delay(1500, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static TaskbarState? Read()
    {
        nint taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == 0 || !Native.GetWindowRect(taskbar, out var barRect)) return null;
        nint tray = Native.FindWindowEx(taskbar, 0, "TrayNotifyWnd", null);
        if (tray == 0 || !Native.GetWindowRect(tray, out var trayRect)) return null;
        var bounds = Rect(barRect);
        var trayBounds = Rect(trayRect);
        var root = AutomationElement.FromHandle(taskbar);
        var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        int right = bounds.Left;
        foreach (AutomationElement button in buttons)
        {
            var rect = button.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0 || rect.Bottom <= bounds.Top || rect.Top >= bounds.Bottom) continue;
            if (rect.Left >= bounds.Left && rect.Right <= trayBounds.Left + 1)
                right = Math.Max(right, (int)Math.Ceiling(rect.Right));
        }
        int dpi = (int)Native.GetDpiForWindow(taskbar);
        if (dpi <= 0) dpi = 96;
        return new(taskbar, bounds, trayBounds, right, dpi);
    }

    private static PixelRect Rect(Native.Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    public void Dispose()
    {
        _stop.Cancel();
        if (_worker.IsCompleted) _stop.Dispose();
    }
}
