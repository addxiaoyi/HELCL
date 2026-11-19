using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HelL.Core.Launcher.Plugins;

internal static class PluginMarketCheck
{
    internal static async Task Run(string folder, string? baseline = null)
    {
        Directory.CreateDirectory(folder);
        var listings = await PluginCatalog.Fetch(PluginCatalog.DefaultUrl);
        if (listings.Count != 4) throw new InvalidDataException("Expected four public plugin packages.");
        if (listings.Any(listing => listing.Manifest.Version != "1.1.0")) throw new InvalidDataException("The public catalog is stale; expected 1.1.0.");
        var previousUrl = await PluginCatalog.Fetch("https://raw.githubusercontent.com/addxiaoyi/HELL/main/market/catalog.json");
        if (!previousUrl.Select(listing => listing.Sha256).SequenceEqual(listings.Select(listing => listing.Sha256))) throw new InvalidDataException("Saved official source did not migrate to the current catalog.");
        var store = new PluginStore(Path.Combine(folder, "installed-" + Guid.NewGuid().ToString("N")));
        foreach (var listing in listings)
        {
            bool disabled = listing.Manifest.Id == "server-notebook" && baseline != null;
            if (baseline != null)
            {
                store.InstallPackage(Path.Combine(baseline, listing.Manifest.Id + "-1.0.0.hellplugin"));
                if (disabled) store.SetEnabled(listing.Manifest.Id, false);
            }
            await PluginCatalog.Install(listing, store);
            var installed = store.List().Single(plugin => plugin.Manifest.Id == listing.Manifest.Id);
            if (installed.Enabled == disabled || installed.Error.Length > 0 || installed.Manifest.Version != listing.Manifest.Version) throw new InvalidDataException("Public plugin failed to install: " + listing.Manifest.Id);
            var preview = await PluginCatalog.Preview(listing, Path.Combine(folder, "previews"));
            if (preview.Manifest.Screenshots.Count == 0 || preview.Manifest.Releases.Count == 0 || preview.Manifest.Screenshots.Any(path => !File.Exists(Path.Combine(preview.Directory, path)))) throw new InvalidDataException("Public plugin preview is incomplete.");
            Console.WriteLine("[PUBLIC INSTALL] " + listing.Manifest.Id + " " + listing.Manifest.Version + " " + listing.Sha256);
        }
        var sample = listings.First(entry => entry.Manifest.Id == "server-notebook");
        store.SetEnabled(sample.Manifest.Id, false);
        await PluginCatalog.Install(sample, store);
        if (store.List().Single(plugin => plugin.Manifest.Id == sample.Manifest.Id).Enabled) throw new InvalidDataException("Update re-enabled a disabled plugin.");
        using var client = new HttpClient(); var bytes = await client.GetByteArrayAsync(sample.PackageUrl);
        using var sha = SHA256.Create(); string checksum = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        if (checksum != sample.Sha256) throw new InvalidDataException("Downloaded package does not match the public catalog.");
        File.WriteAllBytes(Path.Combine(folder, "server-notebook-" + sample.Manifest.Version + ".hellplugin"), bytes);
        Console.WriteLine("[PUBLIC MARKET PASS] 4 actual HTTPS installs; disabled state preserved; downloaded SHA-256 matches");
    }
}
