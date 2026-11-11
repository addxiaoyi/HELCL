using System.Diagnostics;
using System.Text.Json;
using HelL.Core.Launcher;
using HelL.Portable;

if (args.Length == 2 && args[0] == "--credentials") return CredentialProbe.Run(args[1]);
if (args.Length == 2 && args[0] == "--catalog")
{
    try { return CatalogProbe.Run(args[1]); }
    catch (Exception error) { Console.Error.WriteLine("Catalog probe failed: " + error.Message); return 1; }
}

if (args.Length != 2 || !Path.IsPathFullyQualified(args[0]))
{
    Console.Error.WriteLine("Usage: HelL.Portable.Verify <absolute isolated directory> <Minecraft version>");
    return 2;
}
string root = Path.GetFullPath(args[0]);
string marker = Path.Combine(root, ".hell-portable-verification");
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !File.Exists(marker))
{
    Console.Error.WriteLine("Refusing a nonempty directory without a verification marker.");
    return 2;
}
Directory.CreateDirectory(root);
File.WriteAllText(marker, "Dedicated install/demo verification; never use production data here.");
using var home = PortableHome.Open(root);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
GameProcess? game = null;
StreamWriter? log = null;
try
{
    var launcher = new PortableLauncher(root);
    if (!launcher.InstancesList().Any(instance => instance.Name == "Demo")) launcher.CreateInstance("Demo");
    long lastNote = 0;
    var progress = new Progress<InstallProgress>(note =>
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastNote) < 2000) return;
        Interlocked.Exchange(ref lastNote, now);
        Console.WriteLine($"{note.Percent:0}% {note.Message}");
    });
    launcher.Install("Demo", args[1], progress, timeout.Token);
    var launch = launcher.PrepareDemo("Demo", args[1], "", progress, timeout.Token);
    Directory.CreateDirectory(launch.game_dir);
    log = new StreamWriter(Path.Combine(root, "game.log")) { AutoFlush = true };
    game = new GameProcess(launch.cmd, launch.game_dir, line => log.WriteLine(line));
    bool rendered = false;
    var watch = Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(120))
    {
        timeout.Token.ThrowIfCancellationRequested();
        if (game.Poll() is { } code) throw new IOException($"Game exited with {code}; see game.log.");
        rendered = game.LastLines().Any(line => line.Contains("textures/atlas", StringComparison.OrdinalIgnoreCase));
        if (rendered) break;
        await Task.Delay(500, timeout.Token);
    }
    if (!rendered) throw new TimeoutException("No game atlas render evidence within two minutes; see game.log.");
    await Task.Delay(10000, timeout.Token);
    if (game.Poll().HasValue) throw new IOException("Game exited shortly after atlas creation.");
    string report = JsonSerializer.Serialize(new { ok = true, version = args[1],
        runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
        demo = true, javaMajor = Java.GetJavaMajor(launch.cmd[0]), atlasCreated = rendered, aliveAfterSeconds = 10 });
    File.WriteAllText(Path.Combine(root, "verification.json"), report);
    Console.WriteLine(report);
    return 0;
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(root, "failure.txt"), ex.ToString());
    Console.Error.WriteLine(ex.Message);
    return 1;
}
finally
{
    if (game != null)
    {
        if (!game.proc.HasExited) game.proc.Kill(entireProcessTree: true);
        game.Wait();
        game.proc.Dispose();
    }
    log?.Dispose();
}
