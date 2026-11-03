using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskbarMini.Core;

namespace TaskbarMini.App;

internal sealed class WidgetWindow : NativeWindow, IDisposable
{
    private const uint TransparentColor = 0x010101;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _startupItem;
    private readonly Func<bool> _startupEnabled;
    private readonly Action<bool> _setStartup;
    private Font? _font;
    private int _dpi;
    private int _availableWidth;
    private DisplayLines _displayedLines;
    private bool _lightTheme;
    private MetricSnapshot _snapshot = new(null, null, null, null, DateTimeOffset.UtcNow);
    internal Size WidgetSize { get; private set; }
    internal int PaintCount { get; private set; }
    internal bool IsEmbedded => Handle != 0 && Native.GetParent(Handle) != 0 && ((long)Native.GetWindowLongPtr(Handle, -16) & Native.WsChild) != 0;
    internal bool IsShown => Handle != 0 && Native.IsWindowVisible(Handle);
    internal string LastStatus { get; private set; } = "等待任务栏";
    internal string[] MenuLabels => [_menu.Items[0].Text ?? "", _menu.Items[1].Text ?? ""];

    internal WidgetWindow(Func<bool> startupEnabled, Action<bool> setStartup, Action exit)
    {
        _startupEnabled = startupEnabled; _setStartup = setStartup;
        _startupItem = new ToolStripMenuItem("开机启动");
        _startupItem.Click += (_, _) =>
        {
            try { _setStartup(!_startupEnabled()); }
            catch (Exception ex) { MessageBox.Show($"无法修改开机启动：{ex.Message}", "TaskbarMini", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        _menu.Items.Add(_startupItem);
        _menu.Items.Add("退出", null, (_, _) => exit());
        _menu.Opening += (_, _) => _startupItem.Checked = _startupEnabled();
    }

    internal void Update(TaskbarState? taskbar, MetricSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (taskbar is null || !Native.IsWindow(taskbar.Handle)) { Hide("暂时无法访问任务栏，正在重试"); return; }
        bool created = Handle == 0 || !Native.IsWindow(Handle) || Native.GetParent(Handle) != taskbar.Handle;
        if (created)
        {
            if (Handle != 0) DestroyHandle();
            CreateHandle(new CreateParams
            {
                Caption = "TaskbarMini",
                Parent = taskbar.Handle,
                Style = Native.WsChild | Native.WsClipSiblings,
                ExStyle = Native.WsExLayered | Native.WsExToolWindow | Native.WsExNoActivate,
                Width = 1, Height = 1
            });
            if (Native.GetParent(Handle) != taskbar.Handle || !IsEmbedded) throw new InvalidOperationException("窗口未成功嵌入任务栏");
            if (!Native.SetLayeredWindowAttributes(Handle, TransparentColor, 255, 1)) throw new Win32Exception();
        }
        int available = taskbar.Tray.Left - taskbar.ButtonsRight - Math.Max(1, (int)Math.Round(taskbar.Dpi / 96d));
        var lines = MetricRules.Format(snapshot);
        bool layoutChanged = _dpi != taskbar.Dpi || _availableWidth != available || _font is null;
        if (layoutChanged) ConfigureFont(taskbar.Dpi, available);
        var placement = Placement.BesideTray(taskbar.Bounds, taskbar.Tray, taskbar.ButtonsRight, WidgetSize.Width, WidgetSize.Height, taskbar.Dpi);
        if (placement is not { } p) { Hide("任务栏空间不足，腾出空间后自动恢复"); return; }
        Native.GetWindowRect(Handle, out var current);
        bool shown = ((long)Native.GetWindowLongPtr(Handle, -16) & 0x10000000) != 0;
        bool moved = current.Left != p.Left || current.Top != p.Top || current.Right != p.Right || current.Bottom != p.Bottom;
        if (moved || !shown)
        {
            if (!Native.SetWindowPos(Handle, 0, p.Left - taskbar.Bounds.Left, p.Top - taskbar.Bounds.Top, p.Width, p.Height,
                Native.SwpNoActivate | Native.SwpShowWindow)) throw new Win32Exception();
        }
        bool light = IsLightTheme();
        if (created || layoutChanged || moved || !shown || _displayedLines != lines || _lightTheme != light)
            Native.InvalidateRect(Handle, 0, false);
        _displayedLines = lines;
        _lightTheme = light;
        LastStatus = "已嵌入";
    }

    private void ConfigureFont(int dpi, int available)
    {
        _font?.Dispose();
        _dpi = dpi;
        _availableWidth = available;
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        for (float logicalSize = 11; logicalSize >= 8; logicalSize -= 0.5f)
        {
            _font?.Dispose();
            _font = new Font("Consolas", logicalSize * dpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
            var columns = MeasureColumns(graphics, _font, dpi);
            int height = Math.Max(columns.TextHeight * 2 + columns.Padding * 2, (int)Math.Round(38d * dpi / 96));
            int firstWidth = columns.CpuLabel + columns.Percent + columns.Temperature + columns.Gap;
            int secondWidth = columns.Label * 2 + columns.Percent * 2 + columns.Gap;
            WidgetSize = new(columns.Padding + Math.Max(firstWidth, secondWidth), height);
            if (WidgetSize.Width <= available) break;
        }
    }

    private void Hide(string reason)
    {
        if (Handle != 0 && Native.IsWindow(Handle) && ((long)Native.GetWindowLongPtr(Handle, -16) & 0x10000000) != 0)
            Native.SetWindowPos(Handle, 0, 0, 0, 0, 0, Native.SwpNoActivate | Native.SwpNoZOrder | Native.SwpHideWindow);
        LastStatus = reason;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WmPaint)
        {
            nint dc = Native.BeginPaint(Handle, out var paint);
            try
            {
                if (dc != 0 && _font is not null)
                {
                    using var graphics = Graphics.FromHdc(dc);
                    DrawBuffered(graphics, WidgetSize, _font, IsLightTheme(), _snapshot, _dpi);
                    PaintCount++;
                }
            }
            finally { Native.EndPaint(Handle, ref paint); }
            m.Result = 0; return;
        }
        if (m.Msg == Native.WmEraseBackground) { m.Result = 1; return; }
        if (m.Msg == Native.WmMouseActivate) { m.Result = 3; return; }
        if (m.Msg == Native.WmContextMenu) { _menu.Show(Cursor.Position); m.Result = 0; return; }
        base.WndProc(ref m);
    }

    internal static bool IsLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    internal static void Draw(Graphics graphics, Size size, Font font, bool light, MetricSnapshot snapshot, int dpi, bool transparent = true)
    {
        graphics.Clear(transparent ? Color.FromArgb(1, 1, 1) : light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32));
        var text = MetricRules.Format(snapshot);
        var first = text.First.Split([':', ' ']);
        var second = text.Second.Split([':', ' ']);
        var columns = MeasureColumns(graphics, font, dpi);
        int pad = columns.Padding;
        int row = (size.Height - pad * 2) / 2;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
        var color = light ? Color.FromArgb(24, 24, 24) : Color.FromArgb(245, 245, 245);
        void Field(string value, int x, int y, int width, bool numeric = false) => TextRenderer.DrawText(graphics, value, font,
            new Rectangle(x, y, width, row), color, flags | (numeric ? TextFormatFlags.Right : TextFormatFlags.Left));
        int cpuX = pad + columns.CpuLabel;
        int temperatureX = cpuX + columns.Percent + columns.Gap;
        Field("CPU:", pad, pad, columns.CpuLabel);
        Field(first[1], cpuX, pad, columns.Percent, true);
        Field(first[2], temperatureX, pad, columns.Temperature, true);
        int memoryX = pad + columns.Label;
        int diskLabelX = memoryX + columns.Percent + columns.Gap;
        int diskX = diskLabelX + columns.Label;
        Field("M:", pad, pad + row, columns.Label);
        Field(second[1], memoryX, pad + row, columns.Percent, true);
        Field("D:", diskLabelX, pad + row, columns.Label);
        Field(second[3], diskX, pad + row, columns.Percent, true);
    }

    internal readonly record struct TextColumns(int CpuLabel, int Label, int Percent, int Temperature, int Gap, int Padding, int TextHeight);

    internal static TextColumns MeasureColumns(Graphics graphics, Font font, int dpi)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        Size Measure(string text) => TextRenderer.MeasureText(graphics, text, font, Size.Empty, flags);
        int padding = Math.Max(1, (int)Math.Round(2d * dpi / 96));
        var temperature = Measure("150°C");
        return new(Measure("CPU:").Width, Math.Max(Measure("M:").Width, Measure("D:").Width),
            Measure("100%").Width, temperature.Width, Measure(" ").Width, padding, temperature.Height);
    }

    internal static void DrawBuffered(Graphics target, Size size, Font font, bool light, MetricSnapshot snapshot, int dpi)
    {
        // An opaque bitmap preserves text when copied into the color-keyed layered child window.
        using var frame = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame)) Draw(graphics, size, font, light, snapshot, dpi);
        target.DrawImageUnscaled(frame, Point.Empty);
    }

    internal void SavePreview(string path, bool light, MetricSnapshot? snapshot = null)
    {
        using var bitmap = new Bitmap(WidgetSize.Width, WidgetSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        Draw(graphics, WidgetSize, _font!, light, snapshot ?? _snapshot, _dpi, false);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose()
    {
        if (Handle != 0 && Native.IsWindow(Handle)) DestroyHandle();
        _font?.Dispose(); _menu.Dispose();
    }
}
