using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase10DeepEnhancementTests
{
    // ==========================================
    // 1. Modrinth (.mrpack) index.json 解析与验证
    // ==========================================

    [Fact]
    public void ModrinthIndex_ParsesValidManifestAndFiles()
    {
        string sampleJson = """
        {
          "formatVersion": 1,
          "game": "minecraft",
          "versionId": "1.0.0-release",
          "name": "Fabulously Optimized",
          "summary": "A simple Minecraft modpack focusing on performance.",
          "dependencies": {
            "minecraft": "1.20.1",
            "fabric-loader": "0.15.7"
          },
          "files": [
            {
              "path": "mods/sodium-fabric-0.5.8+mc1.20.1.jar",
              "hashes": {
                "sha1": "2aae6c35c94fcfb415dbe95f408b9ce91ee846ed",
                "sha512": "b613c015b6379566367375fa8b301c238bfa79f6..."
              },
              "env": {
                "client": "required",
                "server": "unsupported"
              },
              "downloads": [
                "https://cdn.modrinth.com/data/AANobbMI/versions/xxxx/sodium-fabric-0.5.8%2Bmc1.20.1.jar"
              ],
              "fileSize": 1234567
            },
            {
              "path": "mods/lithium-fabric-0.11.2+mc1.20.1.jar",
              "hashes": {
                "sha1": "1111111111111111111111111111111111111111"
              },
              "downloads": [
                "https://cdn.modrinth.com/data/lithium/lithium.jar"
              ],
              "fileSize": 654321
            }
          ]
        }
        """;

        var index = ModrinthIndexHelper.Parse(sampleJson);

        Assert.NotNull(index);
        Assert.True(index.IsValid);
        Assert.Equal("minecraft", index.Game);
        Assert.Equal("1.0.0-release", index.VersionId);
        Assert.Equal("Fabulously Optimized", index.Name);
        Assert.Equal("A simple Minecraft modpack focusing on performance.", index.Summary);
        Assert.Equal("1.20.1", index.GameVersion);
        Assert.Equal("fabric-loader", index.PrimaryLoaderType);
        Assert.Equal("0.15.7", index.PrimaryLoaderVersion);
        Assert.Equal(2, index.Files.Count);

        var first = index.Files[0];
        Assert.Equal("mods/sodium-fabric-0.5.8+mc1.20.1.jar", first.Path);
        Assert.Equal("2aae6c35c94fcfb415dbe95f408b9ce91ee846ed", first.Sha1);
        Assert.Equal(1234567, first.FileSize);
        Assert.Single(first.Downloads);
        Assert.StartsWith("https://cdn.modrinth.com/", first.Downloads[0]);
    }

    [Fact]
    public void ModrinthIndex_RejectsCorruptOrIncompleteJson()
    {
        var emptyIndex = ModrinthIndexHelper.Parse("{}");
        Assert.NotNull(emptyIndex);
        Assert.False(emptyIndex.IsValid);

        var badJson = ModrinthIndexHelper.Parse("not-a-valid-json");
        Assert.NotNull(badJson);
        Assert.False(badJson.IsValid);
    }

    // ==========================================
    // 2. 高级崩溃日志诊断 (Advanced CrashLogExpert)
    // ==========================================

    [Fact]
    public void CrashLogExpert_IdentifiesMixinInjectionFailure()
    {
        string log = """
        [12:00:00] [main/ERROR]: Minecraft has crashed!
        org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: An unexpected critical error was encountered
        Caused by: org.spongepowered.asm.mixin.injection.throwables.InjectionError: Critical injection failure: @Inject annotation on onRender could not find any targets matching 'render' in net.minecraft.client.gui.screen.TitleScreen.
        at org.spongepowered.asm.mixin.transformer.MixinTargetContext.transformMethod(MixinTargetContext.java:500)
        [sodium.mixins.core.json:features.gui.TitleScreenMixin]
        """;

        var result = CrashLogExpert.Analyze(log, exitCode: 1);

        Assert.True(result.IsIdentified);
        Assert.Equal(CrashIssueType.MixinFailure, result.IssueType);
        Assert.Contains("Mixin", result.Reason);
        Assert.Contains("TitleScreen", result.Reason);
        Assert.Contains("sodium", result.SuspectedMod, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Recommendation);
        Assert.NotEmpty(result.Recommendation);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesDuplicateMods()
    {
        string log = """
        net.fabricmc.loader.impl.FormattedException: Duplicate versions for mod ID 'cloth-config':
        - 11.1.118 at mods/cloth-config-11.1.118-fabric.jar
        - 11.1.106 at mods/cloth-config-11.1.106-fabric.jar
        Please remove one of them!
        """;

        var result = CrashLogExpert.Analyze(log, exitCode: 1);

        Assert.True(result.IsIdentified);
        Assert.Equal(CrashIssueType.DuplicateMods, result.IssueType);
        Assert.Contains("cloth-config", result.SuspectedMod);
        Assert.Contains("重复", result.Reason);
        Assert.Contains("删除", result.Recommendation);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesMissingClassDependency()
    {
        string log = """
        java.lang.NoClassDefFoundError: net/fabricmc/fabric/api/client/rendering/v1/HudRenderCallback
        at com.example.minimap.MinimapMod.onInitialize(MinimapMod.java:42)
        """;

        var result = CrashLogExpert.Analyze(log, exitCode: 1);

        Assert.True(result.IsIdentified);
        Assert.Equal(CrashIssueType.MissingDependency, result.IssueType);
        Assert.Contains("HudRenderCallback", result.Reason);
        Assert.Contains("前置", result.Recommendation);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesModIncompatibility()
    {
        string log = """
        [main/FATAL]: Incompatible mods found!
        net.fabricmc.loader.impl.gui.FabricStatusTree: OptiFine is incompatible with Sodium (sodium), but both are installed!
        """;

        var result = CrashLogExpert.Analyze(log, exitCode: 1);

        Assert.True(result.IsIdentified);
        Assert.Equal(CrashIssueType.ModIncompatibility, result.IssueType);
        Assert.Contains("冲突", result.Reason);
        Assert.Contains("OptiFine", result.Reason);
        Assert.Contains("Sodium", result.Reason);
    }

    // ==========================================
    // 3. 启动前多维综合体检门禁 (Launch Preflight Pipeline)
    // ==========================================

    [Fact]
    public void LaunchPreflightGate_BlocksOn32BitJavaWithOverMemory()
    {
        var request = new LaunchPreflightRequest
        {
            MinecraftVersion = "1.20.1",
            JavaPath = @"C:\Program Files (x86)\Java\jre1.8.0_351\bin\javaw.exe",
            JavaMajorVersion = 8,
            IsJava64Bit = false,
            AllocatedMemoryMb = 2048,
            SystemAvailableMemoryMb = 8192,
            InstanceDirectory = @"D:\Minecraft\.minecraft"
        };

        var report = LaunchPreflightGate.Evaluate(request);

        Assert.False(report.CanProceed);
        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Severity == PreflightSeverity.Error && i.Message.Contains("32 位"));
    }

    [Fact]
    public void LaunchPreflightGate_BlocksOnJavaVersionMismatch()
    {
        var request = new LaunchPreflightRequest
        {
            MinecraftVersion = "1.20.5",
            JavaPath = @"C:\Program Files\Java\jdk-17\bin\javaw.exe",
            JavaMajorVersion = 17,
            IsJava64Bit = true,
            AllocatedMemoryMb = 4096,
            SystemAvailableMemoryMb = 16384,
            InstanceDirectory = @"D:\Minecraft\.minecraft"
        };

        var report = LaunchPreflightGate.Evaluate(request);

        Assert.False(report.CanProceed);
        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, i => i.Severity == PreflightSeverity.Error && i.Message.Contains("Java 21"));
    }

    [Fact]
    public void LaunchPreflightGate_WarnsOnExcessiveMemoryAllocation()
    {
        var request = new LaunchPreflightRequest
        {
            MinecraftVersion = "1.20.1",
            JavaPath = @"C:\Program Files\Java\jdk-17\bin\javaw.exe",
            JavaMajorVersion = 17,
            IsJava64Bit = true,
            AllocatedMemoryMb = 7500,
            SystemAvailableMemoryMb = 8000, // 分配超 90% 可用内存
            InstanceDirectory = @"D:\Minecraft\.minecraft"
        };

        var report = LaunchPreflightGate.Evaluate(request);

        Assert.True(report.CanProceed); // Warning 不阻断
        Assert.False(report.HasErrors);
        Assert.True(report.HasWarnings);
        Assert.Contains(report.Issues, i => i.Severity == PreflightSeverity.Warning && i.Message.Contains("空闲物理内存"));
    }

    [Fact]
    public void LaunchPreflightGate_PassesHealthyEnvironment()
    {
        var request = new LaunchPreflightRequest
        {
            MinecraftVersion = "1.20.1",
            JavaPath = @"C:\Program Files\Java\jdk-17\bin\javaw.exe",
            JavaMajorVersion = 17,
            IsJava64Bit = true,
            AllocatedMemoryMb = 4096,
            SystemAvailableMemoryMb = 12000,
            InstanceDirectory = @"D:\Minecraft\.minecraft"
        };

        var report = LaunchPreflightGate.Evaluate(request);

        Assert.True(report.CanProceed);
        Assert.False(report.HasErrors);
        Assert.False(report.HasWarnings);
        Assert.Empty(report.Issues);
    }

    // ==========================================
    // 4. 分块下载色块调度矩阵 (Download Block Matrix)
    // ==========================================

    [Fact]
    public void DownloadBlockMatrix_InitializesBlocksCorrectly()
    {
        long totalBytes = 10 * 1024 * 1024; // 10MB
        long blockSize = 2 * 1024 * 1024;  // 2MB 每个分块 -> 5 个分块

        var matrix = new DownloadBlockMatrix(totalBytes, blockSize);

        Assert.Equal(5, matrix.TotalBlocks);
        Assert.Equal(0, matrix.CompletedBlocks);
        Assert.Equal(0.0, matrix.ProgressRatio);

        // 分块边界检查
        Assert.Equal(0, matrix[0].StartOffset);
        Assert.Equal(2 * 1024 * 1024 - 1, matrix[0].EndOffset);
        Assert.Equal(8 * 1024 * 1024, matrix[4].StartOffset);
        Assert.Equal(10 * 1024 * 1024 - 1, matrix[4].EndOffset);
    }

    [Fact]
    public void DownloadBlockMatrix_TransitionsStateAndAllocatesNextPending()
    {
        long totalBytes = 6 * 1024 * 1024; // 6MB
        long blockSize = 2 * 1024 * 1024;  // 3 个分块

        var matrix = new DownloadBlockMatrix(totalBytes, blockSize);

        // 获取下一个待下载块
        int next = matrix.AcquireNextPendingBlock();
        Assert.Equal(0, next);
        Assert.Equal(DownloadBlockState.Downloading, matrix[0].State);

        int next2 = matrix.AcquireNextPendingBlock();
        Assert.Equal(1, next2);
        Assert.Equal(DownloadBlockState.Downloading, matrix[1].State);

        // 完成第 0 块
        matrix.MarkBlockCompleted(0);
        Assert.Equal(DownloadBlockState.Completed, matrix[0].State);
        Assert.Equal(1, matrix.CompletedBlocks);
        Assert.InRange(matrix.ProgressRatio, 0.33, 0.34);

        // 标记第 1 块失败，应重置为 Pending 供重试
        matrix.MarkBlockFailed(1);
        Assert.Equal(DownloadBlockState.Pending, matrix[1].State);
        Assert.Equal(1, matrix[1].RetryCount);

        // 再次获取，应该重新取得第 1 块
        int retryNext = matrix.AcquireNextPendingBlock();
        Assert.Equal(1, retryNext);
    }
}
