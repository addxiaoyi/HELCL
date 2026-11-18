using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class RuntimePackage
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public sealed class LocalRuntime
{
    private readonly string _root;
    private readonly HttpClient _http;
    private Process? _process;
    private string _endpoint = "";
    private readonly object _processLock = new object();
    public string ModelsPath
    {
        get
        {
            string exe = FindExecutable();
            if (exe.Length == 0 || exe.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return Path.Combine(_root, "models");
            string? configured = Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            return !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models");
        }
    }
    public bool Managed { get { lock (_processLock) return _process != null && !_process.HasExited; } }

    public LocalRuntime(string root, HttpClient http) { _root = root; _http = http; }

    public string FindExecutable()
    {
        var candidates = new[] {
            Path.Combine(_root, "runtime", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe")
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.Combine(path.Trim('"'), "ollama.exe")));
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    public async Task<RuntimePackage> PackageAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ollama/ollama/releases/latest");
        request.Headers.UserAgent.ParseAdd("HelL-LocalModels/1.0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return await PackageFromReleaseAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        var release = document.RootElement;
        string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
        string name = architecture.Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? "ollama-windows-arm64.zip" : "ollama-windows-amd64.zip";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (OllamaClient.Text(asset, "name") != name) continue;
            string digest = OllamaClient.Text(asset, "digest");
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal))
                return await PackageFromReleaseAsync(ct).ConfigureAwait(false);
            return new RuntimePackage { Version = OllamaClient.Text(release, "tag_name"), Url = OllamaClient.Text(asset, "browser_download_url"), Size = OllamaClient.Number(asset, "size"), Sha256 = digest.Substring(7) };
        }
        throw new IOException("官方版本未提供当前架构的 Windows 便携安装包。");
    }

    private async Task<RuntimePackage> PackageFromReleaseAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var release = await _http.GetAsync("https://github.com/ollama/ollama/releases/latest", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        release.EnsureSuccessStatusCode();
        Uri resolved = release.RequestMessage.RequestUri;
        var tag = Regex.Match(resolved.AbsolutePath, @"\A/ollama/ollama/releases/tag/(v[0-9][a-zA-Z0-9.\-]*)\z");
        if (resolved.Host != "github.com" || !tag.Success) throw new IOException("无法识别 Ollama 官方版本，请稍后重试。");
        string version = tag.Groups[1].Value;
        string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
        string file = architecture.Equals("ARM64", StringComparison.OrdinalIgnoreCase) ? "ollama-windows-arm64.zip" : "ollama-windows-amd64.zip";
        string prefix = "https://github.com/ollama/ollama/releases/download/" + version + "/";
        using var checksums = await _http.GetAsync(prefix + "sha256sum.txt", timeout.Token).ConfigureAwait(false);
        checksums.EnsureSuccessStatusCode();
        string manifest = await checksums.Content.ReadAsStringAsync().ConfigureAwait(false);
        string digest = ChecksumFor(manifest, file);
        using var head = new HttpRequestMessage(HttpMethod.Head, prefix + file);
        using var asset = await _http.SendAsync(head, timeout.Token).ConfigureAwait(false);
        asset.EnsureSuccessStatusCode();
        long size = asset.Content.Headers.ContentLength ?? 0;
        if (size <= 0) throw new IOException("官方服务器未返回安装包大小，请稍后重试。");
        return new RuntimePackage { Version = version, Url = prefix + file, Sha256 = digest, Size = size };
    }

    public static string ChecksumFor(string manifest, string file)
    {
        foreach (string line in manifest.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"\A([a-fA-F0-9]{64})\s+\*?(?:\./)?(.+)\z");
            if (match.Success && match.Groups[2].Value == file) return match.Groups[1].Value.ToLowerInvariant();
        }
        throw new IOException("官方校验清单中没有此安装包，已停止自动安装。");
    }

    public async Task InstallAsync(RuntimePackage package, Action<long, long, string> progress, CancellationToken ct)
    {
        if (Managed) throw new InvalidOperationException("请先停止由启动器管理的运行环境，再更新安装。");
        var uri = new Uri(package.Url);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/ollama/ollama/releases/download/", StringComparison.Ordinal))
            throw new ArgumentException("仅支持 Ollama 官方发布包。");
        string archive = Path.Combine(_root, "downloads", "ollama-" + package.Sha256 + ".zip");
        await new ModelDownload(_http).DownloadAsync(uri, archive, package.Size, package.Sha256, progress, ct).ConfigureAwait(false);
        string staging = Path.Combine(_root, "runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string destination = Path.Combine(_root, "runtime");
        try
        {
            progress(0, 0, "正在解压运行环境");
            using (var zip = ZipFile.OpenRead(archive))
            {
                long unpacked = zip.Entries.Sum(entry => entry.Length);
                if (new DriveInfo(Path.GetPathRoot(staging)!).AvailableFreeSpace < unpacked + ModelRules.GiB)
                    throw new IOException("解压空间不足。安装包已保留，释放磁盘空间后重试。");
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    string path = SafeArchivePath(staging, entry.FullName);
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var input = entry.Open();
                    using var output = File.Create(path);
                    await input.CopyToAsync(output, 131072, ct).ConfigureAwait(false);
                }
            }
            if (!File.Exists(Path.Combine(staging, "ollama.exe"))) throw new IOException("安装包中没有找到 ollama.exe。");
            ct.ThrowIfCancellationRequested();
            // Keep the old installation intact until extraction has fully succeeded.
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool hadRuntime = Directory.Exists(destination);
            if (hadRuntime) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); }
            catch { if (hadRuntime) Directory.Move(backup, destination); throw; }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static string SafeArchivePath(string root, string name)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, name));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || name.Contains(":"))
            throw new IOException("安装包包含越界路径，已停止解压。");
        return path;
    }

    public async Task StartAsync(string endpoint, CancellationToken ct)
    {
        endpoint = ModelRules.Endpoint(endpoint);
        lock (_processLock)
        {
            if (Managed)
            {
                if (_endpoint != endpoint) throw new IOException("运行环境仍在 " + _endpoint + "，请先停止服务再切换地址。");
                return;
            }
            _process?.Dispose();
            string exe = FindExecutable();
            if (exe.Length == 0) throw new IOException("没有找到 Ollama，请先安装运行环境。");
            Directory.CreateDirectory(ModelsPath);
            var start = new ProcessStartInfo(exe, "serve")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            start.EnvironmentVariables["OLLAMA_HOST"] = endpoint;
            start.EnvironmentVariables["OLLAMA_NO_CLOUD"] = "1";
            // Reuse an existing installation's store; only our portable runtime gets an isolated store.
            if (exe.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                start.EnvironmentVariables["OLLAMA_MODELS"] = ModelsPath;
            _process = new Process { StartInfo = start };
            _process.OutputDataReceived += (_, args) => Log(args.Data);
            _process.ErrorDataReceived += (_, args) => Log(args.Data);
            _process.Start();
            _endpoint = endpoint;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        try
        {
            using var client = new OllamaClient(endpoint);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Managed) throw new IOException("Ollama 启动后退出。请检查 ai-local/runtime.log（端口可能已被占用）。");
                try { await client.JsonAsync("/api/version", null, ct).ConfigureAwait(false); return; }
                catch (HttpRequestException) { await Task.Delay(500, ct).ConfigureAwait(false); }
            }
            throw new IOException("本地服务未在预期时间内就绪。请查看 ai-local/runtime.log 后重试。");
        }
        catch { Stop(); throw; }
    }

    private void Log(string? line)
    {
        if (line == null) return;
        lock (_processLock)
        {
            try { File.AppendAllText(Path.Combine(_root, "runtime.log"), line + Environment.NewLine); }
            catch (IOException ex) { System.Diagnostics.Trace.TraceError("LocalRuntime.Log: " + ex.Message); }
        }
    }

    public void Stop()
    {
        lock (_processLock)
        {
            if (_process == null) return;
            if (!_process.HasExited)
            {
                using var stop = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + _process.Id + " /T /F")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true });
                if (stop == null || !stop.WaitForExit(5000)) throw new IOException("本地服务未能及时停止，请稍后重试。");
                if (!_process.HasExited) throw new IOException("无法停止本地服务进程，系统拒绝了请求。");
            }
            _process.Dispose();
            _process = null;
        }
    }
}
