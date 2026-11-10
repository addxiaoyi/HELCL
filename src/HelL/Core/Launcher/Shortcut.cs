using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public static class Shortcut
{
	private const string _BAD_NAME_CHARS = "\\/:*?\"<>|";

	private static string _PROJECT_ROOT => Path.GetDirectoryName(Path.GetFullPath(ExePath()));

	private static string ExePath()
	{
		return Process.GetCurrentProcess().MainModule.FileName;
	}

	public static string SafeFilename(object name)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = Py.Strip(Py.Str(Py.Or(name, "")));
		foreach (char c in text)
		{
			stringBuilder.Append(("\\/:*?\"<>|".IndexOf(c) >= 0) ? '_' : c);
		}
		string text2 = Py.Strip(stringBuilder.ToString(), ". ");
		if (text2.Length <= 0)
		{
			return "Minecraft";
		}
		return text2;
	}

	public static string DesktopDir()
	{
		if (Utils.IS_WINDOWS)
		{
			string text = null;
			try
			{
				text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			}
			catch (Exception)
			{
			}
			if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
			{
				return text;
			}
		}
		string text2 = (Utils.IS_WINDOWS ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME"));
		if (string.IsNullOrEmpty(text2))
		{
			text2 = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		string[] array = new string[2] { "Desktop", "桌面" };
		foreach (string text3 in array)
		{
			string text4 = PPath.Join(text2, text3);
			if (Directory.Exists(text4))
			{
				return text4;
			}
		}
		return PPath.Norm(text2);
	}

	public static List<string> LauncherCommand()
	{
		string fullPath = Path.GetFullPath(ExePath());
		string text = Path.Combine(Path.GetDirectoryName(fullPath), Utils.IS_WINDOWS ? "HelL-CLI.exe" : "HelL-CLI");
		return new List<string> { File.Exists(text) ? text : fullPath };
	}

	public static List<string> LaunchArgs(object instance, object version, object username = null, object account = null)
	{
		List<string> list = new List<string>
		{
			"-i",
			Py.Str(instance),
			"launch",
			Py.Str(version)
		};
		if (Py.Truthy(account))
		{
			list.AddRange(new string[2]
			{
				"--account",
				Py.Str(account)
			});
		}
		else if (Py.Truthy(username))
		{
			list.AddRange(new string[2]
			{
				"--username",
				Py.Str(username)
			});
		}
		return list;
	}

	private static string _quote(string arg)
	{
		if (!arg.Contains(" ") && arg.Length != 0)
		{
			return arg;
		}
		return "\"" + arg + "\"";
	}

	private static string _ps_literal(string t)
	{
		return "'" + t.Replace("'", "''") + "'";
	}

	private static string _create_windows(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string text = PPath.Join(DesktopDir(), name + ".lnk");
		List<string> list = new List<string>
		{
			"$ErrorActionPreference = 'Stop'",
			"$sh = New-Object -ComObject WScript.Shell",
			"$sc = $sh.CreateShortcut(" + _ps_literal(text) + ")",
			"$sc.TargetPath = " + _ps_literal(target),
			"$sc.Arguments = " + _ps_literal(arguments),
			"$sc.WorkingDirectory = " + _ps_literal(workdir),
			"$sc.Description = " + _ps_literal(description)
		};
		if (icon.Length > 0)
		{
			list.Add("$sc.IconLocation = " + _ps_literal(icon));
		}
		list.Add("$sc.Save()");
		string text2 = Path.Combine(Path.GetTempPath(), "tipcl_shortcut_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".ps1");
		try
		{
			File.WriteAllText(text2, string.Join("\n", list), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			using Process process = Process.Start(new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + text2 + "\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardError = true,
				RedirectStandardOutput = true
			});
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			string text3 = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				string text4 = Py.Strip((text3.Length > 0) ? text3 : task.Result);
				throw new ShortcutError((text4.Length > 0) ? Py.Slice(text4, 0, 400) : "PowerShell 创建快捷方式失败");
			}
		}
		finally
		{
			try
			{
				File.Delete(text2);
			}
			catch (Exception)
			{
			}
		}
		if (!File.Exists(PPath.Abs(text)))
		{
			throw new ShortcutError("快捷方式未生成: " + text);
		}
		return text;
	}

	private static void Chmod755(string path)
	{
	}

	private static string _create_linux(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string text = PPath.Join(DesktopDir(), name + ".desktop");
		List<string> list = new List<string>
		{
			"[Desktop Entry]",
			"Type=Application",
			"Name=" + name,
			"Comment=" + description,
			"Exec=" + Py.Strip(_quote(target) + " " + arguments),
			"Path=" + workdir,
			"Terminal=false",
			"Categories=Game;"
		};
		if (icon.Length > 0)
		{
			list.Add("Icon=" + icon);
		}
		File.WriteAllText(PPath.Abs(text), string.Join("\n", list) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Chmod755(PPath.Abs(text));
		return text;
	}

	private static string _create_macos(string name, string target, string arguments, string workdir, string icon, string description)
	{
		string text = PPath.Join(DesktopDir(), name + ".command");
		File.WriteAllText(contents: string.Join("\n", new List<string>
		{
			"#!/bin/sh",
			"# " + description,
			"cd " + _quote(workdir),
			"exec " + _quote(target) + " " + arguments
		}) + "\n", path: PPath.Abs(text), encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Chmod755(PPath.Abs(text));
		return text;
	}

	public static string CreateLaunchShortcut(object instance, object version, object username = null, object account = null, object name = null)
	{
		if (!Py.Truthy(version))
		{
			throw new ShortcutError("请先选择要创建快捷方式的版本");
		}
		List<string> list = LauncherCommand();
		string target = list[0];
		IEnumerable<string> source = list.Skip(1).Concat<string>(LaunchArgs(instance, version, username, account));
		string arguments = string.Join(" ", source.Select(_quote));
		string name2 = SafeFilename(Py.Truthy(instance) ? Py.Or(name, Py.Str(version) + " - " + Py.Str(instance)) : version);
		string icon = "";
		string[] array = new string[2]
		{
			Path.Combine(_PROJECT_ROOT, "icon.ico"),
			ExePath()
		};
		foreach (string text in array)
		{
			if (Utils.IS_WINDOWS && File.Exists(text))
			{
				icon = text;
				break;
			}
		}
		string description = "HelL 启动 " + Py.Str(Py.Or(instance, "default")) + " / " + Py.Str(version);
		string pROJECT_ROOT = _PROJECT_ROOT;
		if (Utils.IS_WINDOWS)
		{
			return _create_windows(name2, target, arguments, pROJECT_ROOT, icon, description);
		}
		if (Utils.IS_MAC)
		{
			return _create_macos(name2, target, arguments, pROJECT_ROOT, icon, description);
		}
		return _create_linux(name2, target, arguments, pROJECT_ROOT, icon, description);
	}

	public static bool RemoveLaunchShortcut(object name)
	{
		string text = SafeFilename(name);
		string text2 = (Utils.IS_WINDOWS ? ".lnk" : (Utils.IS_MAC ? ".command" : ".desktop"));
		string path = PPath.Abs(PPath.Join(DesktopDir(), text + text2));
		if (File.Exists(path))
		{
			File.Delete(path);
			return true;
		}
		return false;
	}
}
