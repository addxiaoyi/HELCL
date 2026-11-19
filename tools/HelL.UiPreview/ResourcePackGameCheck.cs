using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;

internal static class ResourcePackGameCheck
{
    internal static async Task<int> Run(string seed, string pack, string folder, bool hold)
    {
        seed = Path.GetFullPath(seed); pack = Path.GetFullPath(pack); folder = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(seed, ".hell-verification"))) throw new IOException("Game seed is not an isolated verification profile.");
        if (!File.Exists(pack) || Path.GetExtension(pack) != ".zip") throw new IOException("Installed resource pack is missing.");
        Directory.CreateDirectory(folder);
        using var verified = JsonDocument.Parse(File.ReadAllText(Path.Combine(seed, "verification.json")));
        string version = verified.RootElement.GetProperty("version").GetString()!, java = verified.RootElement.GetProperty("java").GetString()!;
        Utils.SetRoot(Environment.GetEnvironmentVariable("TIPCL_HOME")!); Config.Reset();
        Config.CONFIG.Set("shared_libraries", false); Config.CONFIG.Set("shared_assets", false);
        var instance = new Instance("resource-pack-game"); instance.EnsureStandardDirs();
        string source = Path.Combine(seed, ".minecraft", "verify-demo");
        foreach (string tree in new[] { "assets", "libraries", "versions" }) CopyTree(Path.Combine(source, tree), Path.Combine(instance.path, tree));
        string filename = Path.GetFileName(pack), installed = Path.Combine(instance.path, "resourcepacks", filename);
        File.Copy(pack, installed, true);
        string originalHash = Utils.Sha256File(pack);
        File.WriteAllLines(Path.Combine(instance.path, "options.txt"), new[] {
            "version:3955", "lang:zh_cn", "onboardAccessibility:false", "guiScale:2", "fullscreen:false", "maxFps:60",
            "resourcePacks:" + JsonSerializer.Serialize(new[] { "vanilla", "file/" + filename }), "incompatibleResourcePacks:[]" });
        using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(4));
        var metadata = (PyDict)instance.VersionJson(version);
        java = Java.ResolveLaunchJava(metadata, java, new DownloadManager(token: deadline.Token), (_, _, _) => { });
        var accounts = new AccountManager();
        var launch = McLauncher.BuildLaunchCommand(instance, version, accounts.LaunchProps(accounts.OfflineAccount("HelLResourceTest")), java,
            memory_mb: 2048L, width: 960L, height: 600L, extra_game_args: new object[] { "--demo" });
        var log = new ConcurrentQueue<string>();
        using var writer = new StreamWriter(Path.Combine(folder, "minecraft.log")) { AutoFlush = true };
        var game = new GameProcess(launch.cmd, launch.game_dir, line => { log.Enqueue(line); lock (writer) writer.WriteLine(line); });
        try
        {
            Console.WriteLine("[GAME] Started isolated Minecraft " + version + " PID=" + game.proc.Id);
            var clock = Stopwatch.StartNew(); bool loaded = false;
            while (clock.Elapsed < TimeSpan.FromMinutes(3))
            {
                if (game.Poll().HasValue) throw new IOException("Minecraft exited before resource reload finished.");
                game.proc.Refresh();
                loaded = game.proc.MainWindowHandle != IntPtr.Zero && log.Any(line => line.Contains("Reloading ResourceManager") && line.Contains(filename)) && log.Any(line => line.Contains("textures/atlas"));
                if (loaded) break;
                await Task.Delay(500);
            }
            if (!loaded) throw new TimeoutException("Minecraft did not load the selected pack within three minutes.");
            await Task.Delay(5000);
            string[] failures = log.Where(line => line.Contains("Failed to load resource") || line.Contains("Failed to reload resource") || line.Contains("Resource reload failed")).ToArray();
            if (failures.Length > 0) throw new IOException("Resource reload errors: " + string.Join("; ", failures));
            if (game.Poll().HasValue || Utils.Sha256File(installed) != originalHash) throw new IOException("Game or resource bytes changed unexpectedly.");
            File.WriteAllText(Path.Combine(folder, "game-ready.json"), JsonSerializer.Serialize(new { version, java, javaMajor = Java.GetJavaMajor(java),
                demo = true, pid = game.proc.Id, gameDir = launch.game_dir, filename, sha256 = originalHash, resourceManagerLoaded = true,
                atlasLoaded = true, resourceErrors = failures, windowTitle = game.proc.MainWindowTitle }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("[GAME READY] Pack enabled and resource atlas loaded. Window=" + game.proc.MainWindowTitle);
            if (hold)
            {
                clock.Restart();
                while (!File.Exists(Path.Combine(folder, "finish.signal")) && clock.Elapsed < TimeSpan.FromMinutes(8))
                { if (game.Poll().HasValue) break; await Task.Delay(500); }
            }
            return 0;
        }
        finally
        {
            if (!game.proc.HasExited && (!game.proc.CloseMainWindow() || !game.proc.WaitForExit(15000))) game.Kill();
            game.Wait(); game.proc.Dispose();
            Console.WriteLine("[GAME CLOSED] Isolated test process stopped.");
        }
    }

    private static void CopyTree(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        Directory.CreateDirectory(destination);
        foreach (var file in new DirectoryInfo(source).EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected link in isolated game seed.");
            File.Copy(file.FullName, Path.Combine(destination, file.Name), true);
        }
        foreach (var directory in new DirectoryInfo(source).EnumerateDirectories())
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Unexpected directory link in isolated game seed.");
            CopyTree(directory.FullName, Path.Combine(destination, directory.Name));
        }
    }
}
