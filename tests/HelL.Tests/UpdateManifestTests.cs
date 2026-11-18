using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using Xunit;

namespace HelL.Tests;

public class UpdateManifestTests
{
	private const string FutureVersion = "9999.0.0";
	private static readonly string Hash = new string('a', 64);

	[Theory]
	[InlineData("1.0.0", "1", false)]
	[InlineData("1.0.0.0", "1.0.0", false)]
	[InlineData("v2.0.0", "1.99.99", true)]
	[InlineData("2.0.0-beta.1", "2.0.0", false)]
	[InlineData("2.0.0", "2.0.0-rc.1", true)]
	[InlineData("2.0.0-beta.10", "2.0.0-beta.2", true)]
	[InlineData("2.0.0-beta", "2.0.0-alpha", true)]
	[InlineData("2.0.0-1", "2.0.0-alpha", false)]
	[InlineData("2.0.0-beta.1", "2.0.0-beta", true)]
	[InlineData("2.0.0+build.99", "2.0.0+build.1", false)]
	[InlineData("2.0.0.1", "2.0.0", true)]
	public void VersionsCompareReleaseAndPrerelease(string remote, string local, bool newer)
	{
		Assert.Equal(newer, Updater.Newer(remote, local));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("release")]
	[InlineData("../../9999.0")]
	[InlineData("9999.0/../../escape")]
	[InlineData("999999999999999999999999999999")]
	[InlineData("1.2.3.4.5")]
	[InlineData("1..2")]
	public void InvalidVersionsFailWithoutThrowing(string version)
	{
		Assert.False(Updater.Newer(version));
		PyDict check = Updater.EvaluateManifest(PyDict.Of("version", version));
		Assert.False(Py.Truthy(check["ok"]));
		Assert.False(Py.Truthy(check["has_update"]));
		Assert.NotEmpty(Py.S(check["message"]));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("not an object")]
	[InlineData(42)]
	public void InvalidManifestShapeIsAnExplicitFailure(object manifest)
	{
		PyDict check = Updater.EvaluateManifest(manifest);
		Assert.False(Py.Truthy(check["ok"]));
		Assert.Equal(AppInfo.APP_VERSION, check["current"]);
		Assert.False(Py.Truthy(check["has_update"]));
	}

	[Fact]
	public void ArrayManifestIsRejected()
	{
		Assert.False(Py.Truthy(Updater.EvaluateManifest(new List<object>())["ok"]));
	}

	[Theory]
	[InlineData("")]
	[InlineData("https://")]
	[InlineData("file:///C:/update.exe")]
	[InlineData("http://example.com/update.exe")]
	[InlineData("https://user:password@example.com/update.exe")]
	[InlineData("/update.exe")]
	public void UnsafeDownloadAddressesAreRejected(string url)
	{
		PyDict check = Updater.EvaluateManifest(PyDict.Of("version", FutureVersion, "sha256", Hash, "url", url));
		Assert.False(Py.Truthy(check["ok"]));
		Assert.False(Py.Truthy(check["has_update"]));
		Assert.Throws<RuntimeError>(() => Updater.Download(PyDict.Of("url", url, "sha256", Hash)));
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
	public void MissingOrInvalidHashCannotStartAnUpdate(string hash)
	{
		PyDict check = Updater.EvaluateManifest(PyDict.Of("version", FutureVersion, "url", "https://example.com/update.exe", "sha256", hash));
		Assert.False(Py.Truthy(check["ok"]));
		Assert.False(Py.Truthy(check["has_update"]));
		Assert.Throws<RuntimeError>(() => Updater.Download(PyDict.Of("url", "https://example.com/update.exe", "sha256", hash)));
	}

	[Fact]
	public void LegacyManifestFieldNamesRemainSupported()
	{
		PyDict check = Updater.EvaluateManifest(PyDict.Of("latest", FutureVersion, "download", "https://example.com/update.exe",
			"changelog", "修复启动问题", "sha256", Hash.ToUpperInvariant()));
		Assert.True(Py.Truthy(check["ok"]));
		Assert.True(Py.Truthy(check["has_update"]));
		Assert.Equal("修复启动问题", check["notes"]);
		Assert.Equal(Hash, check["sha256"]);
	}

	[Fact]
	public void LoopbackHttpSupportsLocalUpdateTesting()
	{
		PyDict check = Updater.EvaluateManifest(PyDict.Of("version", FutureVersion, "url", "http://127.0.0.1:34567/update.exe", "sha256", Hash));
		Assert.True(Py.Truthy(check["ok"]));
		Assert.True(Py.Truthy(check["has_update"]));
	}

	[Fact]
	public void OlderVersionDoesNotRequireADownload()
	{
		PyDict check = Updater.EvaluateManifest(PyDict.Of("version", "0.0.0"));
		Assert.True(Py.Truthy(check["ok"]));
		Assert.False(Py.Truthy(check["has_update"]));
	}

	[Fact]
	public void VersionComesFromTheBuiltAssembly()
	{
		string version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
		Assert.Equal(version, AppInfo.APP_VERSION);
		Assert.Equal(version, AppInfo.LAUNCHER_VERSION);
		Assert.Equal(version, AppInfo.__version__);
		Assert.Equal("HelL/" + version, AppInfo.CLIENT_HEADER);
		Assert.False(Updater.Newer(version));
	}

	[Fact]
	public void FailedCheckReachesTheUiAsAFailure()
	{
		UpdateInfo info = JsonSerializer.Deserialize<UpdateInfo>(
			"{\"ok\":false,\"has_update\":false,\"message\":\"更新清单缺少有效 SHA-256\"}",
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
		Assert.False(info.Ok);
		Assert.False(info.HasUpdate);
		Assert.Equal("更新清单缺少有效 SHA-256", info.Message);
	}

	[Fact]
	public void ApplyRejectsAPackageChangedAfterDownload()
	{
		using TestHome home = new TestHome();
		string package = Path.Combine(home.Dir, "HelL-" + Hash + ".bin");
		File.WriteAllText(package, "changed after download");
		RuntimeError error = Assert.Throws<RuntimeError>(() => Updater.ApplyStaged(package, false));
		Assert.Contains("SHA-256", error.Message);
		Assert.True(File.Exists(package));
	}

	[Fact]
	public void DownloadCacheUsesTheHashInsteadOfManifestText()
	{
		using TestHome home = new TestHome();
		string previousRoot = Utils.ROOT;
		try
		{
			Utils.SetRoot(home.Dir);
			Config.Reset();
			string cache = Path.Combine(home.Dir, "cache");
			Directory.CreateDirectory(cache);
			string sample = Path.Combine(home.Dir, "sample.bin");
			File.WriteAllText(sample, "verified update");
			string hash = Utils.Sha256File(sample);
			string package = Path.Combine(cache, "HelL-" + hash + ".bin");
			File.Move(sample, package);

			string downloaded = Updater.Download(PyDict.Of("url", "https://example.invalid/update.exe",
				"sha256", hash, "latest", "../../escape"));
			Assert.Equal(Path.GetFullPath(package), Path.GetFullPath(downloaded));
			Assert.Equal("verified update", File.ReadAllText(downloaded));
			Assert.Single(Directory.GetFiles(cache));
		}
		finally
		{
			Utils.SetRoot(previousRoot);
			Config.Reset();
		}
	}
}
