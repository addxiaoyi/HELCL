using Avalonia;
using SkiaSharp;

namespace HelL.Desktop.Services;

internal static class ImageBounds
{
    internal const long PreviewPixels = 16_000_000;
    internal const long CoverPixels = 64_000_000;
    internal const int MaxDimension = 16_384;
    internal const int MaxEncodedBytes = 32 * 1024 * 1024;

    internal static PixelSize Read(byte[] encoded, long pixelLimit)
    {
        if (encoded.Length == 0 || encoded.Length > MaxEncodedBytes)
            throw new InvalidDataException("图片文件大小不受支持。");

        // Read codec metadata before Avalonia allocates the decoded pixel buffer.
        using var stream = new MemoryStream(encoded, false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("图片格式无效或文件已损坏。");
        var size = new PixelSize(codec.Info.Width, codec.Info.Height);
        Require(size, pixelLimit);
        return size;
    }

    internal static void Require(PixelSize size, long pixelLimit)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > MaxDimension || size.Height > MaxDimension ||
            (long)size.Width * size.Height > pixelLimit)
            throw new InvalidDataException("图片尺寸过大或无效，请选择尺寸较小的图片。");
    }
}
