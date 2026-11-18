using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class SaveProtectionTests : IDisposable
{
	private readonly TestHome home = new TestHome();
	private readonly Instance instance = new Instance("saves-test");
	private string SavesDir => Path.Combine(instance.path, "saves");

	public void Dispose() => home.Dispose();

	private string Archive(string name, params (string Name, byte[] Content)[] entries)
	{
		string path = Path.Combine(home.Dir, name);
		using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
		foreach (var entry in entries)
		{
			using Stream stream = zip.CreateEntry(entry.Name).Open();
			stream.Write(entry.Content);
		}
		return path;
	}

	private static byte[] Level => WorldFixture.LevelDat("Test world", 1, 3465, "1.20.1");
	private PyDict Import(string archive) => Worlds.InstallWorld(null, PyDict.Of("path", archive), instance);

	[Fact]
	public void BackupAndRestore_PreservesWorldBytesAndAvoidsOverwrite()
	{
		string world = WorldFixture.Create(SavesDir, "Original");
		PyDict backup = Saves.BackupSave(instance, "Original");
		using (ZipArchive zip = ZipFile.OpenRead((string)backup["path"]))
			Assert.DoesNotContain(zip.Entries, entry => entry.FullName.EndsWith("session.lock"));
		PyDict restored = Saves.RestoreBackup(instance, backup["name"]);
		string restoredPath = (string)restored["path"];
		Assert.NotEqual(world, restoredPath);
		Assert.Equal(File.ReadAllBytes(Path.Combine(world, "level.dat")), File.ReadAllBytes(Path.Combine(restoredPath, "level.dat")));
		Assert.Equal(File.ReadAllBytes(Path.Combine(world, "region", "r.0.0.mca")), File.ReadAllBytes(Path.Combine(restoredPath, "region", "r.0.0.mca")));
		Assert.Empty(Directory.GetDirectories(SavesDir, ".restore-*"));
	}

	[Fact]
	public void RestoreOverwrite_ReplacesOnlySelectedWorld()
	{
		string world = WorldFixture.Create(SavesDir, "Original");
		byte[] before = File.ReadAllBytes(Path.Combine(world, "level.dat"));
		PyDict backup = Saves.BackupSave(instance, "Original");
		File.WriteAllText(Path.Combine(world, "level.dat"), "changed");
		File.WriteAllText(Path.Combine(world, "later.txt"), "later");
		string neighbor = WorldFixture.Create(SavesDir, "Neighbor");
		Saves.RestoreBackup(instance, backup["name"], overwrite: true);
		Assert.Equal(before, File.ReadAllBytes(Path.Combine(world, "level.dat")));
		Assert.False(File.Exists(Path.Combine(world, "later.txt")));
		Assert.True(File.Exists(Path.Combine(neighbor, "level.dat")));
	}

	[Fact]
	public void LockedWorld_RejectsBackupAndRestore()
	{
		string world = WorldFixture.Create(SavesDir, "Locked");
		PyDict backup = Saves.BackupSave(instance, "Locked");
		using var held = new FileStream(Path.Combine(world, "session.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		Assert.Throws<SaveError>(() => Saves.BackupSave(instance, "Locked"));
		Assert.Throws<SaveError>(() => Saves.RestoreBackup(instance, backup["name"], overwrite: true));
		Assert.True(File.Exists(Path.Combine(world, "level.dat")));
		Assert.Single(Saves.ListBackups(instance));
	}

	[Fact]
	public void CancelledBackup_RemovesPartialArchive()
	{
		WorldFixture.Create(SavesDir, "World");
		Assert.Throws<OperationCanceledException>(() => Saves.BackupSave(instance, "World", on_progress: (_, _, _) => throw new OperationCanceledException()));
		Assert.Empty(Directory.GetFiles(Saves.BackupsDir(instance)));
	}

	[Fact]
	public void CancelledExport_PreservesExistingDestination()
	{
		WorldFixture.Create(SavesDir, "World");
		string export = Path.Combine(home.Dir, "export.zip");
		File.WriteAllText(export, "previous export");
		Assert.Throws<OperationCanceledException>(() => Saves.ExportSave(instance, "World", export, on_progress: (_, _, _) => throw new OperationCanceledException()));
		Assert.Equal("previous export", File.ReadAllText(export));
		Assert.Empty(Directory.GetFiles(home.Dir, "*.part"));
	}

	[Fact]
	public void ExportInsideWorld_IsRejected()
	{
		string world = WorldFixture.Create(SavesDir, "World");
		Assert.Throws<SaveError>(() => Saves.ExportSave(instance, "World", Path.Combine(world, "export.zip")));
		Assert.False(File.Exists(Path.Combine(world, "export.zip")));
	}

	[Theory]
	[InlineData("../outside.txt")]
	[InlineData("World/../../outside.txt")]
	[InlineData("/outside.txt")]
	[InlineData("C:/outside.txt")]
	[InlineData("World/level.dat:extra")]
	[InlineData("World/NUL.txt")]
	[InlineData("World/region./test")]
	[InlineData("World/LEVEL.DAT")]
	public void InvalidArchive_DoesNotInstallAnything(string unsafeName)
	{
		string archive = Archive("bad.zip", ("World/level.dat", Level), (unsafeName, new byte[] { 1 }));
		Assert.Throws<WorldError>(() => Import(archive));
		Assert.Empty(Directory.GetFileSystemEntries(SavesDir));
		Assert.False(File.Exists(Path.Combine(instance.path, "outside.txt")));
	}

	[Fact]
	public void SymlinkArchive_IsRejected()
	{
		string archive = Archive("links.zip", ("World/level.dat", Level), ("World/link", new byte[] { 1 }));
		using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
			zip.GetEntry("World/link").ExternalAttributes = unchecked((int)0xA1FF0000);
		Assert.Throws<WorldError>(() => Import(archive));
		Assert.Empty(Directory.GetFileSystemEntries(SavesDir));
	}

	[Fact]
	public void ImportMultipleWorlds_RejectsCollisionBeforeMovingAnyWorld()
	{
		string existing = WorldFixture.Create(SavesDir, "Existing");
		byte[] original = File.ReadAllBytes(Path.Combine(existing, "level.dat"));
		string archive = Archive("worlds.zip", ("New/level.dat", Level), ("Existing/level.dat", Level));
		Assert.Throws<WorldError>(() => Import(archive));
		Assert.False(Directory.Exists(Path.Combine(SavesDir, "New")));
		Assert.Equal(original, File.ReadAllBytes(Path.Combine(existing, "level.dat")));
		Assert.Single(Directory.GetDirectories(SavesDir));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void CancelledImport_RemovesStagingAndPreservesExistingWorld(bool afterLastChunk)
	{
		string existing = WorldFixture.Create(SavesDir, "Existing");
		byte[] originalLevel = File.ReadAllBytes(Path.Combine(existing, "level.dat"));
		byte[] originalRegion = File.ReadAllBytes(Path.Combine(existing, "region", "r.0.0.mca"));
		byte[] level = Level;
		byte[] region = Enumerable.Repeat((byte)42, 256 * 1024).ToArray();
		string archive = Archive("New.zip", ("New/level.dat", level), ("New/region/r.0.0.mca", region));
		byte[] originalArchive = File.ReadAllBytes(archive);
		long copied = 0;
		using var cancellation = new CancellationTokenSource();
		Assert.Throws<OperationCanceledException>(() => Worlds.InstallWorld(null, PyDict.Of("path", archive), instance,
			(_, done, total) =>
			{
				copied = done;
				if (afterLastChunk ? done == total : done > level.Length) cancellation.Cancel();
			}, token: cancellation.Token));
		Assert.True(copied > level.Length);
		if (!afterLastChunk) Assert.True(copied < level.Length + region.Length);
		Assert.False(Directory.Exists(Path.Combine(SavesDir, "New")));
		Assert.Empty(Directory.GetDirectories(SavesDir, ".import-*"));
		Assert.Equal(originalLevel, File.ReadAllBytes(Path.Combine(existing, "level.dat")));
		Assert.Equal(originalRegion, File.ReadAllBytes(Path.Combine(existing, "region", "r.0.0.mca")));
		Assert.Equal(originalArchive, File.ReadAllBytes(archive));

		Import(archive);
		Assert.Equal(region, File.ReadAllBytes(Path.Combine(SavesDir, "New", "region", "r.0.0.mca")));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Import_SupportsRootAndWrappedWorlds(bool atRoot)
	{
		string prefix = atRoot ? "" : "World/";
		string archive = Archive("World.zip", (prefix + "level.dat", Level), (prefix + "region/r.0.0.mca", new byte[] { 1, 2, 3 }));
		Import(archive);
		Assert.Equal(Level, File.ReadAllBytes(Path.Combine(SavesDir, "World", "level.dat")));
		Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(SavesDir, "World", "region", "r.0.0.mca")));
		Assert.Single(Directory.GetDirectories(SavesDir));
	}

	[Fact]
	public void InvalidRestore_PreservesOriginalWorld()
	{
		string world = WorldFixture.Create(SavesDir, "World");
		byte[] original = File.ReadAllBytes(Path.Combine(world, "level.dat"));
		string archive = Archive("broken.zip", ("World/readme.txt", new byte[] { 1 }));
		Directory.CreateDirectory(Saves.BackupsDir(instance));
		File.Copy(archive, Path.Combine(Saves.BackupsDir(instance), "broken.zip"));
		Assert.Throws<SaveError>(() => Saves.RestoreBackup(instance, "broken.zip", target_name: "World", overwrite: true));
		Assert.Equal(original, File.ReadAllBytes(Path.Combine(world, "level.dat")));
		Assert.Empty(Directory.GetDirectories(SavesDir, ".restore-*"));
	}

	[Fact]
	public void CorruptImport_LeavesNoStagingDirectory()
	{
		string archive = Path.Combine(home.Dir, "broken.zip");
		File.WriteAllText(archive, "not a zip");
		Assert.Throws<WorldError>(() => Import(archive));
		Assert.Empty(Directory.GetFileSystemEntries(SavesDir));
	}

	[Fact]
	public void DamagedLevelDat_CannotReplaceExistingWorld()
	{
		string world = WorldFixture.Create(SavesDir, "World");
		byte[] original = File.ReadAllBytes(Path.Combine(world, "level.dat"));
		string archive = Archive("damaged.zip", ("World/level.dat", new byte[] { 1, 2, 3 }));
		Assert.Throws<WorldError>(() => Import(archive));
		Directory.CreateDirectory(Saves.BackupsDir(instance));
		File.Copy(archive, Path.Combine(Saves.BackupsDir(instance), "damaged.zip"));
		Assert.Throws<SaveError>(() => Saves.RestoreBackup(instance, "damaged.zip", target_name: "World", overwrite: true));
		Assert.Equal(original, File.ReadAllBytes(Path.Combine(world, "level.dat")));
		Assert.Single(Directory.GetDirectories(SavesDir));
	}
}
