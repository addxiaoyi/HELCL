using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Updater
{
	public const string DEFAULT_URL = Config.DEFAULT_UPDATE_URL;

	private static readonly Regex Sha256Pattern = new Regex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);

	public static List<long> _parse(object ver)
	{
		if (!UpdateVersion.TryParse(ver, out UpdateVersion version))
			throw new ValueError("无效的版本号");
		return version.Parts.Select(part => (long)part).ToList();
	}

	public static bool Newer(object remote, string local = null)
	{
		return UpdateVersion.TryParse(remote, out UpdateVersion remoteVersion)
			&& UpdateVersion.TryParse(local ?? AppInfo.APP_VERSION, out UpdateVersion localVersion)
			&& remoteVersion.CompareTo(localVersion) > 0;
	}

	public static string ManifestUrl() => Config.UpdateUrl();

	public static bool ValidSha256(object value)
	{
		return value is string hash && Sha256Pattern.IsMatch(hash.Trim());
	}

	public static PyDict Check(DownloadManager dm = null)
	{
		try
		{
			dm = dm ?? new DownloadManager(2L);
			return EvaluateManifest(dm.FetchJson(ManifestUrl(), 12L));
		}
		catch (Exception error)
		{
			return FailedCheck("检查更新失败: " + Py.ExcStr(error));
		}
	}

	public static PyDict EvaluateManifest(object manifest)
	{
		if (!(manifest is PyDict fields))
			return FailedCheck("更新清单格式无效，需要 JSON 对象");

		string version = ReadText(fields, "version", "latest");
		if (!UpdateVersion.TryParse(version, out _))
			return FailedCheck("更新清单缺少有效版本号");

		string url = ReadText(fields, "url", "download");
		string hash = ReadText(fields, "sha256").ToLowerInvariant();
		bool hasUpdate = Newer(version);
		if (hasUpdate && !ValidSha256(hash))
			return FailedCheck("更新清单缺少有效 SHA-256，已拒绝自动更新", version);
		if (hasUpdate && !ValidDownloadUrl(url))
			return FailedCheck("更新清单缺少有效 HTTPS 下载地址", version);

		return PyDict.Of("ok", true, "current", AppInfo.APP_VERSION, "latest", version,
			"has_update", hasUpdate, "message", hasUpdate ? "发现 " + version : "已是最新版本",
			"notes", ReadText(fields, "notes", "changelog"), "url", url, "sha256", hash);
	}

	private static string ReadText(PyDict fields, string key, string fallback = null)
	{
		string text = fields[key] as string;
		if (string.IsNullOrWhiteSpace(text) && fallback != null)
			text = fields[fallback] as string;
		return (text ?? "").Trim();
	}

	private static PyDict FailedCheck(string message, string latest = null)
	{
		return PyDict.Of("ok", false, "current", AppInfo.APP_VERSION,
			"latest", latest ?? AppInfo.APP_VERSION, "has_update", false,
			"message", message, "notes", "", "url", "", "sha256", "");
	}

	private static bool ValidDownloadUrl(string url)
	{
		return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
			&& !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)
			&& (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
	}

	// Stage beside the executable so the final replacement stays on one volume.
	public static string BuildApplyScript(string package, string exe, int pid, bool restart, string sha256 = null)
	{
		if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(exe))
			throw new ArgumentException("更新包和启动器路径不能为空");
		if (pid <= 0)
			throw new ArgumentOutOfRangeException(nameof(pid));
		if (sha256 != null && !ValidSha256(sha256))
			throw new ArgumentException("更新包 SHA-256 无效", nameof(sha256));

		package = Path.GetFullPath(package);
		exe = Path.GetFullPath(exe);
		if (string.Equals(package, exe, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("更新包不能覆盖自身");

		string Quote(string path) => "'" + path.Replace("'", "''") + "'";
		string header = "$ErrorActionPreference = 'Stop'\n"
			+ "$package = " + Quote(package) + "\n"
			+ "$exe = " + Quote(exe) + "\n"
			+ "$parentId = " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"
			+ "$expectedHash = " + Quote(sha256?.Trim() ?? "") + "\n";
		string replace = @"
$staged = $exe + '.' + [Guid]::NewGuid().ToString('N') + '.new'
$backup = $exe + '.bak'
$log = $package + '.apply.log'
function Read-Sha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    try {
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
        finally { $hasher.Dispose() }
    }
    finally { $stream.Dispose() }
}
try {
    if (Get-Process -Id $parentId -ErrorAction SilentlyContinue) {
        try { Wait-Process -Id $parentId -Timeout 60 -ErrorAction Stop }
        catch { if (Get-Process -Id $parentId -ErrorAction SilentlyContinue) { throw } }
    }
    if (-not [IO.File]::Exists($package)) { throw 'Update package is missing.' }
    if (-not [IO.File]::Exists($exe)) { throw 'Launcher executable is missing.' }
    if (-not $expectedHash) { $expectedHash = Read-Sha256 $package }
    Copy-Item -LiteralPath $package -Destination $staged -Force
    if ((Read-Sha256 $staged) -ne $expectedHash) {
        throw 'Update package SHA-256 verification failed.'
    }
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            [IO.File]::Replace($staged, $exe, $backup)
            break
        }
        catch {
            if ($attempt -eq 39) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
    Remove-Item -LiteralPath $package -ErrorAction Continue
    if ([IO.File]::Exists($log)) { Remove-Item -LiteralPath $log -ErrorAction Continue }
";
		string restartCommand = restart
			? "    Start-Process -FilePath $exe -WorkingDirectory ([IO.Path]::GetDirectoryName($exe))\n"
			: "";
		string finish = @"
    exit 0
}
catch {
    $failure = $_.Exception.ToString()
    [IO.File]::WriteAllText($log, $failure, [Text.Encoding]::UTF8)
    Write-Error -Message $failure -ErrorAction Continue
    exit 1
}
finally {
    if ([IO.File]::Exists($staged)) { Remove-Item -LiteralPath $staged -ErrorAction Continue }
}
";
		return header + replace + restartCommand + finish;
	}

	public static void ApplyStaged(string package, bool restart)
	{
#if HELL_PORTABLE
        throw new PlatformNotSupportedException("便携测试版请退出后解压新包到独立目录，再迁移 data；暂不自动替换可执行程序。");
#else
		string path = Utils.AbsPath(package);
		if (!File.Exists(path))
			throw new RuntimeError("更新包不存在: " + path);
		string name = Path.GetFileNameWithoutExtension(path);
		const string prefix = "HelL-";
		if (!name.StartsWith(prefix, StringComparison.Ordinal) || !ValidSha256(name.Substring(prefix.Length)))
			throw new RuntimeError("更新包缺少原始校验信息，请重新下载");
		string expectedHash = name.Substring(prefix.Length);
		if (!string.Equals(Utils.Sha256File(path), expectedHash, StringComparison.OrdinalIgnoreCase))
			throw new RuntimeError("更新包 SHA-256 校验失败，请重新下载");

		using Process current = Process.GetCurrentProcess();
		string exe = current.MainModule?.FileName ?? "";
		if (exe.Length == 0)
			throw new RuntimeError("找不到启动器自身路径");

		string script = BuildApplyScript(path, exe, current.Id, restart, expectedHash);
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		using Process updater = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
			UseShellExecute = false,
			CreateNoWindow = true
		});
		if (updater == null)
			throw new RuntimeError("无法启动更新程序");
#endif
	}

	public static string Download(PyDict info, DownloadManager dm = null)
	{
		if (info == null)
			throw new RuntimeError("更新清单为空");
		string url = ReadText(info, "url");
		if (!ValidDownloadUrl(url))
			throw new RuntimeError("更新清单没有有效 HTTPS 下载地址");
		string hash = ReadText(info, "sha256").ToLowerInvariant();
		if (!ValidSha256(hash))
			throw new RuntimeError("更新包缺少有效 SHA-256，已拒绝下载");

		// A manifest version is display text; it must never become a filesystem path.
		string path = PPath.Join(Utils.ROOT, "cache", "HelL-" + hash + ".bin");
		dm = dm ?? new DownloadManager(4L);
		dm.Download(url, path, sha256: hash);
		if (!string.Equals(Utils.Sha256File(path), hash, StringComparison.OrdinalIgnoreCase))
		{
			File.Delete(Utils.AbsPath(path));
			throw new RuntimeError("更新包 SHA-256 校验失败");
		}
		return path;
	}
}
