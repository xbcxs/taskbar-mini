namespace TaskbarMini.Core;

public sealed record MetricSnapshot(double? Cpu, double? Memory, double? Disk, double? Temperature,
    DateTimeOffset Timestamp, string CpuSource = "", string TemperatureStatus = "");

public readonly record struct TemperatureReading(string Name, double? Value);
public readonly record struct DiskReading(string Name, double? IdlePercent);
public readonly record struct DisplayLines(string First, string Second);

public static class MetricRules
{
    public static double? Percentage(double? value) => value is { } n && double.IsFinite(n) ? Math.Clamp(n, 0, 100) : null;

    public static double? BusiestDisk(IEnumerable<DiskReading> readings)
    {
        var all = readings.Where(r => Percentage(r.IdlePercent).HasValue).ToArray();
        var physical = all.Where(r => !r.Name.Equals("_Total", StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = physical.Length > 0 ? physical : all;
        return selected.Length == 0 ? null : selected.Max(r => 100 - Percentage(r.IdlePercent)!.Value);
    }

    public static double? CpuTemperature(IEnumerable<TemperatureReading> readings)
    {
        var valid = readings.Where(r => r.Value is > 0 and <= 150 && double.IsFinite(r.Value.Value)).ToArray();
        static int Priority(string name) => name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 0
            : name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        if (valid.Length == 0) return null;
        int priority = valid.Min(r => Priority(r.Name));
        return valid.Where(r => Priority(r.Name) == priority).Max(r => r.Value);
    }

    public static DisplayLines Format(MetricSnapshot snapshot)
    {
        static string Number(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        static string Percent(double? value) => Percentage(value) is { } n ? Number(n) + "%" : "--";
        string temperature = CpuTemperature([new("CPU Package", snapshot.Temperature)]) is { } t ? Number(t) + "°C" : "--";
        return new($"CPU:{Percent(snapshot.Cpu)} {temperature}", $"M:{Percent(snapshot.Memory)} D:{Percent(snapshot.Disk)}");
    }
}
