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

    public void Dispose() => Directory.Delete(root, true);
}
