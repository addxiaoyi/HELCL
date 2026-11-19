using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Contracts;

namespace HelL.Services;

public sealed partial class BridgeHost : IDisposable
{
	private static class CoreState
	{
		internal static readonly object Gate = new object();

		internal static BackendApi? Api;

		internal static EventBus? Bus;

		internal static string? Root;

        internal static RootLease? Lease;

        static CoreState() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Lease?.Dispose();
	}

	private Process? _proc;

	private BridgeServer? _server;

	private int _disposed;

    private HostPipeServer? _hostPipe;

	private static string? _python;

	private static readonly Regex PortRegex = new Regex("port=(\\d+)", RegexOptions.Compiled);

	public BridgeClient Client { get; }

	public int Port { get; }

	public string Root { get; }

	public string Backend { get; }

	public event EventHandler? Exited;

    public HostPipeServer StartHostPipe(IEnumerable<HostPermission> permissions)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BridgeHost));
        lock (CoreState.Gate)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(BridgeHost));
            if (_server == null || CoreState.Api == null || !string.Equals(CoreState.Root, Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A local in-process owner is required for the companion pipe.");
            if (_hostPipe != null) throw new InvalidOperationException("The companion pipe is already active.");
            return _hostPipe = HostPipeServer.Start(CoreState.Api, Environment.MachineName, permissions);
        }
    }

    public void StopHostPipe()
    {
        lock (CoreState.Gate) { _hostPipe?.Dispose(); _hostPipe = null; }
    }

	private BridgeHost(BridgeClient client, Process proc, int port, string root, string backend)
	{
		Client = client;
		_proc = proc;
		Port = port;
		Root = root;
		Backend = backend;
		try
		{
			proc.Exited += delegate
			{
				this.Exited?.Invoke(this, EventArgs.Empty);
			};
		}
		catch (InvalidOperationException)
		{
		}
	}

	private BridgeHost(BridgeClient client, BridgeServer server, int port, string root, string backend)
	{
		Client = client;
		_server = server;
		Port = port;
		Root = root;
		Backend = backend;
		server.Crashed += delegate
		{
			if (Volatile.Read(ref _disposed) == 0)
			{
				this.Exited?.Invoke(this, EventArgs.Empty);
			}
		};
	}

	public static async Task<BridgeHost> StartAsync(CancellationToken ct = default(CancellationToken))
	{
		string text = FindRoot();
		string text2 = Path.Combine(text, "bridge", "server.py");
		string environmentVariable = Environment.GetEnvironmentVariable("TIPCL_BRIDGE_EXE");
		string text3 = ((!string.IsNullOrWhiteSpace(environmentVariable) && File.Exists(environmentVariable)) ? Path.GetFullPath(environmentVariable) : null);
		bool flag = string.Equals(Environment.GetEnvironmentVariable("TIPCL_BRIDGE"), "python", StringComparison.OrdinalIgnoreCase);
		if (flag && File.Exists(text2))
		{
			return await StartExternalAsync(text, null, text2, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (text3 != null && !flag)
		{
			return await StartExternalAsync(text, text3, text2, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await StartInProcessAsync(text, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static BackendApi EnsureCore(string root, out EventBus bus)
	{
		lock (CoreState.Gate)
		{
			if (CoreState.Api != null && CoreState.Bus != null)
			{
				if (!string.Equals(CoreState.Root, root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This process already owns a different launcher data directory.");
				bus = CoreState.Bus;
				return CoreState.Api;
			}
            RootLease lease = RootLease.Acquire(root);
            try
            {
                Environment.SetEnvironmentVariable("TIPCL_HOME", root);
                Utils.SetRoot(root);
                Config.Reset();
                Guard.Install(PPath.Join(root, "tipcl-error.log"));
                string language = Py.Strip(Environment.GetEnvironmentVariable("TIPCL_LANG") ?? "").Replace("-", "_");
                if (language.Length == 0) I18nCore.InitLanguage();
                else
                {
                    if (!I18nCore.AvailableLanguages().ContainsKey(language)) language = I18nCore.CurrentLanguage();
                    I18nCore.CurrentLang = language;
                }
                CoreState.Bus = new EventBus();
                CoreState.Api = new BackendApi(CoreState.Bus);
                CoreState.Root = root;
                CoreState.Lease = lease;
                HelL.Core.Launcher.Ai.McpServer.StartIfEnabled(CoreState.Api);
                HelL.Core.Launcher.Ai.Minecraft.MinecraftModels.StartIfSelected(CoreState.Api);
                bus = CoreState.Bus;
                return CoreState.Api;
            }
            catch
            {
                CoreState.Api = null; CoreState.Bus = null; CoreState.Root = null; CoreState.Lease = null;
                lease.Dispose();
                throw;
            }
		}
	}

	private static async Task<BridgeHost> StartInProcessAsync(string root, CancellationToken ct)
	{
		string token = NewToken();
		BridgeServer srv = await Task.Run(delegate
		{
			EventBus bus;
			BridgeServer bridgeServer = new BridgeServer(EnsureCore(root, out bus), bus, token);
			bridgeServer.Start();
			return bridgeServer;
		}, ct).ConfigureAwait(continueOnCapturedContext: false);
		int port = srv.Port;
		string backend = I18n.L("C 桥");
		BridgeClient client = new BridgeClient(new Uri($"http://127.0.0.1:{port}/"), token);
		for (int i = 0; i < 60; i++)
		{
			try
			{
				if (await client.IsHealthyAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
				{
					client.ConnectEvents();
					return new BridgeHost(client, srv, port, CoreState.Root ?? root, backend);
				}
			}
			catch
			{
			}
			await Task.Delay(100, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		client.Dispose();
		srv.Stop();
		throw new InvalidOperationException(I18n.L("桥已启动但 /health 无响应"));
	}

	private static async Task<BridgeHost> StartExternalAsync(string root, string? native, string server, CancellationToken ct)
	{
		string text = await Task.Run((Func<string>)FindPython, ct).ConfigureAwait(continueOnCapturedContext: false);
		string token = NewToken();
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			WorkingDirectory = root,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		string backend;
		if (native != null)
		{
			backend = I18n.L("C 桥");
			processStartInfo.FileName = native;
			processStartInfo.Arguments = "--root " + QuoteArg(root);
			string text2 = Path.GetDirectoryName(native) ?? "";
			string text3 = Environment.GetEnvironmentVariable("PATH") ?? "";
			if (!string.IsNullOrEmpty(text2))
			{
				processStartInfo.EnvironmentVariables["PATH"] = text2 + Path.PathSeparator + text3;
			}
			if (File.Exists(text))
			{
				processStartInfo.EnvironmentVariables["TIPCL_PYTHON"] = text;
			}
		}
		else
		{
			if (!File.Exists(server))
			{
				throw new FileNotFoundException(I18n.L("找不到外部后端程序（TIPCL_BRIDGE_EXE）"));
			}
			backend = I18n.L("Python 桥");
			processStartInfo.FileName = text;
			processStartInfo.Arguments = "-u " + QuoteArg(server) + " --root " + QuoteArg(root);
			processStartInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
			processStartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
		}
		processStartInfo.EnvironmentVariables["TIPCL_HOME"] = root;
		processStartInfo.EnvironmentVariables["TIPCL_BRIDGE_TOKEN"] = token;
		Process proc = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		StringBuilder err = new StringBuilder();
		if (!proc.Start())
		{
			throw new InvalidOperationException(I18n.L("无法启动桥进程"));
		}
		proc.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			string data = e.Data;
			if (data != null && data.Length > 0 && err.Length < 4000)
			{
				err.AppendLine(e.Data);
			}
		};
		try
		{
			proc.BeginErrorReadLine();
		}
		catch
		{
		}
		string line = null;
		DateTime deadline = DateTime.UtcNow.AddSeconds(30.0);
		Task<string> readTask = proc.StandardOutput.ReadLineAsync();
		while (DateTime.UtcNow < deadline)
		{
			ct.ThrowIfCancellationRequested();
			if (await Task.WhenAny(readTask, Task.Delay(200, ct)).ConfigureAwait(continueOnCapturedContext: false) != readTask)
			{
				if (proc.HasExited)
				{
					break;
				}
				continue;
			}
			line = await readTask.ConfigureAwait(continueOnCapturedContext: false);
			if ((line != null && line.IndexOf("TIPCL_BRIDGE", StringComparison.Ordinal) >= 0) || line == null)
			{
				break;
			}
			readTask = proc.StandardOutput.ReadLineAsync();
		}
		if (line == null || line.IndexOf("TIPCL_BRIDGE", StringComparison.Ordinal) < 0)
		{
			KillTree(proc);
			string text4 = err.ToString().Trim();
			throw new InvalidOperationException(I18n.L("桥进程未输出端口") + ((text4.Length > 0) ? (I18n.L("：\n") + Tail(text4, 600)) : ""));
		}
		Match match = PortRegex.Match(line);
		if (!match.Success)
		{
			throw new InvalidOperationException(I18n.L("无法解析桥端口: ") + line);
		}
		int port = int.Parse(match.Groups[1].Value);
		BridgeClient client = new BridgeClient(new Uri($"http://127.0.0.1:{port}/"), token);
		for (int i = 0; i < 60; i++)
		{
			try
			{
				if (await client.IsHealthyAsync(ct).ConfigureAwait(continueOnCapturedContext: false))
				{
					client.ConnectEvents();
					return new BridgeHost(client, proc, port, root, backend);
				}
			}
			catch
			{
			}
			await Task.Delay(100, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		client.Dispose();
		KillTree(proc);
		throw new InvalidOperationException(I18n.L("桥已启动但 /health 无响应"));
	}

	private static string Tail(string text, int max)
	{
		if (text.Length > max)
		{
			return "…" + text.Substring(text.Length - max);
		}
		return text;
	}

	public static string FindRoot()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("TIPCL_HOME");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return NormalizeRoot(Path.GetFullPath(environmentVariable));
		}
		string[] array = new string[3]
		{
			AppContext.BaseDirectory,
			Environment.CurrentDirectory,
			Path.GetDirectoryName(typeof(BridgeHost).Assembly.Location) ?? ""
		};
		for (int i = 0; i < array.Length; i++)
		{
			string text = WalkUp(array[i]);
			if (text != null)
			{
				return text;
			}
		}
		string text2 = Payload.EnsureRoot();
		if (text2 != null)
		{
			return NormalizeRoot(text2);
		}
		throw new DirectoryNotFoundException(I18n.L("无法准备启动器数据目录，请检查 %LOCALAPPDATA%\\HelL 是否可写"));
	}

	public static string FindPython()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("TIPCL_PYTHON");
		if (!string.IsNullOrWhiteSpace(environmentVariable) && File.Exists(environmentVariable))
		{
			return environmentVariable;
		}
		if (_python != null)
		{
			return _python;
		}
		string text = Payload.EmbeddedPython();
		if (text != null)
		{
			return _python = text;
		}
		string text2 = null;
		string text3 = null;
		int num = 0;
		foreach (string item in PythonCandidates())
		{
			int num2 = ProbePython(item);
			if (num2 >= 0)
			{
				if (text2 == null)
				{
					text2 = item;
				}
				if (num2 > num)
				{
					text3 = item;
					num = num2;
				}
				if (num2 == 3)
				{
					break;
				}
			}
		}
		return _python = text3 ?? text2 ?? "python";
	}

	private static IEnumerable<string> PythonCandidates()
	{
		List<string> list = new List<string>();
		list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "binaries", "python", "envs", "tipcl5", "Scripts", "python.exe"));
		List<string> list2 = list;
		string[] array = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				try
				{
					list2.Add(Path.Combine(text.Trim().Trim('"'), "python.exe"));
				}
				catch
				{
				}
			}
		}
		list2.AddRange(RegisteredPythons());
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in list2)
		{
			string fullPath;
			try
			{
				fullPath = Path.GetFullPath(item);
			}
			catch
			{
				continue;
			}
			if (!(Path.GetDirectoryName(fullPath) ?? "").EndsWith("\\Microsoft\\WindowsApps", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath) && seen.Add(fullPath))
			{
				yield return fullPath;
			}
		}
	}

	private static List<string> RegisteredPythons()
	{
		List<string> list = new List<string>();
		RegistryHive[] array = new RegistryHive[2]
		{
			RegistryHive.CurrentUser,
			RegistryHive.LocalMachine
		};
		foreach (RegistryHive hKey in array)
		{
			RegistryView[] array2 = new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			foreach (RegistryView view in array2)
			{
				try
				{
					using RegistryKey registryKey = RegistryKey.OpenBaseKey(hKey, view);
					using RegistryKey registryKey2 = registryKey.OpenSubKey("Software\\Python\\PythonCore");
					if (registryKey2 == null)
					{
						continue;
					}
					string[] subKeyNames = registryKey2.GetSubKeyNames();
					foreach (string text in subKeyNames)
					{
						using RegistryKey registryKey3 = registryKey2.OpenSubKey(text + "\\InstallPath");
						if (registryKey3?.GetValue("ExecutablePath") is string { Length: >0 } text2)
						{
							list.Add(text2);
						}
						else if (registryKey3?.GetValue(null) is string { Length: >0 } text3)
						{
							list.Add(Path.Combine(text3, "python.exe"));
						}
					}
				}
				catch
				{
				}
			}
		}
		return list;
	}

	private static int ProbePython(string python)
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = python,
				Arguments = "-c \"import importlib.util as u; print(2 * bool(u.find_spec('requests')) + bool(u.find_spec('keyring')))\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true
			});
			if (process == null)
			{
				return -1;
			}
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			if (!process.WaitForExit(8000))
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
				return -1;
			}
			int result;
			return (process.ExitCode == 0 && int.TryParse(task.Result.Trim(), out result)) ? result : (-1);
		}
		catch
		{
			return -1;
		}
	}

	private static string? WalkUp(string start)
	{
		if (string.IsNullOrWhiteSpace(start))
		{
			return null;
		}
		try
		{
			for (DirectoryInfo directoryInfo = new DirectoryInfo(Path.GetFullPath(start)); directoryInfo != null; directoryInfo = directoryInfo.Parent)
			{
				if (LooksLikeRoot(directoryInfo.FullName))
				{
					return NormalizeRoot(directoryInfo.FullName);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	internal static string NormalizeRoot(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}
		string text = path.TrimEnd('\\', '/');
		if (text.Length == 0)
		{
			return path;
		}
		if (text.Length == 2 && text[1] == ':')
		{
			return text + Path.DirectorySeparatorChar;
		}
		return text;
	}

	public static string? FindNativeBridge(string root)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("TIPCL_BRIDGE_EXE");
		if (!string.IsNullOrWhiteSpace(environmentVariable) && File.Exists(environmentVariable))
		{
			return Path.GetFullPath(environmentVariable);
		}
		string[] array = new string[3]
		{
			Path.Combine(AppContext.BaseDirectory, "tipcl-bridge.exe"),
			Path.Combine(root, "tipcl-bridge.exe"),
			Path.Combine(root, "native", "build", "tipcl-bridge.exe")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	private static bool LooksLikeRoot(string path)
	{
		if (!File.Exists(Path.Combine(path, "bridge", "server.py")))
		{
			if (File.Exists(Path.Combine(path, "native", "data", "catalog.json")))
			{
				return FindNativeBridge(path) != null;
			}
			return false;
		}
		return true;
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}
        StopRemoteCompanion();
        StopHostPipe();
		Client.Dispose();
		BridgeServer server = _server;
		_server = null;
		if (server != null)
		{
			try
			{
				server.Stop();
			}
			catch
			{
			}
		}
		Process proc = _proc;
		if (proc != null && !proc.HasExited)
		{
			KillTree(_proc);
		}
		_proc?.Dispose();
		_proc = null;
	}

	private static void KillTree(Process proc)
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = "taskkill",
				Arguments = "/PID " + proc.Id + " /T /F",
				UseShellExecute = false,
				CreateNoWindow = true
			});
			process?.WaitForExit(5000);
		}
		catch
		{
		}
		try
		{
			if (!proc.HasExited)
			{
				proc.Kill();
			}
		}
		catch
		{
		}
	}

	internal static string QuoteArg(string arg)
	{
		if (string.IsNullOrEmpty(arg))
		{
			return "\"\"";
		}
		StringBuilder stringBuilder = new StringBuilder(arg.Length + 8);
		stringBuilder.Append('"');
		int num = 0;
		foreach (char c in arg)
		{
			switch (c)
			{
			case '\\':
				num++;
				continue;
			case '"':
				stringBuilder.Append('\\', num * 2 + 1).Append('"');
				break;
			default:
				stringBuilder.Append('\\', num).Append(c);
				break;
			}
			num = 0;
		}
		stringBuilder.Append('\\', num * 2).Append('"');
		return stringBuilder.ToString();
	}

	private static string NewToken()
	{
		byte[] array = new byte[32];
		using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
		{
			randomNumberGenerator.GetBytes(array);
		}
		return BitConverter.ToString(array).Replace("-", "");
	}
}
