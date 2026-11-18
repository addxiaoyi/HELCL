#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace HelL.Portable;

public sealed record PortableLaunchOptions(int MemoryMb = 2048, int Width = 960, int Height = 540)
{
    public PortableLaunchOptions Validate()
    {
        if (MemoryMb is < 512 or > 262144)
            throw new ArgumentOutOfRangeException(nameof(MemoryMb), "游戏内存应为 512–262144 MB。");
        if (Width is < 320 or > 16384 || Height is < 240 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(Width), "游戏窗口宽度应为 320–16384，高度应为 240–16384。");
        return this;
    }

    internal static PortableLaunchOptions Load(string path)
    {
        if (!File.Exists(path)) return new PortableLaunchOptions();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("启动设置文件过大，请备份后修复。");
        try
        {
            var saved = JsonSerializer.Deserialize<SavedOptions>(File.ReadAllText(path));
            if (saved is not { Schema: 1, Options: not null }) throw new InvalidDataException("启动设置格式不受支持。");
            return saved.Options.Validate();
        }
        catch (Exception error) when (error is JsonException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("启动设置已损坏，请检查内存和窗口尺寸。", error);
        }
    }

    internal void Save(string path)
    {
        Validate();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new SavedOptions(1, this)));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record SavedOptions(int Schema, PortableLaunchOptions? Options);
}
