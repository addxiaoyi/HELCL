using Avalonia;
using Avalonia.Media.Imaging;
using HelL.Core.Platform;

namespace HelL.Desktop.Services;

internal sealed class DesktopImages : IImageCodec
{
    public IDecodedImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        return new Decoded(new Bitmap(stream));
    }

    private sealed class Decoded(Bitmap bitmap) : IDecodedImage
    {
        public int Width => bitmap.PixelSize.Width;
        public int Height => bitmap.PixelSize.Height;
        public bool HasAlpha => true;
        public IDecodedImage Resize(int width, int height) => new Decoded(bitmap.CreateScaledBitmap(new PixelSize(width, height), BitmapInterpolationMode.HighQuality));
        public byte[] EncodePng()
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }
        public byte[] EncodeJpeg(int quality)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, new JpegBitmapEncoderOptions { Quality = Math.Clamp(quality, 1, 100) });
            return stream.ToArray();
        }
        public void Dispose() => bitmap.Dispose();
    }
}
