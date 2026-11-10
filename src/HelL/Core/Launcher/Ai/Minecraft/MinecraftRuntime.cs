using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftRuntime
{
    private static readonly string[] Payload = { "package.json", "package-lock.json", "dist/server.js", "dist/game.js", "dist/tools.js", "dist/activities.js", "dist/terrain.js", "dist/crafting.js", "dist/versions.json" };
    private static readonly SemaphoreSlim InstallGate = new SemaphoreSlim(1);
    private static readonly Lazy<string> VersionKey = new Lazy<string>(Fingerprint);
    private static string Root => Path.GetFullPath(Py.S(Config.CONFIG.Get("minecraft_runtime_root", Path.Combine(Utils.ROOT, "ai", "minecraft"))));
    public static string Profiles => Path.Combine(Root, "login");
    public static string DirectoryPath => Path.Combine(Root, "runtime", VersionKey.Value);
    public static string Script => Path.Combine(DirectoryPath, "dist", "server.js");
    public static bool Ready => File.Exists(Script) && File.Exists(Path.Combine(DirectoryPath, ".ready")) && File.Exists(Path.Combine(DirectoryPath, "node_modules", "mineflayer", "package.json"));
    public static bool HealthyFiles()
    {
        if (!Ready) return false;
        try { return Payload.All(file => File.ReadAllText(Path.Combine(DirectoryPath, file.Replace('/', Path.DirectorySeparatorChar))) == Read(file)); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
    }
    public static PyDict Diagnose(string preferredNode, bool verifyProcess = false)
    {
        string node = FindNode(preferredNode), nodeError = "";
        try { ValidateNode(node); } catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception) { nodeError = ex.Message; }
        bool healthy = HealthyFiles();
        if (healthy && nodeError.Length == 0 && verifyProcess)
        {
            var probe = new McpClient("minecraft", node, Py.L(Script));
            try { probe.Connect(); healthy = probe.ListTools().OfType<PyDict>().Any(tool => Py.S(tool.Get("name")) == "connection_info"); }
            catch (Exception ex) { healthy = false; System.Diagnostics.Trace.TraceWarning("Minecraft component validation: " + ex.Message); }
            finally { probe.Close(); }
        }
        return PyDict.Of("ready", healthy && nodeError.Length == 0, "files_ok", healthy, "node", node, "node_error", nodeError,
            "code", nodeError.Length > 0 ? "node_unavailable" : !healthy ? "component_missing" : "ready",
            "message", nodeError.Length > 0 ? nodeError : !healthy ? "游戏组件缺失或已损坏，可一键修复后重新检测。" : "Node.js 与游戏组件已就绪。",
            "recovery", nodeError.Length > 0 ? "choose_node" : !healthy ? "repair" : "none");
    }

    private static string Fingerprint()
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", Payload.Select(Read))))).Replace("-", "").Substring(0, 20).ToLowerInvariant();
    }
    private static string Read(string file) => CoreResources.ReadText("minecraft/" + file) ?? throw new IOException("缺少内置 Minecraft MCP 文件：" + file);

    public static string FindNode(string preferred = "")
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return File.Exists(preferred) && Path.GetFileName(preferred).Equals("node.exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(preferred) : "";
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(dir => Path.Combine(dir.Trim('"'), "node.exe"))
            .Concat(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe") });
        foreach (string path in candidates)
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && Path.GetFileName(path).Equals("node.exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(path);
        return "";
    }

    public static void ValidateNode(string node)
    {
        if (!File.Exists(node)) throw new IOException("未找到 Node.js，请安装 Node.js 22 或更新版本，或选择 node.exe。");
        using var process = Process.Start(new ProcessStartInfo(node, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })
            ?? throw new IOException("无法启动 Node.js。");
        if (!process.WaitForExit(5000)) { process.Kill(); throw new IOException("Node.js 版本检查超时。"); }
        string output = process.StandardOutput.ReadToEnd().Trim().TrimStart('v');
        if (process.ExitCode != 0 || !Version.TryParse(output, out var version) || version.Major < 22) throw new IOException("Minecraft 组件需要 Node.js 22 或更新版本。");
    }

    public static void Install(string node, TaskContext ctx)
    {
        if (!InstallGate.Wait(0)) throw new InvalidOperationException("Minecraft 组件正在安装，请等待完成。");
        try
        {
            ctx.Token.ThrowIfCancellationRequested();
            ValidateNode(node);
            string npm = Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin", "npm-cli.js");
            if (!File.Exists(npm)) throw new IOException("此 Node.js 不包含 npm，请选择完整的 Node.js 安装。");
            string destination = DirectoryPath;
            string directory = destination + ".pending-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
            foreach (string file in Payload)
            {
                string target = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, Read(file), new UTF8Encoding(false));
            }
            ctx.Progress(0, 1, "正在安装 Minecraft MCP 组件…");
            var info = new ProcessStartInfo(node, Utils.List2CmdLine(new[] { npm, "ci", "--omit=dev", "--ignore-scripts", "--no-audit", "--no-fund", "--registry=https://registry.npmjs.org", "--fetch-retries=1", "--fetch-timeout=30000" }))
            { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.EnvironmentVariables["NODE_ENV"] = "production";
            using var process = Process.Start(info) ?? throw new IOException("无法启动组件安装。");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var deadline = Stopwatch.StartNew();
            try
            {
                while (!process.WaitForExit(250))
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    if (deadline.Elapsed > TimeSpan.FromMinutes(5)) throw new TimeoutException("组件下载超时，请检查网络后重试。");
                }
                if (process.ExitCode != 0) throw new IOException("组件安装失败（npm 退出码 " + process.ExitCode + "），请检查网络和 Node.js 安装后重试。");
            }
            finally { if (!process.HasExited) process.Kill(); }
            output.GetAwaiter().GetResult(); errors.GetAwaiter().GetResult();
            if (!File.Exists(Path.Combine(directory, "node_modules", "mineflayer", "package.json"))) throw new IOException("组件安装不完整，请重试。");
            var probe = new McpClient("minecraft", node, Py.L(Path.Combine(directory, "dist", "server.js")));
            using var abort = ctx.Token.Register(probe.Abort);
            try
            {
                probe.Connect();
                if (!probe.ListTools().OfType<PyDict>().Any(tool => Py.S(tool.Get("name")) == "connection_info")) throw new IOException("组件自检未返回预期工具，请重试。");
            }
            finally { probe.Close(); }
            ctx.Token.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(directory, ".ready"), "1.0.0");
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool existed = Directory.Exists(destination);
            if (existed) Directory.Move(destination, backup);
            try { Directory.Move(directory, destination); }
            catch { if (existed) Directory.Move(backup, destination); throw; }
            ctx.Progress(1, 1, "Minecraft MCP 组件已准备好");
        }
        finally { InstallGate.Release(); }
    }

    public static void ClearLogin()
    {
        if (MinecraftSession.Running) throw new InvalidOperationException("请先断开 AI 玩家，再清除登录缓存。");
        if (!Directory.Exists(Profiles)) return;
        for (string? path = Path.GetFullPath(Profiles); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("登录缓存目录不能包含链接。");
        var files = new DirectoryInfo(Profiles).GetFiles("*.json");
        if (files.Any(file => (file.Attributes & FileAttributes.ReparsePoint) != 0)) throw new IOException("登录缓存包含链接，未清除。");
        foreach (var file in files) file.Delete();
    }
}
