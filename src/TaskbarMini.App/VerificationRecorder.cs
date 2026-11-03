using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Forms;
using TaskbarMini.Core;

namespace TaskbarMini.App;

internal sealed class VerificationRecorder
{
    private readonly string _output, _startupBefore;
    private readonly int _seconds;
    private readonly StartupManager _startup;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly List<MetricSnapshot> _samples = [];
    private DateTimeOffset _lastTimestamp;
    private bool _captured;
    private bool _textVisible;
    private double? _cpuBaseline, _baselineSeconds;

    internal VerificationRecorder(string output, int seconds, StartupManager startup)
    {
        _output = output; _seconds = seconds; _startup = startup;
        Directory.CreateDirectory(output);
        _startupBefore = startup.ReadSnapshot();
    }

    internal bool? Tick(WidgetWindow window, TaskbarState? state, MetricSnapshot sample, string? error)
    {
        if (_lastTimestamp != sample.Timestamp)
        {
            _samples.Add(sample); _lastTimestamp = sample.Timestamp;
        }
        if (_elapsed.Elapsed.TotalSeconds >= 5 && _cpuBaseline is null)
        {
            using var process = Process.GetCurrentProcess();
            _cpuBaseline = process.TotalProcessorTime.TotalSeconds;
            _baselineSeconds = _elapsed.Elapsed.TotalSeconds;
        }
        if (!_captured && _elapsed.Elapsed.TotalSeconds >= 6 && window.IsEmbedded && window.IsShown && state is not null)
        {
            _textVisible = CaptureTaskbar(state.Bounds, window.Handle);
            window.SavePreview(Path.Combine(_output, "preview-dark.png"), false);
            window.SavePreview(Path.Combine(_output, "preview-light.png"), true);
            window.SavePreview(Path.Combine(_output, "preview-three-digits.png"), false, new(100, 100, 100, 100, DateTimeOffset.UtcNow));
            _captured = true;
        }
        if (_elapsed.Elapsed.TotalSeconds < _seconds) return null;
        bool sameStartup = _startupBefore == _startup.ReadSnapshot();
        bool parentMatches = state is not null && window.Handle != 0 && Native.GetParent(window.Handle) == state.Handle;
        Native.GetWindowRect(window.Handle, out var widgetRect);
        bool clearOfIcons = state is not null && widgetRect.Left >= state.ButtonsRight && widgetRect.Right <= state.Tray.Left;
        bool touchesTray = state is not null && widgetRect.Right == state.Tray.Left;
        var recovery = ProbeRecovery();
        bool metricsReady = _samples.Exists(s => s.Cpu.HasValue && s.Memory.HasValue && s.Disk.HasValue);
        bool passed = parentMatches && window.IsEmbedded && window.IsShown && clearOfIcons && touchesTray && _captured && _textVisible && sameStartup && metricsReady && recovery.Initial && recovery.Detached && recovery.Reattached && recovery.HiddenWhenFull && recovery.Restored && recovery.DpiScaled && error is null;
        using var current = Process.GetCurrentProcess();
        var report = new
        {
            passed,
            actualTaskbarHandle = state?.Handle.ToInt64(),
            widgetHandle = window.Handle.ToInt64(),
            actualParentHandle = Native.GetParent(window.Handle).ToInt64(),
            parentMatches, wsChild = window.IsEmbedded, visible = window.IsShown, clearOfIcons, touchesTray, textVisible = _textVisible,
            taskbar = state?.Bounds, tray = state?.Tray, buttonsRight = state?.ButtonsRight, dpi = state?.Dpi,
            widget = new PixelRect(widgetRect.Left, widgetRect.Top, widgetRect.Right - widgetRect.Left, widgetRect.Bottom - widgetRect.Top),
            status = window.LastStatus, paintCount = window.PaintCount, error,
            menu = window.MenuLabels,
            recovery,
            startupUnchanged = sameStartup,
            startupTaskXml = StartupDefinition.TaskXml(Environment.ProcessPath!, WindowsIdentity.GetCurrent().User!.Value),
            isAdministrator = StartupManager.IsAdministrator,
            workingSetMb = Math.Round(current.WorkingSet64 / 1048576d, 1),
            steadyStateCpuPercent = _cpuBaseline is { } baseline && _baselineSeconds is { } seconds
                ? Math.Round(100 * (current.TotalProcessorTime.TotalSeconds - baseline) / (_elapsed.Elapsed.TotalSeconds - seconds) / Environment.ProcessorCount, 3) : (double?)null,
            samples = _samples
        };
        File.WriteAllText(Path.Combine(_output, "verification.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }

    private bool CaptureTaskbar(PixelRect bounds, nint widget)
    {
        using var image = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, image.Size);
        image.Save(Path.Combine(_output, "taskbar-actual.png"), System.Drawing.Imaging.ImageFormat.Png);
        Native.GetWindowRect(widget, out var rect);
        var area = new Rectangle(rect.Left - bounds.Left, rect.Top - bounds.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var crop = image.Clone(area, image.PixelFormat);
        crop.Save(Path.Combine(_output, "widget-actual.png"), System.Drawing.Imaging.ImageFormat.Png);
        bool light = WidgetWindow.IsLightTheme();
        int first = 0, second = 0;
        for (int y = 0; y < crop.Height; y++)
            for (int x = 0; x < crop.Width; x++)
            {
                var pixel = crop.GetPixel(x, y);
                bool foreground = light ? pixel.R < 80 && pixel.G < 80 && pixel.B < 80 : pixel.R > 180 && pixel.G > 180 && pixel.B > 180;
                if (foreground) { if (y < crop.Height / 2) first++; else second++; }
            }
        return first >= 20 && second >= 20;
    }

    private sealed record RecoveryResult(bool Initial, bool Detached, bool Reattached, bool HiddenWhenFull, bool Restored, bool DpiScaled, Size InitialSize, Size ScaledSize);
    private static RecoveryResult ProbeRecovery()
    {
        using var first = new ProbeParent();
        using var second = new ProbeParent();
        using var widget = new WidgetWindow(() => false, _ => { }, () => { });
        var sample = new MetricSnapshot(12, 48, 3, 52, DateTimeOffset.UtcNow);
        static TaskbarState State(nint handle) => new(handle, new(0, 0, 1920, 48), new(1600, 0, 320, 48), 1000, 96);
        widget.Update(State(first.Handle), sample);
        bool initial = Native.GetParent(widget.Handle) == first.Handle && widget.IsEmbedded;
        var initialSize = widget.WidgetSize;
        widget.Update(new(first.Handle, new(0, 0, 1920, 96), new(1600, 0, 320, 96), 1000, 192), sample);
        var scaledSize = widget.WidgetSize;
        bool dpiScaled = widget.WidgetSize.Width >= initialSize.Width * 1.8 && widget.WidgetSize.Height >= initialSize.Height * 1.8;
        widget.Update(State(first.Handle) with { ButtonsRight = 1599 }, sample);
        bool hidden = ((long)Native.GetWindowLongPtr(widget.Handle, -16) & 0x10000000) == 0;
        widget.Update(State(first.Handle), sample);
        bool restored = ((long)Native.GetWindowLongPtr(widget.Handle, -16) & 0x10000000) != 0;
        nint oldChild = widget.Handle;
        first.Dispose();
        bool detached = !Native.IsWindow(oldChild);
        widget.Update(State(second.Handle), sample);
        bool reattached = Native.GetParent(widget.Handle) == second.Handle && widget.IsEmbedded;
        return new(initial, detached, reattached, hidden, restored, dpiScaled, initialSize, scaledSize);
    }

    private sealed class ProbeParent : NativeWindow, IDisposable
    {
        internal ProbeParent() => CreateHandle(new CreateParams { Caption = "TaskbarMini verification host", Width = 1920, Height = 48 });
        public void Dispose() { if (Handle != 0) DestroyHandle(); }
    }
}
