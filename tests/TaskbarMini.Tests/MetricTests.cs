using System.Globalization;
using TaskbarMini.Core;

namespace TaskbarMini.Tests;

public sealed class MetricTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(12.5, 12.5)] [InlineData(-10, 0)] [InlineData(140, 100)]
    public void PercentagesAreBounded(double input, double expected) => Assert.Equal(expected, MetricRules.Percentage(input));

    [Fact]
    public void MissingAndInvalidSamplesStayMissing()
    {
        Assert.Null(MetricRules.Percentage(null));
        Assert.Null(MetricRules.Percentage(double.NaN));
        Assert.Null(MetricRules.Percentage(double.PositiveInfinity));
    }

    [Fact]
    public void DiskDisplaysBusiestPhysicalDiskInsteadOfDilutedTotal()
    {
        DiskReading[] input = [new("_Total", 65), new("0 C:", 100), new("1 D:", 30)];
        Assert.Equal(70, MetricRules.BusiestDisk(input));
    }

    [Fact]
    public void DiskHandlesIdleBoundsAndUnavailableCounters()
    {
        Assert.Equal(0, MetricRules.BusiestDisk([new("0 C:", 400)]));
        Assert.Equal(100, MetricRules.BusiestDisk([new("0 C:", -2)]));
        Assert.Equal(25, MetricRules.BusiestDisk([new("_Total", 75)]));
        Assert.Null(MetricRules.BusiestDisk([new("0 C:", double.NaN)]));
        Assert.Null(MetricRules.BusiestDisk([]));
    }

    [Fact]
    public void PackageTemperatureWinsOverHotterIndividualCore()
    {
        Assert.Equal(52, MetricRules.CpuTemperature([new("CPU Core #1", 70), new("CPU Package", 52)]));
    }

    [Fact]
    public void AmdControlDieAndCoreFallbacksAreSupported()
    {
        Assert.Equal(65, MetricRules.CpuTemperature([new("Core (Tctl/Tdie)", 65), new("CPU Core #1", 60)]));
        Assert.Equal(75, MetricRules.CpuTemperature([new("CPU Core #1", 60), new("CPU Core #2", 75)]));
        Assert.Null(MetricRules.CpuTemperature([new("CPU Package", null), new("Core #1", double.NaN)]));
        Assert.Null(MetricRules.CpuTemperature([new("CPU Package", 500)]));
    }

    [Fact]
    public void LayoutHasExactlyTheRequestedTwoRowsRegardlessOfCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var text = MetricRules.Format(new(12.4, 48.1, 3.2, 52.1, DateTimeOffset.UtcNow));
            Assert.Equal("CPU:12% 52°C", text.First);
            Assert.Equal("M:48% D:3%", text.Second);
            Assert.DoesNotContain('\n', text.First + text.Second);
            var missing = MetricRules.Format(new(null, null, null, null, DateTimeOffset.UtcNow));
            Assert.Equal("CPU:-- --", missing.First);
            Assert.Equal("M:-- D:--", missing.Second);
            var threeDigits = MetricRules.Format(new(100, 100, 100, 100, DateTimeOffset.UtcNow));
            Assert.Equal("CPU:100% 100°C", threeDigits.First);
            Assert.Equal("M:100% D:100%", threeDigits.Second);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void TextNeverDisplaysNaNInfinityOrPercentOverOneHundred()
    {
        var lines = MetricRules.Format(new(101, -1, double.PositiveInfinity, double.NaN, DateTimeOffset.UtcNow));
        Assert.Equal("CPU:100% --", lines.First);
        Assert.Equal("M:0% D:--", lines.Second);
    }
}
