using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase8AdvancedIntegrationTests
{
    // --- 1. CurseForge 整合包规范 (manifest.json) 解析与互转 ---

    [Fact]
    public void CurseForgeManifest_ParsesAndValidatesMetadata()
    {
        string sampleJson = """
        {
          "minecraft": {
            "version": "1.20.1",
            "modLoaders": [
              {
                "id": "forge-47.2.0",
                "primary": true
              }
            ]
          },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "Super Adventure Pack",
          "version": "2.0.0",
          "author": "ExplorerDev",
          "files": [
            {
              "projectID": 238222,
              "fileID": 4829102,
              "required": true
            },
            {
              "projectID": 312019,
              "fileID": 4901234,
              "required": false
            }
          ],
          "overrides": "overrides"
        }
        """;

        var manifest = CurseForgeManifestHelper.Parse(sampleJson);

        Assert.NotNull(manifest);
        Assert.True(manifest.IsValid);
        Assert.Equal("1.20.1", manifest.GameVersion);
        Assert.Equal("forge", manifest.PrimaryLoaderType);
        Assert.Equal("47.2.0", manifest.PrimaryLoaderVersion);
        Assert.Equal("Super Adventure Pack", manifest.Name);
        Assert.Equal("2.0.0", manifest.Version);
        Assert.Equal("ExplorerDev", manifest.Author);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(238222, manifest.Files[0].ProjectId);
        Assert.Equal(4829102, manifest.Files[0].FileId);
        Assert.True(manifest.Files[0].Required);
        Assert.False(manifest.Files[1].Required);
    }

    [Fact]
    public void CurseForgeManifest_GeneratesValidJson()
    {
        var manifest = new CurseForgeModpackManifest
        {
            Name = "My Custom Pack",
            Version = "1.0.0",
            Author = "HelL Player",
            GameVersion = "1.20.1",
            PrimaryLoaderType = "fabric",
            PrimaryLoaderVersion = "0.15.7",
            Files = new List<CurseForgeModpackFile>
            {
                new CurseForgeModpackFile(306612, 4567890, true)
            }
        };

        string json = CurseForgeManifestHelper.Generate(manifest);
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("minecraftModpack", root.GetProperty("manifestType").GetString());
        Assert.Equal(1, root.GetProperty("manifestVersion").GetInt32());
        Assert.Equal("My Custom Pack", root.GetProperty("name").GetString());
        Assert.Equal("1.20.1", root.GetProperty("minecraft").GetProperty("version").GetString());
        Assert.Equal("fabric-0.15.7", root.GetProperty("minecraft").GetProperty("modLoaders")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void CurseForgeManifest_RejectsInvalidJsonGracefully()
    {
        var manifest = CurseForgeManifestHelper.Parse("{ \"invalid\": true }");
        Assert.NotNull(manifest);
        Assert.False(manifest.IsValid);
        Assert.Equal(string.Empty, manifest.GameVersion);
    }

    // --- 2. 局域网组播嗅探与报文解析 (LAN Discovery Parser) ---

    [Theory]
    [InlineData("[MOTD]Alex's §6Awesome World[/MOTD][AD]25565[/AD]", "Alex's §6Awesome World", "Alex's Awesome World", 25565, true)]
    [InlineData("[MOTD]Vanilla Server[/MOTD][AD]54321[/AD]", "Vanilla Server", "Vanilla Server", 54321, true)]
    [InlineData("[MOTD]No Port In This Header[/MOTD]", "", "", 0, false)]
    [InlineData("[MOTD]Corrupt Port[/MOTD][AD]999999[/AD]", "", "", 0, false)]
    [InlineData("Random junk string from network", "", "", 0, false)]
    public void LanDiscoveryParser_ParsesMotdAndPort(
        string rawPacket, string expectedRawMotd, string expectedCleanMotd, int expectedPort, bool expectedValid)
    {
        var info = LanDiscoveryParser.Parse(rawPacket);
        Assert.Equal(expectedValid, info.IsValid);
        if (expectedValid)
        {
            Assert.Equal(expectedRawMotd, info.RawMotd);
            Assert.Equal(expectedCleanMotd, info.CleanMotd);
            Assert.Equal(expectedPort, info.Port);
        }
    }

    // --- 3. 设置项统一变更脏标记与自动保存控制器 ---

    [Fact]
    public void SettingsAutoSaveController_TracksDirtyAndDebounces()
    {
        int saveCount = 0;
        var controller = new SettingsAutoSaveController(
            onSaveAction: () => { Interlocked.Increment(ref saveCount); },
            debounceDelayMs: 50
        );

        Assert.Equal(SettingsSaveStatus.Clean, controller.Status);
        Assert.False(controller.IsDirty);

        // 标记为脏
        controller.MarkDirty();
        Assert.Equal(SettingsSaveStatus.Pending, controller.Status);
        Assert.True(controller.IsDirty);

        // 多次标记不应重复触发
        controller.MarkDirty();
        controller.MarkDirty();

        // 立即执行同步保存 (Flush)
        controller.FlushSave();

        Assert.Equal(1, saveCount);
        Assert.Equal(SettingsSaveStatus.Clean, controller.Status);
        Assert.False(controller.IsDirty);
    }

    // --- 4. 安全文件操作与抗杀毒软件加锁重试器 ---

    [Fact]
    public void SafeFileOperationHelper_RetriesOnLockedFileAndSucceeds()
    {
        int attempts = 0;
        var result = SafeFileOperationHelper.ExecuteWithRetry(() =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }
            return "SUCCESS";
        }, maxRetries: 4, initialDelayMs: 10);

        Assert.Equal("SUCCESS", result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void SafeFileOperationHelper_AtomicWritePreservesOriginalOnFailure()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HelL_SafeFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string targetFile = Path.Combine(tempDir, "config.json");

        try
        {
            File.WriteAllText(targetFile, "ORIGINAL CONTENT");

            // 模拟写入失败
            Assert.Throws<InvalidOperationException>(() =>
            {
                SafeFileOperationHelper.AtomicWrite(targetFile, stream =>
                {
                    byte[] part = System.Text.Encoding.UTF8.GetBytes("NEW PARTIAL DATA");
                    stream.Write(part, 0, part.Length);
                    throw new InvalidOperationException("Simulation write crash!");
                });
            });

            // 验证原始文件未受损
            string content = File.ReadAllText(targetFile);
            Assert.Equal("ORIGINAL CONTENT", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SafeFileOperationHelper_AtomicWriteOverwritesSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "HelL_SafeFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string targetFile = Path.Combine(tempDir, "config.json");

        try
        {
            File.WriteAllText(targetFile, "ORIGINAL CONTENT");

            SafeFileOperationHelper.AtomicWrite(targetFile, stream =>
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes("NEW COMPLETE DATA");
                stream.Write(data, 0, data.Length);
            });

            string content = File.ReadAllText(targetFile);
            Assert.Equal("NEW COMPLETE DATA", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
