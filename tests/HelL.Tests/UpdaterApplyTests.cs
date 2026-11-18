using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public class UpdaterApplyTests
{
	[Fact]
	public void ScriptWaitsAndRestartsOnlyAfterReplacement()
	{
		string script = Updater.BuildApplyScript(@"C:\Users\张三\cache\update.bin", @"D:\游戏\HelL.exe", 1234, restart: true);
		Assert.Contains("$parentId = 1234", script);
		Assert.Contains("Wait-Process -Id $parentId", script);
		Assert.Contains("$package = 'C:\\Users\\张三\\cache\\update.bin'", script);
		Assert.Contains("[IO.File]::Replace($staged, $exe, $backup)", script);
		Assert.Contains("Start-Process -FilePath $exe", script);
	}

	[Fact]
	public void ScriptEscapesQuotesAndCanSkipRestart()
	{
		string script = Updater.BuildApplyScript(@"C:\a'b\x.bin", @"C:\it's\HelL.exe", 1, restart: false);
		Assert.Contains(@"'C:\a''b\x.bin'", script);
		Assert.Contains(@"'C:\it''s\HelL.exe'", script);
		Assert.DoesNotContain("Start-Process", script);
	}

	[Fact]
	public void ScriptRejectsReplacingThePackageItself()
	{
		Assert.Throws<ArgumentException>(() => Updater.BuildApplyScript(@"C:\update.exe", @"C:\update.exe", 1, false));
	}

	[Fact]
	public void ApplyReplacesTheLauncherAndKeepsItsBackup()
	{
		if (!OperatingSystem.IsWindows())
			return;

		using TestHome home = new TestHome();
		string folder = Path.Combine(home.Dir, "中文 空格's [literal] __PACKAGE__");
		Directory.CreateDirectory(folder);
		string exe = Path.Combine(folder, "HelL.exe");
		string package = Path.Combine(folder, "update.bin");
		File.WriteAllText(exe, "previous version");
		File.WriteAllText(package, "new version");

		string script = Updater.BuildApplyScript(package, exe, int.MaxValue, false, Utils.Sha256File(package));
		var applied = RunScript(script);
		Assert.True(applied.ExitCode == 0, $"Exit {applied.ExitCode}: {applied.Output}");
		Assert.Equal("new version", File.ReadAllText(exe));
		Assert.Equal("previous version", File.ReadAllText(exe + ".bak"));
		Assert.False(File.Exists(package));
		Assert.Empty(Directory.GetFiles(folder, "*.new"));

		File.WriteAllText(package, "next version");
		applied = RunScript(Updater.BuildApplyScript(package, exe, int.MaxValue, false));
		Assert.True(applied.ExitCode == 0, $"Exit {applied.ExitCode}: {applied.Output}");
		Assert.Equal("next version", File.ReadAllText(exe));
		Assert.Equal("new version", File.ReadAllText(exe + ".bak"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InvalidPackageKeepsTheLauncherAndDoesNotRestart(bool missing)
	{
		if (!OperatingSystem.IsWindows())
			return;

		using TestHome home = new TestHome();
		string marker = Path.Combine(home.Dir, "restarted.txt");
		string exe = Path.Combine(home.Dir, "launcher.cmd");
		string package = Path.Combine(home.Dir, "update.bin");
		string launcher = "@echo restarted>\"" + marker + "\"\r\n";
		File.WriteAllText(exe, launcher);
		if (!missing)
			File.WriteAllText(package, "corrupted update");

		var applied = RunScript(Updater.BuildApplyScript(package, exe, int.MaxValue, true, new string('0', 64)));
		Assert.NotEqual(0, applied.ExitCode);
		Assert.Equal(launcher, File.ReadAllText(exe));
		Assert.False(File.Exists(marker));
		Assert.False(File.Exists(exe + ".bak"));
		Assert.Equal(!missing, File.Exists(package));
		Assert.True(File.Exists(package + ".apply.log"), $"Exit {applied.ExitCode}: {applied.Output}");
		Assert.Contains(missing ? "Update package is missing." : "SHA-256 verification failed", File.ReadAllText(package + ".apply.log"));
		Assert.Empty(Directory.GetFiles(home.Dir, "*.new"));
	}

	[Fact]
	public void LockedLauncherSurvivesAReplacementFailure()
	{
		if (!OperatingSystem.IsWindows())
			return;

		using TestHome home = new TestHome();
		string exe = Path.Combine(home.Dir, "HelL.exe");
		string package = Path.Combine(home.Dir, "update.bin");
		File.WriteAllText(exe, "working launcher");
		File.WriteAllText(package, "new version");

		using (File.Open(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			var applied = RunScript(Updater.BuildApplyScript(package, exe, int.MaxValue, false));
			Assert.NotEqual(0, applied.ExitCode);
			Assert.True(File.Exists(package + ".apply.log"), $"Exit {applied.ExitCode}: {applied.Output}");
			Assert.Contains("System.IO.IOException", File.ReadAllText(package + ".apply.log"));
		}
		Assert.Equal("working launcher", File.ReadAllText(exe));
		Assert.Equal("new version", File.ReadAllText(package));
		Assert.Empty(Directory.GetFiles(home.Dir, "*.new"));
	}

	private static (int ExitCode, string Output) RunScript(string script)
	{
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		using Process process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		}) ?? throw new InvalidOperationException("Could not start the update script.");
		var output = process.StandardOutput.ReadToEndAsync();
		var errors = process.StandardError.ReadToEndAsync();
		if (!process.WaitForExit(35000))
		{
			process.Kill(entireProcessTree: true);
			throw new TimeoutException("The update script did not finish within 35 seconds.");
		}
		return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
	}
}
