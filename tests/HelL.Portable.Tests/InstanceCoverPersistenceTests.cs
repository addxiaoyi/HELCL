using HelL.Core.Launcher;
using HelL.Core.Platform;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class InstanceCoverPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-cover-save-" + Guid.NewGuid().ToString("N"));
    private readonly IImageCodec? previousCodec = PlatformServices.ImageCodec;
    private readonly Instance instance;
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];

    public InstanceCoverPersistenceTests()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("cover");
        instance = new Instance("cover");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void FailedMetadataSaveRestoresTheOldCover(bool corrupt, bool hadCover)
    {
        string metadata = Path.Combine(instance.path, ".instance.json");
        string cover = Path.Combine(instance.path, ".hell-cover.png");
        string input = Path.Combine(root, "input.png");
        File.WriteAllBytes(input, Png);
        if (hadCover) File.WriteAllText(cover, "original cover bytes");
        if (corrupt) File.WriteAllText(metadata, "corrupt metadata fixture");
        byte[] before = File.ReadAllBytes(metadata);
        PlatformServices.ImageCodec = new Codec(() =>
        {
            if (corrupt) return;
            File.Move(metadata, metadata + ".fixture");
            Directory.CreateDirectory(metadata);
        });
        try
        {
            var error = Record.Exception(() => InstanceArtwork.SetCover(instance, input));
            Assert.True(error is IOException or InvalidDataException or UnauthorizedAccessException or HelL.Core.OSError,
                error?.ToString() ?? "Expected metadata failure.");
        }
        finally
        {
            if (Directory.Exists(metadata)) { Directory.Delete(metadata); File.Move(metadata + ".fixture", metadata); }
        }

        if (hadCover) Assert.Equal("original cover bytes", File.ReadAllText(cover));
        else Assert.False(File.Exists(cover));
        Assert.Equal(before, File.ReadAllBytes(metadata));
        Assert.Empty(Directory.GetFiles(instance.path, ".hell-cover-*.tmp"));
        Assert.Empty(Directory.GetFiles(instance.path, ".hell-cover-*.bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulReplacementCommitsCoverAndMetadataTogether(bool hadCover)
    {
        string input = Path.Combine(root, "input.png");
        File.WriteAllBytes(input, Png);
        if (hadCover) File.WriteAllText(Path.Combine(instance.path, ".hell-cover.png"), "original cover bytes");
        PlatformServices.ImageCodec = new Codec(() => { });
        InstanceArtwork.SetCover(instance, input);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(instance.path, ".hell-cover.png")));
        Assert.Equal(true, instance.Meta().Get("custom_cover"));
        Assert.Empty(Directory.GetFiles(instance.path, ".hell-cover-*.bak"));
    }

    private sealed class Codec(Action encode) : IImageCodec
    {
        public IDecodedImage Decode(byte[] bytes) => new Picture(encode);
    }

    private sealed class Picture(Action encode) : IDecodedImage
    {
        public int Width => 10;
        public int Height => 10;
        public bool HasAlpha => true;
        public IDecodedImage Resize(int width, int height) => new Picture(encode);
        public byte[] EncodePng() { encode(); return Png; }
        public byte[] EncodeJpeg(int quality) => throw new NotSupportedException();
        public void Dispose() { }
    }

    public void Dispose()
    {
        PlatformServices.ImageCodec = previousCodec!;
        Config.Reset();
        Directory.Delete(root, recursive: true);
    }
}
