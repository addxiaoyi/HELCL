using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Platform;
using HelL.Core.Platform.Wpf;
using HelL.Pages;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL;

public partial class App : Application
{
	private enum Elevation
	{
		NotNeeded,
		Restarted,
		Declined
	}

	private static readonly string[] SelfCheckArgs = new string[5] { "--nav-dump", "--ime-check", "--i18n-check", "--consent-check", "--smoke" };

	private const int DeclineExitCode = 1223;

	public static bool IsDark { get; private set; }

	internal static ResourceDictionary? PaletteDictionary
	{
		get
		{
			int num = PaletteIndex();
			if (num < 0)
			{
				return null;
			}
			return Application.Current.Resources.MergedDictionaries[num];
		}
	}

	public static event Action? ThemeVisualsChanged;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		switch (RequestElevation(e.Args))
		{
		case Elevation.Declined:
			Shutdown(1223);
			return;
		case Elevation.Restarted:
			Shutdown(0);
			return;
		}
		if (e.Args.Contains("--nav-dump"))
		{
			Environment.ExitCode = NavDump.Run();
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--ime-check"))
		{
			Environment.ExitCode = ImeGuard.SelfTest();
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--i18n-check"))
		{
			Environment.ExitCode = I18nCheck.Run(e.Args);
			Shutdown(Environment.ExitCode);
			return;
		}
		if (e.Args.Contains("--consent-check"))
		{
			Environment.ExitCode = FeedbackConsent.SelfTest();
			Shutdown(Environment.ExitCode);
			return;
		}
		InitPlatform();
		I18n.Init(e.Args);
		Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata(60));
		RenderOptions.ProcessRenderMode = RenderMode.Default;
		base.DispatcherUnhandledException += OnUnhandled;
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs args)
		{
			LogCrash(args.ExceptionObject as Exception);
		};
		if (e.Args.Contains("--ai-ui-selftest"))
		{
			Environment.ExitCode = AiSelfTest.Run(e.Args);
			Shutdown(Environment.ExitCode);
		}
		else if (e.Args.Contains("--smoke"))
		{
			Smoke.Run(e.Args);
		}
		else
		{
			new MainWindow().Show();
		}
	}

	internal static void InitPlatform()
	{
		try
		{
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
			}
			catch (NotSupportedException)
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			}
			if (ServicePointManager.DefaultConnectionLimit < 64)
			{
				ServicePointManager.DefaultConnectionLimit = 64;
			}
			ServicePointManager.Expect100Continue = false;

			PlatformServices.ImageCodec = new WpfImageCodec();
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				PlatformServices.SecretStore = new WinCredSecretStore();
			}
			Crash.ShellOpenHook = ShellOpen;
			Auth.OpenBrowserHook = delegate(string url)
			{
				Process.Start(new ProcessStartInfo(url)
				{
					UseShellExecute = true
				})?.Dispose();
			};
		}
		catch (Exception ex)
		{
			LogCrash(ex);
		}
	}

	private static void ShellOpen(string path)
	{
		try
		{
			using (Process.Start(new ProcessStartInfo(PPath.Abs(path))
			{
				UseShellExecute = true
			}))
			{
			}
		}
		catch (Win32Exception ex)
		{
			throw new OSError(ex.Message, ex);
		}
		catch (InvalidOperationException ex2)
		{
			throw new OSError(ex2.Message, ex2);
		}
	}

	public static bool IsElevated()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private static bool NeedsElevation(string[] args)
	{
		string[] selfCheckArgs = SelfCheckArgs;
		foreach (string value in selfCheckArgs)
		{
			if (args.Contains<string>(value))
			{
				return false;
			}
		}
		return true;
	}

	private static Elevation RequestElevation(string[] args)
	{
		if (!NeedsElevation(args) || IsElevated())
		{
			return Elevation.NotNeeded;
		}
		string text;
		try
		{
			text = Process.GetCurrentProcess().MainModule?.FileName ?? "";
		}
		catch
		{
			text = "";
		}
		if (string.IsNullOrEmpty(text))
		{
			return Elevation.NotNeeded;
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = text,
			UseShellExecute = true,
			Verb = "runas",
			WorkingDirectory = AppContext.BaseDirectory,
			Arguments = JoinArgs(args)
		};
		try
		{
			Process.Start(startInfo);
			return Elevation.Restarted;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			MessageBox.Show(I18n.L("HelL 需要管理员权限才能运行。\n\n已取消提权，程序即将退出。"), I18n.L("需要管理员权限"), MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return Elevation.Declined;
		}
		catch (Exception ex2)
		{
			LogCrash(ex2);
			MessageBox.Show(I18n.L("HelL 需要管理员权限才能运行，但提权失败：\n\n{0}", ex2.Message), I18n.L("需要管理员权限"), MessageBoxButton.OK, MessageBoxImage.Hand);
			return Elevation.Declined;
		}
	}

	private static string JoinArgs(string[] args)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (string text in args)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(' ');
			}
			if (text.Length == 0)
			{
				stringBuilder.Append("\"\"");
				continue;
			}
			if (text.IndexOfAny(new char[3] { ' ', '\t', '"' }) < 0)
			{
				stringBuilder.Append(text);
				continue;
			}
			stringBuilder.Append('"');
			int num = 0;
			string text2 = text;
			foreach (char c in text2)
			{
				switch (c)
				{
				case '\\':
					num++;
					break;
				case '"':
					stringBuilder.Append('\\', num * 2 + 1);
					stringBuilder.Append('"');
					num = 0;
					break;
				default:
					stringBuilder.Append('\\', num);
					num = 0;
					stringBuilder.Append(c);
					break;
				}
			}
			stringBuilder.Append('\\', num * 2);
			stringBuilder.Append('"');
		}
		return stringBuilder.ToString();
	}

	private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogCrash(e.Exception);
		Smoke.Note("unhandled", e.Exception.ToString());
		e.Handled = true;
		try
		{
			(Application.Current?.MainWindow as MainWindow)?.Toast(I18n.L("出错了"), e.Exception.Message, ToastKind.Error);
		}
		catch
		{
		}
	}

	private static void LogCrash(Exception? ex)
	{
		if (ex == null)
		{
			return;
		}
		string contents = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
		string[] array = new string[3]
		{
			AppContext.BaseDirectory,
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			Path.GetTempPath()
		};
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				try
				{
					File.AppendAllText(Path.Combine(text, "tipcl-wpf-error.log"), contents);
					break;
				}
				catch
				{
				}
			}
		}
	}

	internal static void RaiseThemeVisualsChanged()
	{
		try
		{
			App.ThemeVisualsChanged?.Invoke();
		}
		catch (Exception)
		{
		}
	}

	internal static Uri PaletteUri(bool dark)
	{
		return new Uri(dark ? "/HelL;component/Themes/Palette.Dark.xaml" : "/HelL;component/Themes/Palette.Light.xaml", UriKind.Relative);
	}

	internal static int PaletteIndex()
	{
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		for (int i = 0; i < mergedDictionaries.Count; i++)
		{
			if ((mergedDictionaries[i].Source?.OriginalString ?? "").IndexOf("Themes/Palette.", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return i;
			}
		}
		for (int j = 0; j < mergedDictionaries.Count; j++)
		{
			if (mergedDictionaries[j].Contains("C.Paper") && mergedDictionaries[j].Contains("B.Accent"))
			{
				return j;
			}
		}
		return -1;
	}

	public static void ApplyTheme(bool dark)
	{
		IsDark = dark;
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		ResourceDictionary resourceDictionary = new ResourceDictionary
		{
			Source = PaletteUri(dark)
		};
		int num = PaletteIndex();
		if (num >= 0)
		{
			mergedDictionaries[num] = resourceDictionary;
		}
		else
		{
			mergedDictionaries.Insert(0, resourceDictionary);
		}
		SettingsPage.ApplyVisuals();
		foreach (MainWindow window in Application.Current.Windows.OfType<MainWindow>().ToArray())
		{
			Backdrop.Refresh(window);
			window.SyncThemeIndicator();
		}
	}

}
