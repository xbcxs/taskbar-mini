using System.Xml.Linq;

namespace TaskbarMini.Core;

public static class StartupDefinition
{
    public static string TaskXml(string executable, string userSid)
    {
        ValidateExecutable(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement Element(string name, object value) => new(ns + name, value);
        var document = new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
            Element("Triggers", new XElement(ns + "LogonTrigger", Element("Enabled", "true"), Element("UserId", userSid))),
            Element("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings", Element("MultipleInstancesPolicy", "IgnoreNew"),
                Element("DisallowStartIfOnBatteries", "false"), Element("StopIfGoingOnBatteries", "false"),
                Element("StartWhenAvailable", "true"), Element("ExecutionTimeLimit", "PT0S")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec", Element("Command", executable), Element("Arguments", "--no-elevate"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable)!)))));
        return document.ToString();
    }

    public static string RunCommand(string executable)
    {
        ValidateExecutable(executable);
        return $"\"{executable}\" --no-elevate";
    }

    private static void ValidateExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || executable.Contains('"') ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("启动目标必须是完整的 exe 路径。", nameof(executable));
    }
}
