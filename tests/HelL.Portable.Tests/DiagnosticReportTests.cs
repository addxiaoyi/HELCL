using System.IO.Compression;
using System.Text.Json;
using HelL.Core.Diagnostics;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class DiagnosticReportTests
{
    [Theory]
    [InlineData("Opened /var/private/sensitive-value")]
    [InlineData("设备码：sensitive-value")]
    [InlineData("密码为 sensitive-value")]
    public void DiagnosticMessagesDoNotExposeAbsolutePathsOrLocalizedCredentials(string message)
    {
        Assert.DoesNotContain("sensitive-value", DiagnosticReportBuilder.SanitizeMessage(message));
    }

    [Fact]
    public void PreCancelledCollectionDoesNotStartEnvironmentOrNetworkChecks()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DiagnosticCapture.Collect(DiagnosticSections.All, cancellation: cancellation.Token));
    }
    private static readonly DateTimeOffset Captured = new(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuilderIncludesOnlySelectedSections()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Windows", "X64", 10, 0, 2048, 4,
            [new DiagnosticFinding("missing_java", "warning")],
            [new DiagnosticMirror("official", true, 45)]);

        DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot,
            DiagnosticSections.Application | DiagnosticSections.Library, Captured);
        string json = report.ToJson();

        Assert.Contains("3.2.0", json);
        Assert.Contains("\"launcher.instance_count\": \"4\"", json);
        Assert.DoesNotContain("system.platform", json);
        Assert.DoesNotContain("launch_check", json);
        Assert.DoesNotContain("mirror.", json);
    }

    [Fact]
    public void BuilderRedactsUnsafeRecentIssueDetails()
    {
        var snapshot = new DiagnosticSnapshot("bad C:\\Users\\Alice", "AlicePC", "C:\\private", 10, 0, 20, 1,
            RecentIssues: [new DiagnosticIssue("download_failed", "access_token=abc123 https://private.example/a?token=secret C:\\Users\\Alice\\app.log alice@example.com\nraw stack")]);

        string json = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured).ToJson();

        Assert.Contains("download_failed", json);
        Assert.Contains("Sensitive diagnostic content omitted.", json);
        Assert.DoesNotContain("abc123", json);
        Assert.DoesNotContain("private.example", json);
        Assert.DoesNotContain("Alice", json);
        Assert.DoesNotContain("alice@example.com", json);
        Assert.DoesNotContain("raw stack", json);
    }

    [Fact]
    public void BuilderRejectsInvalidCountsAndMasksUnrecognizedPlatformFields()
    {
        var invalid = new DiagnosticSnapshot("preview private build", "AlicePC", "C:\\Users\\Alice", 10, 0, null, 2);
        string json = DiagnosticReportBuilder.Build(invalid, DiagnosticSections.All, Captured).ToJson();

        Assert.Contains("\"app.version\": \"unknown\"", json);
        Assert.Contains("\"system.platform\": \"Other\"", json);
        Assert.Contains("\"system.architecture\": \"Other\"", json);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticReportBuilder.Build(invalid with { InstanceCount = -1 }, DiagnosticSections.Library, Captured));
    }

    [Fact]
    public async Task PackageContainsOnlyTheReviewedJsonDocument()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Linux", "Arm64", 10, 0, null, 1);
        DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);
        await using var package = new MemoryStream();

        await report.WritePackageAsync(package);
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        ZipArchiveEntry entry = Assert.Single(archive.Entries);
        Assert.Equal("diagnostics.json", entry.FullName);
        using var reader = new StreamReader(entry.Open());
        using JsonDocument document = JsonDocument.Parse(await reader.ReadToEndAsync());
        Assert.Equal(1, document.RootElement.GetProperty("schema_version").GetInt32());
        Assert.DoesNotContain("/home/", document.RootElement.GetRawText());
        Assert.DoesNotContain("access_token", document.RootElement.GetRawText());
    }

    [Fact]
    public void CollectionNeverSerializesTheSuppliedDataDirectory()
    {
        string privatePath = Path.Combine(Path.GetTempPath(), "diagnostic-user-path-" + Guid.NewGuid().ToString("N"));
        DiagnosticSnapshot snapshot = DiagnosticSnapshot.Collect(privatePath, "3.2.0", 1);

        string json = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured).ToJson();

        Assert.DoesNotContain(privatePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnostic-user-path", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndividualFieldsCanBeRemovedBeforeExport()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Linux", "X64", 10, 0, 1024, 1);
        DiagnosticReport all = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);

        DiagnosticReport selected = all.SelectFields(["app.version", "launcher.instance_count"]);
        string json = selected.ToJson();

        Assert.Equal(2, selected.Fields.Count);
        Assert.DoesNotContain("system.platform", json);
        Assert.Throws<ArgumentException>(() => all.SelectFields(["private.path"]));
    }

    [Fact]
    public void RepeatedAndUntrustedFindingsCannotExpandTheReportWithoutBound()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Linux", "X64", 10, 0, 1024, 1,
            Enumerable.Range(0, 5000).Select(index => new DiagnosticFinding("check_" + index, "warning")).ToArray(),
            Enumerable.Repeat(new DiagnosticMirror("official", true, 20), 5000).ToArray(),
            Enumerable.Repeat(new DiagnosticIssue("failure", new string('a', 5000)), 5000).ToArray());

        DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);

        Assert.Equal(40, report.Fields.Count(field => field.Key.StartsWith("launch_check.")));
        Assert.Single(report.Fields, field => field.Key.StartsWith("mirror."));
        Assert.Equal(8, report.Fields.Count(field => field.Key.StartsWith("recent_issue.")));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(report.ToJson()) <= 16 * 1024);
    }

    [Fact]
    public async Task CancelledPackageWritePreservesTheExistingDestination()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Linux", "X64", 10, 0, 1024, 1);
        DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);
        await using var destination = new MemoryStream();
        await destination.WriteAsync("existing file"u8.ToArray());
        byte[] original = destination.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => report.WritePackageAsync(destination, cancellation.Token));

        Assert.Equal(original, destination.ToArray());
    }

    [Theory]
    [InlineData("{\"access_token\":\"json-secret-value\"}")]
    [InlineData("--accessToken cli-secret-value")]
    [InlineData("Authorization: Bearer bearer-secret-value")]
    public void SensitiveKeywordOmitsTheWholeMessage(string message)
    {
        Assert.Equal("Sensitive diagnostic content omitted.", DiagnosticReportBuilder.SanitizeMessage(message));
    }

    [Fact]
    public void ExistingPreflightWarnSeverityIsPreserved()
    {
        var snapshot = new DiagnosticSnapshot("3.2.0", "Windows", "X64", 10, 0, 1024, 1,
            [new DiagnosticFinding("disk_warn", "warn")]);

        DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.LaunchChecks, Captured);

        DiagnosticField warning = Assert.Single(report.Fields);
        Assert.Equal("launch_check.disk_warn", warning.Key);
        Assert.Equal("warning", warning.Value);
    }

    [Fact]
    public async Task FileSaveReplacesAnExistingFileWithACompletePackage()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hell-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "report.zip");
        try
        {
            await File.WriteAllTextAsync(destination, "original");
            var snapshot = new DiagnosticSnapshot("3.2.0", "Windows", "X64", 10, 0, 1024, 1);
            DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);

            await report.SavePackageAsync(destination);

            using var archive = ZipFile.OpenRead(destination);
            Assert.Equal("diagnostics.json", Assert.Single(archive.Entries).FullName);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task FailedFilePromotionLeavesTheOldFileAndRemovesTemporaryPackage()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(Path.GetTempPath(), "hell-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "report.zip");
        try
        {
            await File.WriteAllTextAsync(destination, "original");
            var snapshot = new DiagnosticSnapshot("3.2.0", "Windows", "X64", 10, 0, 1024, 1);
            DiagnosticReport report = DiagnosticReportBuilder.Build(snapshot, DiagnosticSections.All, Captured);
            using (var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
                await Assert.ThrowsAsync<IOException>(() => report.SavePackageAsync(destination));

            Assert.Equal("original", await File.ReadAllTextAsync(destination));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
