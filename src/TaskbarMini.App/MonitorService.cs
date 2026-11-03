using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using TaskbarMini.Core;

namespace TaskbarMini.App;

internal sealed class MonitorService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _metricsTask, _temperatureTask;
    private MetricSnapshot _latest = new(null, null, null, null, DateTimeOffset.UtcNow);
    private TemperatureState _temperature = new(null, "正在读取 CPU 温度", DateTimeOffset.UtcNow);
    internal MetricSnapshot Latest => Volatile.Read(ref _latest);
    private sealed record TemperatureState(double? Value, string Status, DateTimeOffset Timestamp);

    internal MonitorService()
    {
        _metricsTask = Task.Run(CollectMetrics);
        _temperatureTask = Task.Run(CollectTemperature);
    }

    private async Task CollectMetrics()
    {
        using var cpu = new PdhCounter(@"\Processor Information(_Total)\% Processor Utility");
        using var cpuFallback = new PdhCounter(@"\Processor(_Total)\% Processor Time");
        using var disk = new PdhCounter(@"\PhysicalDisk(*)\% Idle Time");
        ulong lastIdle = 0, lastTotal = 0;
        while (!_stop.IsCancellationRequested)
        {
            double? cpuValue = cpu.Single();
            double? fallback = cpuFallback.Single();
            string source = "Processor Utility";
            if (cpuValue is null) { cpuValue = fallback; source = "Processor Time"; }
            double? systemTimes = null;
            if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong total = kernel + user;
                if (lastTotal > 0 && total > lastTotal && idle >= lastIdle)
                    systemTimes = 100d * (1 - (idle - lastIdle) / (double)(total - lastTotal));
                lastIdle = idle; lastTotal = total;
            }
            if (cpuValue is null) { cpuValue = systemTimes; source = "GetSystemTimes"; }
            var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
            double? memoryValue = Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0
                ? 100d * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical : null;
            var temperature = Volatile.Read(ref _temperature);
            bool fresh = DateTimeOffset.UtcNow - temperature.Timestamp < TimeSpan.FromSeconds(5);
            Volatile.Write(ref _latest, new(MetricRules.Percentage(cpuValue), MetricRules.Percentage(memoryValue),
                MetricRules.BusiestDisk(disk.Array()), fresh ? temperature.Value : null, DateTimeOffset.UtcNow, source,
                !fresh && temperature.Value.HasValue ? "CPU 温度采样超时" : temperature.Status));
            try { await Task.Delay(1000, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CollectTemperature()
    {
        while (!_stop.IsCancellationRequested)
        {
            var computer = new Computer { IsCpuEnabled = true };
            try
            {
                computer.Open();
                while (!_stop.IsCancellationRequested)
                {
                    var readings = new List<TemperatureReading>();
                    foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                    {
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                            readings.Add(new(sensor.Name, sensor.Value));
                    }
                    double? value = MetricRules.CpuTemperature(readings);
                    Volatile.Write(ref _temperature, new(value, value.HasValue ? "正常" : "CPU 温度不可读：可能需要管理员权限、PawnIO 驱动或硬件支持", DateTimeOffset.UtcNow));
                    if (value is null) break;
                    await Task.Delay(1000, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Volatile.Write(ref _temperature, new(null, $"CPU 温度不可读：{ex.Message}", DateTimeOffset.UtcNow)); }
            finally { try { computer.Close(); } catch (Exception) { /* A failed sensor must not block exit. */ } }
            try { await Task.Delay(30000, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // Sensor teardown stays on its own worker; do not race hardware I/O from the UI thread.
        try { Task.WaitAll([_metricsTask, _temperatureTask], TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        if (_metricsTask.IsCompleted && _temperatureTask.IsCompleted) _stop.Dispose();
    }
}

internal sealed class PdhCounter : IDisposable
{
    private nint _query, _counter;
    private const uint Format = 0x200 | 0x8000;
    internal PdhCounter(string path)
    {
        if (Native.PdhOpenQuery(null, 0, out _query) != 0) return;
        if (Native.PdhAddEnglishCounter(_query, path, 0, out _counter) != 0) { _counter = 0; return; }
        Native.PdhCollectQueryData(_query);
    }
    internal double? Single()
    {
        if (_counter == 0 || Native.PdhCollectQueryData(_query) != 0) return null;
        return Native.PdhGetFormattedCounterValue(_counter, Format, out _, out var value) == 0 && value.Status <= 1 && double.IsFinite(value.Value)
            ? value.Value : null;
    }
    internal IReadOnlyList<DiskReading> Array()
    {
        if (_counter == 0 || Native.PdhCollectQueryData(_query) != 0) return [];
        uint bytes = 0;
        if (Native.PdhGetFormattedCounterArray(_counter, Format, ref bytes, out _, 0) != 0x800007D2 || bytes == 0 || bytes > 1048576) return [];
        nint buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (Native.PdhGetFormattedCounterArray(_counter, Format, ref bytes, out var count, buffer) != 0) return [];
            var result = new List<DiskReading>();
            int stride = Marshal.SizeOf<Native.CounterItem>();
            for (int i = 0; i < count && (i + 1L) * stride <= bytes; i++)
            {
                var item = Marshal.PtrToStructure<Native.CounterItem>(buffer + i * stride);
                if (item.Value.Status <= 1) result.Add(new(Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value));
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public void Dispose() { if (_query != 0) { Native.PdhCloseQuery(_query); _query = 0; } }
}
