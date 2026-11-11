using System.Security.Cryptography;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

internal static class CatalogProbe
{
    internal static int Run(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Use an absolute isolated directory.");
        string root = Path.GetFullPath(directory);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("Catalog probe requires a new empty directory.");
        using var home = PortableHome.Open(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("资源验收");
        // Only a metadata fixture is needed to verify resource placement; no game is launched.
        string version = Path.Combine(launcher.InstanceFolder("资源验收"), "versions", "1.21.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\"}");
        File.WriteAllText(Path.Combine(version, "1.21.1.jar"), "not executable; resource placement fixture");
        var catalog = new PortableCatalog();
        var target = catalog.Target("资源验收", "1.21.1");
        var matches = catalog.Search("resourcepack", "忠实", "modrinth", target, timeout.Token);
        var project = matches.FirstOrDefault(row => row.Slug.Contains("faithful", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Chinese search did not return Faithful.");
        var releases = catalog.Releases(project, target, timeout.Token);
        var release = releases.FirstOrDefault(row => Py.Int(row.File.Get("size")) is > 0 and < 40000000)
            ?? throw new InvalidOperationException("No compatible small resource-pack file was returned.");
        File.WriteAllText(Path.Combine(root, "catalog-selection.json"), JsonSerializer.Serialize(release));
        catalog.Download(project, release, "资源验收", "1.21.1", new Progress<InstallProgress>(), timeout.Token);
        string path = Path.Combine(launcher.InstanceFolder("资源验收"), "resourcepacks", release.Filename);
        string expected = Py.S((release.File.Get("hashes") as PyDict ?? release.File).Get("sha512"));
        string actual = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (actual != expected) throw new IOException($"Downloaded resource SHA-512 mismatch: expected {expected}, actual {actual}.");
        var report = new { ok = true, query = "忠实", project, release = release.Id, file = release.Filename,
            bytes = new FileInfo(path).Length, sha512 = actual, gameLaunched = false };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "catalog-live.json"), json);
        Console.WriteLine(json);
        return 0;
    }
}
