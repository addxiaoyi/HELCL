using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HelL.Services;

public sealed class ThrottledLogBuffer
{
    private readonly int _maxLines;
    private readonly Queue<string> _lines = new Queue<string>();
    private readonly List<string> _pendingBatch = new List<string>();
    private readonly object _lock = new object();

    public ThrottledLogBuffer(int maxLines = 1000)
    {
        _maxLines = Math.Max(10, maxLines);
    }

    public void EnqueueLog(string line)
    {
        if (line == null) return;

        lock (_lock)
        {
            _lines.Enqueue(line);
            _pendingBatch.Add(line);

            while (_lines.Count > _maxLines)
            {
                _lines.Dequeue();
            }
        }
    }

    public string FlushBatch()
    {
        lock (_lock)
        {
            if (_pendingBatch.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _pendingBatch.Count; i++)
            {
                sb.AppendLine(_pendingBatch[i]);
            }
            _pendingBatch.Clear();
            return sb.ToString();
        }
    }

    public IReadOnlyList<string> GetSnapshot()
    {
        lock (_lock)
        {
            return _lines.ToList();
        }
    }
}

public readonly struct CropRect
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public CropRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

public static class PerformanceOptimizerUtils
{
    public static (int minMb, int maxMb) CalculateAdaptiveMemory(
        int totalMb, int availableMb, int modCount, bool hasShaders)
    {
        // 4G 及以下低配环境
        if (totalMb <= 4096)
        {
            return (1536, 2048);
        }

        // 8G 机器：兼顾多模组运行且不超载物理空闲
        if (totalMb <= 8192)
        {
            if (availableMb <= 3500 && (modCount >= 100 || hasShaders))
            {
                return (3072, 3840);
            }
            return (3072, 4096);
        }

        // 16G 及以上环境
        if (totalMb >= 16384)
        {
            if (modCount <= 30 && !hasShaders)
            {
                return (4096, 6144);
            }
            if (modCount >= 200 || hasShaders)
            {
                return (8192, 10240);
            }
            return (6144, 8192);
        }

        return (4096, 6144);
    }

    public static CropRect GetSkinHeadCropRect(int skinWidth, int skinHeight)
    {
        // Minecraft 标准皮肤 64x64/64x32 中，头部前脸的坐标为 (8, 8)，尺寸为 8x8
        int scale = Math.Max(1, skinWidth / 64);
        return new CropRect(8 * scale, 8 * scale, 8 * scale, 8 * scale);
    }
}
