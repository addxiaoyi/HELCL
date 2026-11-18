using System;

namespace HelL.Core.Launcher;

public sealed class WallpaperCachePolicy
{
    public static WallpaperCachePolicy Default { get; } = new(512, 0);
    public int CapacityMiB { get; }
    public int KeepDays { get; }
    public long CapacityBytes => CapacityMiB * 1048576L;
    public WallpaperCachePolicy(int capacityMiB, int keepDays)
    {
        if (capacityMiB < 256 || capacityMiB > 4096) throw new ArgumentOutOfRangeException(nameof(capacityMiB), "缓存容量应为 256–4096 MiB。");
        if (keepDays < 0 || keepDays > 365) throw new ArgumentOutOfRangeException(nameof(keepDays), "保留时间应为 0–365 天。");
        CapacityMiB = capacityMiB; KeepDays = keepDays;
    }
    public bool Expired(DateTime lastUsedUtc, DateTime utcNow) => KeepDays > 0 && lastUsedUtc <= utcNow.AddDays(-KeepDays);
}
