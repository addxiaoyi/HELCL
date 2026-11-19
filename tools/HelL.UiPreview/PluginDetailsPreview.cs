using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Pages;
using HelL.Services;

internal static class PluginDetailsPreview
{
    private static int _checks;
    internal static async Task Run(MainWindow window, string folder, string market)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(market, "catalog.json")));
        var listings = JsonSerializer.Deserialize<List<PluginListing>>(doc.RootElement.GetProperty("plugins").GetRawText(), PluginManifest.Json)!;
        foreach (var listing in listings)
        {
            string package = Path.Combine(market, listing.Manifest.Id + "-" + listing.Manifest.Version + ".hellplugin");
            using var sha = SHA256.Create(); string checksum = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(package))).Replace("-", "").ToLowerInvariant();
            Check(checksum == listing.Sha256, "preview package SHA-256 matches " + listing.Manifest.Id);
            new PluginStore(Path.Combine(Utils.ROOT, "cache", "plugin-previews", checksum)).InstallPackage(package);
            LauncherPlugins.Store.InstallPackage(Path.Combine(market, listing.Manifest.Id + "-1.0.0.hellplugin"));
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark); window.Width = dark ? 900 : 1180;
                var panel = new PluginDetailsPanel(listing, () => Task.CompletedTask); Dlg.Panel("插件详情", panel, 740);
                await Until(() => CustomizationPreview.Walk(panel).OfType<LocalPicture>().Count(p => p.Ready) == listing.Manifest.Screenshots.Count, "detail screenshot decode " + listing.Manifest.Id);
                Check(CustomizationPreview.Walk(panel).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "更新插件" && b.IsEnabled), "detail exposes update from installed 1.0.0");
                Check(CustomizationPreview.Walk(panel).OfType<TextBlock>().Any(t => t.Text == listing.Manifest.Releases[0].Notes), "release notes render " + listing.Manifest.Id);
                window.UpdateLayout(); await Task.Delay(120);
                Program.SaveSnapshot(panel, Path.Combine(folder, listing.Manifest.Id + "-" + (dark ? "dark" : "light") + ".png"));
                Program.SaveSnapshot(window, Path.Combine(folder, listing.Manifest.Id + "-" + (dark ? "dark" : "light") + "-window.png"));
                CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        var sample = listings[0]; string correctHash = sample.Sha256; sample.Sha256 = "invalid";
        var failed = new PluginDetailsPanel(sample, () => Task.CompletedTask); Dlg.Panel("插件详情", failed, 740);
        await Until(() => CustomizationPreview.Walk(failed).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "重试截图"), "invalid preview displays recoverable error");
        sample.Sha256 = correctHash; CustomizationPreview.Named(failed, "重试截图").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => CustomizationPreview.Walk(failed).OfType<LocalPicture>().Any(p => p.Ready), "retry recovers real preview images");
        CustomizationPreview.Named(failed, "放大插件截图").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => CustomizationPreview.Walk(Dlg.Host!).OfType<LocalPicture>().Any(p => p.Ready && p.Height == 420), "screenshot opens at full display size");
        Program.SaveSnapshot(window, Path.Combine(folder, "enlarged-screenshot.png"));
        CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string damaged = Path.Combine(Utils.ROOT, "cache", "plugin-previews", sample.Sha256, sample.Manifest.Id, sample.Manifest.Screenshots[0]);
        File.WriteAllText(damaged, "broken image cache fixture");
        var damagedPanel = new PluginDetailsPanel(sample, () => Task.CompletedTask); Dlg.Panel("插件详情", damagedPanel, 740);
        await Until(() => CustomizationPreview.Walk(damagedPanel).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "重试截图"), "damaged cache displays retry");
        CustomizationPreview.Named(damagedPanel, "重试截图").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => CustomizationPreview.Walk(damagedPanel).OfType<LocalPicture>().Count(p => p.Ready) == sample.Manifest.Screenshots.Count, "retry downloads verified package and repairs damaged cache");
        Console.WriteLine("[PLUGIN DETAILS] " + _checks + " checks passed");
    }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        var end = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < end) await Task.Delay(100); Check(ready(), message);
    }
}
