using HelL.Desktop.Models;
using HelL.Desktop.Services;
using Xunit;

namespace HelL.Desktop.Tests;

public sealed class FrontendRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-desktop-frontend-" + Guid.NewGuid().ToString("N"));
    public FrontendRecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public void LogTailReadsOnlyBoundedEndAndRedactsCredentials()
    {
        string path = Path.Combine(root, "large.log");
        File.WriteAllText(path, "old-entry\n" + new string('x', LogPreview.MaxBytes * 2) + "\nAuthorization: Bearer private-value\nOpen C:\\Users\\Private\\log.txt\nlast-line\n");
        string tail = LogPreview.ReadTail(path, 3);
        Assert.DoesNotContain("old-entry", tail);
        Assert.DoesNotContain("private-value", tail);
        Assert.DoesNotContain("C:\\Users", tail);
        Assert.Contains("last-line", tail);
        Assert.True(tail.Length < LogPreview.MaxBytes);
    }

    [Theory]
    [InlineData("access_token=private-value")]
    [InlineData("设备码 private-value")]
    [InlineData("https://server.test/?key=private-value")]
    public void SensitiveLogValuesAreNotSentInPreview(string line)
        => Assert.DoesNotContain("private-value", LogPreview.Redact(line));

    [Theory]
    [InlineData(@"Opened \\private-server\players\private-value.log")]
    [InlineData("Opened /root/private-value/world.log")]
    [InlineData("Opened /srv/private-value/world.log")]
    [InlineData("Contact private-value@example.test")]
    [InlineData("Connected to 192.0.2.123", "192.0.2.123")]
    [InlineData("Connected to 2001:db8:0:1::1234", "2001:db8:0:1::1234")]
    [InlineData("Connected to [fe80::1234%eth0]:25565", "fe80::1234%eth0")]
    public void PrivateLocationsAndNetworkIdentifiersAreMasked(string line, string sensitive = "private-value")
        => Assert.DoesNotContain(sensitive, LogPreview.Redact(line));

    [Theory]
    [InlineData("Connected to 192.0.2.123.", "192.0.2.123")]
    [InlineData("Connected to 2001:db8::1234.", "2001:db8::1234")]
    [InlineData("Connected to ::ffff:192.0.2.123.", "::ffff:192.0.2.123")]
    public void AddressMaskingPreservesSentencePunctuation(string line, string address)
    {
        string preview = LogPreview.Redact(line);
        Assert.DoesNotContain(address, preview);
        Assert.Equal("Connected to [网络地址].", preview);
    }

    [Theory]
    [InlineData(@"Opened C:\Users\Jane Doe\private\latest.log")]
    [InlineData(@"Opened \\private-server\Jane Doe\private\latest.log")]
    [InlineData("Opened /home/Jane Doe/private/latest.log")]
    [InlineData("Opened /Jane Doe/private/latest.log")]
    public void PathsWithSpacesCannotLeaveANameOrPathSuffix(string line)
    {
        string preview = LogPreview.Redact(line);
        Assert.Equal("Opened [本机路径]", preview);
        Assert.DoesNotContain("Doe", preview);
        Assert.DoesNotContain("private", preview);
    }

    [Theory]
    [InlineData("Open \"C:\\Users\\Jane Doe\\private\\latest.log\": file missing")]
    [InlineData("Open '\\\\private-server\\Jane Doe\\private\\latest.log': file missing")]
    [InlineData("Open \"/home/Jane Doe/private/latest.log\": file missing")]
    public void QuotedPathsKeepTheFollowingDiagnostic(string line)
        => Assert.Equal("Open [本机路径]: file missing", LogPreview.Redact(line));

    [Fact]
    public void LogMaskingKeepsOrdinaryMessagesAndVersionNumbersReadable()
    {
        const string text = "Minecraft 1.21.1 loaded 42 mods at 12:34:56. Memory: 4096 MiB.\nUse /help or Ctrl+C; examples 999.2.3.4 and not:a:network.";
        Assert.Equal(text, LogPreview.Redact(text));
    }

    [Fact]
    public void TerminalHistorySurvivesRestartAndNeverRestoresRunningTasks()
    {
        var finished = new DesktopTask("download resources");
        finished.Report("Authorization: Bearer private-value");
        finished.Finish(DesktopTaskState.Failed, "network request failed");
        var active = new DesktopTask("still running");
        DesktopTaskHistory.Save(root, [active, finished]);
        DesktopTask saved = Assert.Single(DesktopTaskHistory.Read(root));
        Assert.Equal(DesktopTaskState.Failed, saved.State);
        Assert.False(saved.CanCancel);
        Assert.DoesNotContain("private-value", File.ReadAllText(Path.Combine(root, "desktop-task-history.json")));
    }

    [Fact]
    public void CorruptHistoryIsReportedWithoutOverwritingEvidence()
    {
        string path = Path.Combine(root, "desktop-task-history.json");
        File.WriteAllText(path, "{broken");
        Assert.Throws<System.Text.Json.JsonException>(() => DesktopTaskHistory.Read(root));
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[{\"Title\":\"bad record\",\"State\":0,\"Message\":\"running\",\"Lines\":[]}]")]
    public void NewTerminalTaskArchivesCorruptHistoryBeforeReplacingIt(string broken)
    {
        string path = Path.Combine(root, "desktop-task-history.json");
        File.WriteAllText(path, broken);
        Assert.ThrowsAny<Exception>(() => DesktopTaskHistory.Read(root));
        var terminal = new DesktopTask("new completed task");
        terminal.Finish(DesktopTaskState.Completed, "complete");

        DesktopTaskHistory.Save(root, [terminal]);

        string archived = Assert.Single(Directory.GetFiles(root, "desktop-task-history.*.corrupt"));
        Assert.Equal(broken, File.ReadAllText(archived));
        Assert.Equal("new completed task", Assert.Single(DesktopTaskHistory.Read(root)).Title);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(Directory.GetFiles(root, ".desktop-task-history-*.tmp"));
        Assert.Null(DesktopTaskHistory.Save(root, [terminal]));
        Assert.Single(Directory.GetFiles(root, "desktop-task-history.*.corrupt"));
    }

    [Fact]
    public void UnicodeTaskLogsRemainInsideTheReadableHistoryBudget()
    {
        var tasks = Enumerable.Range(0, 100).Select(index =>
        {
            var task = new DesktopTask("任务 " + index);
            for (int line = 0; line < 80; line++) task.Log.Add(new string('测', 512));
            task.Finish(DesktopTaskState.Completed, "complete");
            return task;
        }).ToArray();
        DesktopTaskHistory.Save(root, tasks);
        Assert.True(new FileInfo(Path.Combine(root, "desktop-task-history.json")).Length <= 2 * 1024 * 1024);
        DesktopTask[] restored = DesktopTaskHistory.Read(root);
        Assert.NotEmpty(restored);
        Assert.Equal("任务 0", restored[0].Title);
    }

    [UnixFact]
    public void NewHistoryIsPrivateAndCorruptArchiveRetainsItsOriginalMode()
    {
        if (OperatingSystem.IsWindows()) return;
        string path = Path.Combine(root, "desktop-task-history.json");
        File.WriteAllText(path, "{broken");
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
        var terminal = new DesktopTask("complete");
        terminal.Finish(DesktopTaskState.Completed, "complete");
        string archived = DesktopTaskHistory.Save(root, [terminal])!;
        Assert.Equal(mode, File.GetUnixFileMode(path));
        Assert.Equal(mode, File.GetUnixFileMode(Path.Combine(root, archived)));
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "Unix file modes are verified on Unix runners."; }
    }

    public void Dispose() => Directory.Delete(root, true);
}
