using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarMini.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool verification = args.Length >= 2 && args[0] == "--verify";
        string mutexName = @"Local\TaskbarMini-" + WindowsIdentity.GetCurrent().User!.Value;
        Mutex? instance = new(false, mutexName, out bool created);
        if (!created)
        {
            instance.Dispose();
            if (!verification) MessageBox.Show("TaskbarMini 已在运行。任务栏空间不足时会自动隐藏，腾出空间后恢复。", "TaskbarMini");
            return 3;
        }
        if (!verification && !StartupManager.IsAdministrator && !args.Contains("--no-elevate") && !args.Contains("--elevated"))
        {
            instance.Dispose();
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--elevated" });
                return 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* Consent cancelled: retain the other three metrics. */ }
            catch (Win32Exception) { /* Elevation unavailable: keep a usable ordinary-user display. */ }
            instance = new(false, mutexName, out created);
            if (!created) { instance.Dispose(); return 3; }
        }
        using (instance)
        {
            try
            {
                var startup = new StartupManager();
                if (!verification)
                {
                    try { startup.InitializeFirstRun(); }
                    catch (Exception ex) { MessageBox.Show($"开机启动暂未启用：{ex.Message}。可稍后通过右键菜单重新启用。", "TaskbarMini"); }
                }
                string? output = verification ? Path.GetFullPath(args[1]) : null;
                int seconds = verification && args.Length > 2 && int.TryParse(args[2], out int n) ? Math.Clamp(n, 5, 300) : 18;
                using var context = new MonitorContext(startup, output, seconds);
                Application.Run(context);
                return context.Result;
            }
            catch (Exception ex)
            {
                if (verification)
                {
                    Directory.CreateDirectory(args[1]);
                    File.WriteAllText(Path.Combine(args[1], "error.txt"), ex.ToString());
                }
                else MessageBox.Show($"TaskbarMini 无法启动：{ex.Message}", "TaskbarMini", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

internal sealed class MonitorContext : ApplicationContext
{
    private readonly MonitorService _monitor = new();
    private readonly TaskbarLocator _locator = new();
    private readonly WidgetWindow _window;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly VerificationRecorder? _verification;
    private string? _lastError;
    private bool _disposed;
    internal int Result { get; private set; }

    internal MonitorContext(StartupManager startup, string? output, int seconds)
    {
        _window = new(() => startup.IsEnabled, startup.SetEnabled, ExitThread);
        if (output is not null) _verification = new(output, seconds, startup);
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        try { _window.Update(_locator.Latest, _monitor.Latest); _lastError = null; }
        catch (Exception ex) { _lastError = ex.Message; }
        if (_verification?.Tick(_window, _locator.Latest, _monitor.Latest, _lastError) is { } passed)
        {
            Result = passed ? 0 : 2;
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _timer.Stop(); _timer.Dispose(); _window.Dispose(); _locator.Dispose(); _monitor.Dispose(); }
        base.Dispose(disposing);
    }
}
