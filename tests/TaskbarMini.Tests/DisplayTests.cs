using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskbarMini.App;
using TaskbarMini.Core;

namespace TaskbarMini.Tests;

public sealed class DisplayTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(192)]
    public void WindowBoundsStayFixedAcrossDigitCountsAndMissingSamples(int dpi) => OnSta(() =>
    {
        using var host = new Host();
        using var widget = new WidgetWindow(() => false, _ => { }, () => { });
        var state = State(host.Handle, dpi);
        widget.Update(state, Sample(1));
        var size = widget.WidgetSize;
        Native.GetWindowRect(widget.Handle, out var initial);
        foreach (double? value in new double?[] { 9, 10, 99, 100, null, 1 })
        {
            widget.Update(state, Sample(value));
            Native.GetWindowRect(widget.Handle, out var current);
            Assert.Equal(size, widget.WidgetSize);
            Assert.Equal(initial.Left, current.Left);
            Assert.Equal(state.Tray.Left, current.Right);
        }
    });

    [Fact]
    public void RepeatedSamplesDoNotRepositionOrRepaintTheWindow() => OnSta(() =>
    {
        using var host = new Host();
        using var widget = new WidgetWindow(() => false, _ => { }, () => { });
        var state = State(host.Handle, 96);
        widget.Update(state, Sample(10));
        using var hook = new PaintHook(widget.Handle);
        SendMessage(widget.Handle, Native.WmPaint, 0, 0);
        int paints = hook.Paints;
        for (int i = 0; i < 10; i++) widget.Update(state, Sample(10.1));
        Assert.Equal(0, hook.Positions);
        Assert.False(GetUpdateRect(widget.Handle, 0, false));
        Assert.Equal(paints, hook.Paints);
    });

    [Theory]
    [InlineData(20)]
    [InlineData(22)]
    public void FollowingFieldsAndUnitsStayFixedWhenDigitCountsChange(int fontSize) => OnSta(() =>
    {
        using var font = new Font("Consolas", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var first = Render(new(1, 1, 12, 56, DateTimeOffset.UtcNow), font);
        using var second = Render(new(100, 100, 12, 56, DateTimeOffset.UtcNow), font);
        using var graphics = Graphics.FromImage(first);
        var columns = WidgetWindow.MeasureColumns(graphics, font, 192);
        int temperatureX = columns.Padding + columns.CpuLabel + columns.Percent + columns.Gap;
        int diskLabelX = columns.Padding + columns.Label + columns.Percent + columns.Gap;
        AssertRegionEqual(first, second, new(temperatureX, 0, columns.Temperature, 38));
        AssertRegionEqual(first, second, new(diskLabelX, 38, 180 - diskLabelX, 38));
        using var temp = Render(new(1, 1, 12, 100, DateTimeOffset.UtcNow), font);
        int unitsWidth = TextRenderer.MeasureText(graphics, "°C", font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        int unitsX = temperatureX + columns.Temperature - unitsWidth;
        AssertRegionEqual(first, temp, new(unitsX, 0, unitsWidth, 38));
    });

    private static Bitmap Render(MetricSnapshot sample, Font font)
    {
        var bitmap = new Bitmap(180, 76);
        using var graphics = Graphics.FromImage(bitmap);
        WidgetWindow.Draw(graphics, bitmap.Size, font, false, sample, 192, false);
        return bitmap;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BufferedFramePreservesBothRowsAndTransparencyKey(bool light) => OnSta(() =>
    {
        using var font = new Font("Consolas", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        using var direct = new Bitmap(180, 76);
        using var buffered = new Bitmap(180, 76);
        var sample = new MetricSnapshot(100, 9, 1, 150, DateTimeOffset.UtcNow);
        using (var graphics = Graphics.FromImage(direct)) WidgetWindow.Draw(graphics, direct.Size, font, light, sample, 192);
        using (var graphics = Graphics.FromImage(buffered)) WidgetWindow.DrawBuffered(graphics, buffered.Size, font, light, sample, 192);
        AssertRegionEqual(direct, buffered, new(0, 0, 180, 76));
        Assert.Equal(Color.FromArgb(1, 1, 1), buffered.GetPixel(0, 0));
    });

    private static void AssertRegionEqual(Bitmap first, Bitmap second, Rectangle area)
    {
        int ink = 0;
        var background = first.GetPixel(0, 0);
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Assert.Equal(first.GetPixel(x, y), second.GetPixel(x, y));
                var pixel = first.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) > 8 || Math.Abs(pixel.G - background.G) > 8 || Math.Abs(pixel.B - background.B) > 8) ink++;
            }
        Assert.True(ink > 20, "Comparison must include visible text.");
    }

    private static MetricSnapshot Sample(double? value) => new(value, value, value, value, DateTimeOffset.UtcNow);
    private static TaskbarState State(nint host, int dpi)
    {
        Native.GetWindowRect(host, out var rect);
        return new(host, new(rect.Left, rect.Top, 1920, 96), new(rect.Left + 1600, rect.Top, 320, 96), rect.Left + 1000, dpi);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Host : NativeWindow, IDisposable
    {
        internal Host() => CreateHandle(new CreateParams { Caption = "TaskbarMini display test", Style = unchecked((int)0x80000000), Width = 1920, Height = 96 });
        public void Dispose() => DestroyHandle();
    }

    private sealed class PaintHook : NativeWindow, IDisposable
    {
        internal int Positions, Paints;
        internal PaintHook(nint window) => AssignHandle(window);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x46) Positions++;
            if (message.Msg == Native.WmPaint) Paints++;
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUpdateRect(nint hwnd, nint rect, bool erase);
}
