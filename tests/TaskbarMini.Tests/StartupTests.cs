using System.Xml.Linq;
using TaskbarMini.Core;

namespace TaskbarMini.Tests;

public sealed class StartupTests
{
    [Fact]
    public void ScheduledStartupIsBoundToTheCurrentUserAndDoesNotStopOnBatteryOrAfterThreeDays()
    {
        const string sid = "S-1-5-21-123-456-789-1001";
        const string exe = @"D:\工具 & Apps\TaskbarMini\TaskbarMini.exe";
        var root = XDocument.Parse(StartupDefinition.TaskXml(exe, sid));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal(sid, root.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal(sid, root.Descendants(ns + "Principal").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", root.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("HighestAvailable", root.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("false", root.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", root.Descendants(ns + "StopIfGoingOnBatteries").Single().Value);
        Assert.Equal("PT0S", root.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal(exe, root.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--no-elevate", root.Descendants(ns + "Arguments").Single().Value);
    }

    [Fact]
    public void OrdinaryStartupQuotesTheExecutableAndAvoidsLoginUacPrompts()
    {
        Assert.Equal("\"D:\\My Tools\\TaskbarMini.exe\" --no-elevate", StartupDefinition.RunCommand(@"D:\My Tools\TaskbarMini.exe"));
    }

    [Theory]
    [InlineData("")] [InlineData("relative.exe")] [InlineData("D:\\工具\\bad\".exe")]
    public void InvalidStartupTargetsAreRejected(string executable)
    {
        Assert.Throws<ArgumentException>(() => StartupDefinition.RunCommand(executable));
    }
}
