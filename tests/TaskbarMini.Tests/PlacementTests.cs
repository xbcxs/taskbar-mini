using TaskbarMini.Core;

namespace TaskbarMini.Tests;

public sealed class PlacementTests
{
    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)] [InlineData(240)]
    public void WidgetFitsToLeftOfTrayAndVerticallyInsideTaskbar(int dpi)
    {
        int Scale(int n) => (int)Math.Round(n * dpi / 96d);
        var bar = new PixelRect(0, 1000, Scale(1920), Scale(48));
        var tray = new PixelRect(bar.Right - Scale(300), 1000, Scale(300), bar.Height);
        var p = Placement.BesideTray(bar, tray, Scale(1200), Scale(118), Scale(38), dpi);
        Assert.NotNull(p);
        Assert.True(p.Value.Left >= Scale(1200) + Scale(1));
        Assert.Equal(tray.Left, p.Value.Right);
        Assert.InRange(p.Value.Top, bar.Top, bar.Bottom - p.Value.Height);
    }

    [Fact]
    public void NegativeMonitorCoordinatesDoNotBecomeScreenRelativeErrors()
    {
        var p = Placement.BesideTray(new(-1920, -48, 1920, 48), new(-300, -48, 300, 48), -700, 118, 38, 96);
        Assert.NotNull(p);
        Assert.Equal(-418, p.Value.Left);
        Assert.Equal(-43, p.Value.Top);
    }

    [Fact]
    public void FullTaskbarIsHiddenInsteadOfCoveringAnApp()
    {
        Assert.Null(Placement.BesideTray(new(0, 1000, 1920, 48), new(1600, 1000, 320, 48), 1520, 118, 38, 96));
    }

    [Fact]
    public void InvalidAndDetachedGeometryCannotCreateAFloatingFallback()
    {
        Assert.Null(Placement.BesideTray(default, default, 0, 118, 38, 96));
        Assert.Null(Placement.BesideTray(new(0, 1000, 1920, 48), new(1600, 0, 320, 48), 1000, 118, 38, 96));
        Assert.Null(Placement.BesideTray(new(0, 1000, 1920, 48), new(1600, 1000, 320, 48), 1000, 118, 60, 96));
    }
}
