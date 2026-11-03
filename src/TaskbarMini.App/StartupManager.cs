using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using TaskbarMini.Core;

namespace TaskbarMini.App;

internal sealed class StartupManager
{
    private const string ValueName = "TaskbarMini";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _exe = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序位置");
    private readonly string _taskName = "TaskbarMini-" + WindowsIdentity.GetCurrent().User!.Value;
    internal static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal string TaskName => _taskName;
    internal bool IsEnabled => QueryTask() || ReadRunValue() is not null;
    internal string ReadSnapshot() => $"{QueryTask()}|{ReadRunValue()}|{File.Exists(MarkerPath)}";
    private static string MarkerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarMini", "initialized");

    private string? ReadRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }
    private bool QueryTask()
    {
        try { return RunSchtasks("/Query", "/TN", _taskName) == 0; }
        catch (Exception) { return false; }
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (IsAdministrator)
            {
                string xml = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(xml, StartupDefinition.TaskXml(_exe, WindowsIdentity.GetCurrent().User!.Value));
                    int code = RunSchtasks("/Create", "/TN", _taskName, "/XML", xml, "/F");
                    if (code != 0) throw new Win32Exception($"注册登录任务失败（{code}）");
                }
                finally { File.Delete(xml); }
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
                run?.DeleteValue(ValueName, false);
            }
            else
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                run.SetValue(ValueName, StartupDefinition.RunCommand(_exe));
            }
        }
        else
        {
            if (QueryTask() && RunSchtasks("/Delete", "/TN", _taskName, "/F") != 0) throw new Win32Exception("移除登录任务失败，请以管理员权限运行后重试");
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, true);
            run?.DeleteValue(ValueName, false);
        }
    }

    internal void InitializeFirstRun()
    {
        string marker = MarkerPath;
        if (File.Exists(marker)) return;
        SetEnabled(true);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "自动启动可通过任务栏小工具右键关闭。移动程序后请重新勾选开机启动。");
    }

    private static int RunSchtasks(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动任务计划工具");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException("任务计划操作超时"); }
        System.Threading.Tasks.Task.WaitAll(stdout, stderr);
        return process.ExitCode;
    }
}
