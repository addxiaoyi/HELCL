using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class MirrorLiveCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var checks = new List<object>();
        var transfers = new List<object>();
        var status = await AppServices.Client.CallAsync("download_source_status", new { refresh = true });
        File.WriteAllText(Path.Combine(folder, "probe.json"), status.GetRawText());
        Check(status.GetProperty("samples").GetArrayLength() == 4, "The real proxy-aware probe compares all four providers");
        Check(status.GetProperty("samples").EnumerateArray().Where(row => row.GetProperty("provider").GetString()!.StartsWith("mc_mirror", StringComparison.Ordinal)).All(row => row.GetProperty("available").GetBoolean()), "Both user-provided mirrors pass content validation on this network");

        Config.CONFIG.Set("download_source", "official");
        var dm = new DownloadManager();
        var manifest = await Task.Run(() => Manifest.ListRemoteVersions(dm, true));
        var version = await Task.Run(() => Manifest.GetVersionJson(dm, "1.21.1", true));
        Check(manifest.Count > 87, "Full upstream catalog is preserved instead of the static mirror subset");
        FileMirrors.RegisterVersion(version);
        var client = (PyDict)((PyDict)version.Get("downloads")).Get("client");
        var index = (PyDict)version.Get("assetIndex");
        var logging = (PyDict)((PyDict)((PyDict)version.Get("logging")).Get("client")).Get("file");
        var library = Py.ListOr(version.Get("libraries")).OfType<PyDict>()
            .Select(row => (row.Get("downloads") as PyDict)?.Get("artifact") as PyDict)
            .First(row => row != null && Py.S(row.Get("path")).Contains("gson/2.10.1"))!;
        foreach (string mode in new[] { "mc_mirror", "mc_mirror_west" })
        {
            Config.CONFIG.Set("download_source", mode); Config.CONFIG.Set("download_segments", 1L);
            foreach (var item in new[] { ("logging.xml", logging), ("gson.jar", library) })
                await Download(item.Item1, item.Item2, "mc_mirror_west", mode);
            await Download("index.json", index, "official", mode);
        }
        var assetIndex = (PyDict)PyJson.Loads(File.ReadAllText(Path.Combine(folder, "mc_mirror-index.json")));
        var asset = Py.DictOr(assetIndex.Get("objects")).Select(pair => pair.Value).OfType<PyDict>().First(row => Py.Int(row.Get("size")) > 5000);
        string hash = Py.S(asset.Get("hash"));
        var objectFile = PyDict.Of("url", "https://resources.download.minecraft.net/" + hash.Substring(0, 2) + "/" + hash, "sha1", hash, "size", asset.Get("size"));
        foreach (string mode in new[] { "mc_mirror", "mc_mirror_west" })
        { Config.CONFIG.Set("download_source", mode); await Download("asset.bin", objectFile, "mc_mirror_west", mode); }
        Config.CONFIG.Set("download_source", "mc_mirror_west"); Config.CONFIG.Set("download_segments", 4L);
        await Download("client.jar", client, "mc_mirror_west", "segmented");

        Config.CONFIG.Set("download_source", "auto");
        Source.InvalidateProbe(); var automatic = await Task.Run(() => MirrorHealth.Refresh(true));
        var choices = Source.ExpandDownloadUrls(Py.S(logging.Get("url")));
        Check(FileMirrors.Provider(choices[0]) == Py.S(automatic.Get("preferred")), "Automatic file selection follows this network's measured preference");
        string selected = FileMirrors.Provider(choices[0]);
        await Download("automatic.xml", logging, selected == "mc_mirror" ? "mc_mirror_west" : selected, "auto");

        window.Navigate("settings", instant: true); await Program.WaitForPage(window, "settings");
        var page = Walk(window).OfType<SettingsPage>().Single();
        Walk(page).OfType<Rail>().Single().Select("download"); window.UpdateLayout();
        var picker = (ComboBox)typeof(SettingsPage).GetField("_src", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Check(picker.Items.Count == 5 && picker.Items.Cast<object>().Any(item => item.ToString()!.Contains("美西")), "Download settings exposes both mirror preferences and automatic mode");
        var test = Walk(page).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "检测下载源");
        test.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 120 && !test.IsEnabled; i++) await Task.Delay(100);
        var rows = (SPanel)typeof(SettingsPage).GetField("_sourceResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Check(test.IsEnabled && rows.Children.Count == 4, "Settings detection finishes with visible results and restores the button");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 1000; window.Height = 740; window.UpdateLayout(); await Task.Delay(250);
            Program.SaveSnapshot(window, Path.Combine(folder, "mirrors-" + (dark ? "dark" : "light") + ".png"));
        }
        File.WriteAllText(Path.Combine(folder, "verification.json"), PyJson.Dumps(PyDict.Of("checks", checks, "transfers", transfers, "automatic", automatic, "versions", manifest.Count, "completed_at", DateTime.UtcNow.ToString("O")), 2));
        Console.WriteLine("[MIRROR LIVE] " + checks.Count + " checks passed.");

        async Task Download(string name, PyDict entry, string expected, string group)
        {
            string upstream = Py.S(entry.Get("url"));
            string target = Path.Combine(folder, group + "-" + name); var tracker = new DownloadTracker();
            var downloader = new DownloadManager(tracker: tracker);
            var watch = Stopwatch.StartNew();
            await Task.Run(() => downloader.Download(upstream, target, sha1: Py.S(entry.Get("sha1")), size: Py.Int(entry.Get("size")), force: true, timeout: 30L));
            Check(Utils.Sha1File(target) == Py.S(entry.Get("sha1")), "Downloaded " + group + "/" + name + " matches upstream SHA-1");
            string host = tracker.Snapshot().host;
            Check(FileMirrors.Provider("https://" + host + "/") == expected, group + "/" + name + " reports its actual responder (including redirects or fallback)");
            transfers.Add(PyDict.Of("file", name, "mode", group, "host", host, "url", tracker.url, "bytes", new FileInfo(target).Length, "ms", watch.ElapsedMilliseconds, "sha1", Utils.Sha1File(target)));
        }
        void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); checks.Add(message); Console.WriteLine("[PASS] " + message); }
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
