using System.IO;
using System.IO.Compression;
using HelL.Core.Launcher.Sync;
using Xunit;

namespace HelL.Tests;

public class SaveSyncTests
{
	[Fact]
	public void LevelDatReadsOnlyDataFields()
	{
		LevelDat ld = LevelDat.Read(WorldFixture.LevelDat("我的世界", 1729770000123L, 3465, "1.20.1"));
		Assert.NotNull(ld);
		Assert.Equal("我的世界", ld.LevelName);
		Assert.Equal(1729770000123L, ld.LastPlayed);
		Assert.Equal(3465, ld.DataVersion);
		Assert.Equal("1.20.1", ld.VersionName);
		Assert.Null(LevelDat.Read(new byte[] { 1, 2, 3 }));
	}

	[Theory]
	[InlineData("a", "a", "x", SyncDecision.Same)]
	[InlineData("b", "a", "a", SyncDecision.Push)]
	[InlineData("a", "b", "a", SyncDecision.Pull)]
	[InlineData("b", "c", "a", SyncDecision.Conflict)]
	[InlineData("b", "c", null, SyncDecision.Conflict)]
	[InlineData("a", null, "a", SyncDecision.OnlyLocal)]
	[InlineData(null, "a", null, SyncDecision.OnlyRemote)]
	[InlineData(null, null, null, SyncDecision.Same)]
	public void Decide(string local, string remote, string baseHash, SyncDecision expected)
	{
		Assert.Equal(expected, SaveSync.Decide(local, remote, baseHash));
	}

	[Fact]
	public void HashIgnoresSessionLockAndTracksContent()
	{
		using TestHome home = new TestHome();
		string w = WorldFixture.Create(home.Dir, "W");
		string h1 = SaveSync.WorldHash(w);
		Assert.Matches("^[0-9a-f]{64}$", h1);
		File.WriteAllText(Path.Combine(w, "session.lock"), "changed");
		Assert.Equal(h1, SaveSync.WorldHash(w));
		File.WriteAllText(Path.Combine(w, "region", "r.0.0.mca"), "region-data-1");
		Assert.NotEqual(h1, SaveSync.WorldHash(w));
	}

	[Fact]
	public void ManifestFormatIsStable()
	{
		using TestHome home = new TestHome();
		string w = Path.Combine(home.Dir, "M");
		Directory.CreateDirectory(Path.Combine(w, "b"));
		File.WriteAllText(Path.Combine(w, "a.txt"), "x");
		File.WriteAllText(Path.Combine(w, "b", "c.txt"), "");
		File.WriteAllText(Path.Combine(w, "B.txt"), "y");
		string expected = "B.txt\t1\ta1fce4363854ff888cff4b8e7875d600c2682390412a8cf79b37d0b11148b0fa\n" + "a.txt\t1\t2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\n" + "b/c.txt\t0\te3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n";
		Assert.Equal(expected, SaveSync.Manifest(w));
		Assert.Equal(SaveSync.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(expected)), SaveSync.WorldHash(w));
	}

	[Theory]
	[InlineData("新的世界", true)]
	[InlineData("World 1", true)]
	[InlineData("../x", false)]
	[InlineData("a/b", false)]
	[InlineData(".hidden", false)]
	[InlineData(" pad", false)]
	[InlineData("", false)]
	public void SafeWorldName(string name, bool ok)
	{
		Assert.Equal(ok, SaveSync.SafeWorldName(name));
	}

	[Fact]
	public void ExportZipRoundTripsAndChecks()
	{
		using TestHome home = new TestHome();
		string w = WorldFixture.Create(home.Dir, "Z");
		string zip = Path.Combine(home.Dir, "z.zip");
		using (FileStream fs = File.Create(zip))
		{
			SaveSync.ExportZip(w, fs);
		}
		Assert.Null(SaveSync.CheckZip(zip));
		using (ZipArchive za = ZipFile.OpenRead(zip))
		{
			Assert.Contains(za.Entries, e => e.FullName == "Z/level.dat");
			Assert.DoesNotContain(za.Entries, e => e.FullName.EndsWith("session.lock"));
		}
		string bad = Path.Combine(home.Dir, "bad.zip");
		using (ZipArchive za = ZipFile.Open(bad, ZipArchiveMode.Create))
		{
			za.CreateEntry("readme.txt");
		}
		Assert.NotNull(SaveSync.CheckZip(bad));
		File.WriteAllText(Path.Combine(home.Dir, "nz.zip"), "not zip");
		Assert.NotNull(SaveSync.CheckZip(Path.Combine(home.Dir, "nz.zip")));
	}
}
