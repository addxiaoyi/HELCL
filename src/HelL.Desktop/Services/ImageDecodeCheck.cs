using HelL.Desktop.Controls;
using SkiaSharp;

namespace HelL.Desktop.Services;

internal static class ImageDecodeCheck
{
    internal static void Run()
    {
        var codec = new DesktopImages();
        byte[] portrait = Png(32, 1024);
        using (var image = codec.Decode(portrait))
        {
            if (image.Width != 32 || image.Height != 1024)
                throw new InvalidOperationException("图片解码没有保留有效图片尺寸。");
            using var resized = image.Resize(16, 512);
            using var reopened = codec.Decode(resized.EncodePng());
            if (reopened.Width != 16 || reopened.Height != 512)
                throw new InvalidOperationException("封面缩放后无法重新读取。");
        }
        using (var preview = ProjectPicture.DecodePreview(portrait, 320))
        {
            if (preview.PixelSize.Height != 320 || preview.PixelSize.Width > 320)
                throw new InvalidOperationException("竖向缩略图超出宽高限制。");
        }
        using (var preview = ProjectPicture.DecodePreview(Png(1024, 32), 320))
        {
            if (preview.PixelSize.Width != 320 || preview.PixelSize.Height > 320)
                throw new InvalidOperationException("横向缩略图超出宽高限制。");
        }

        byte[] tooTall = Png(1, ImageBounds.MaxDimension + 1);
        MustReject(() => codec.Decode(tooTall).Dispose());
        MustReject(() => ProjectPicture.DecodePreview(tooTall, 320).Dispose());

        // A small compressed image can claim a huge canvas before any pixels are decoded.
        byte[] oversizedCanvas = OversizedPng();
        MustReject(() => codec.Decode(oversizedCanvas).Dispose());
        MustReject(() => ProjectPicture.DecodePreview(oversizedCanvas, 320).Dispose());
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("无法创建图片自检样本。");
        return encoded.ToArray();
    }

    private static void MustReject(Action decode)
    {
        try { decode(); }
        catch (InvalidDataException error) when (error.Message.Contains("图片尺寸", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("图片解码未在分配像素缓冲前拒绝超大尺寸。");
    }

    private static byte[] OversizedPng()
    {
        byte[] encoded = Png(1, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(16, 4), 8192);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(20, 4), 8192);
        uint checksum = uint.MaxValue;
        foreach (byte value in encoded.AsSpan(12, 17))
        {
            checksum ^= value;
            for (int bit = 0; bit < 8; bit++)
                checksum = (checksum >> 1) ^ ((checksum & 1) == 0 ? 0 : 0xEDB88320U);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(29, 4), ~checksum);
        return encoded;
    }
}
