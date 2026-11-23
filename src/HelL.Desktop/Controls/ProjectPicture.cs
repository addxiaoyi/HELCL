using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HelL.Core.Launcher;
using HelL.Desktop.Services;

namespace HelL.Desktop.Controls;

/// <summary>Displays only an instance image or the selected project's actual upstream artwork.</summary>
public sealed class ProjectPicture : Border
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<ProjectPicture, string?>(nameof(Source));
    public static readonly StyledProperty<int> DecodeWidthProperty = AvaloniaProperty.Register<ProjectPicture, int>(nameof(DecodeWidth), 320);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Slots = new(4);
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly Image picture = new() { Stretch = Stretch.UniformToFill };
    private readonly PathIcon cubeGlyph = new()
    {
        Width = 18,
        Height = 18,
        Opacity = 0.5,
        Data = StreamGeometry.Parse("M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V9.21L13,12.58V19.29L19,15.91Z"),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };
    private CancellationTokenSource? loading;
    private Bitmap? bitmap;
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public int DecodeWidth { get => GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    internal bool ImageLoaded => bitmap != null;

    public ProjectPicture()
    {
        ClipToBounds = true;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(0);
        this.Bind(BackgroundProperty, this.GetResourceObservable("HelL.Field"));
        this.Bind(BorderBrushProperty, this.GetResourceObservable("HelL.Line"));
        cubeGlyph.Bind(PathIcon.ForegroundProperty, this.GetResourceObservable("HelL.Muted"));
        Child = new Grid { Children = { cubeGlyph, picture } };
        AttachedToVisualTree += (_, _) => Refresh();
        DetachedFromVisualTree += (_, _) => Clear();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == SourceProperty || change.Property == DecodeWidthProperty) && IsLoaded) Refresh();
    }

    private void Clear()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        picture.Source = null;
        bitmap?.Dispose();
        bitmap = null;
        cubeGlyph.IsVisible = true;
    }

    private async void Refresh()
    {
        Clear();
        string source = Source ?? "";
        if (source.Length == 0) return;
        var cancellation = new CancellationTokenSource();
        loading = cancellation;
        var token = cancellation.Token;
        int decodeWidth = Math.Clamp(DecodeWidth, 64, 2048);
        try
        {
            await Slots.WaitAsync(token);
            Bitmap decoded;
            try { decoded = await Task.Run(() => Read(source, decodeWidth, token), token); }
            finally { Slots.Release(); }
            if (token.IsCancellationRequested) { decoded.Dispose(); return; }
            bitmap = decoded;
            picture.Source = bitmap;
            cubeGlyph.IsVisible = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                cubeGlyph.Opacity = 0.28;
                System.Diagnostics.Trace.TraceWarning("ProjectPicture: " + error.Message);
            }
        }
    }

    private static async Task<Bitmap> Read(string source, int decodeWidth, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (CatalogPresentation.ImageUrl(source).Length == 0) throw new IOException("Unsupported project image origin.");
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new IOException("Project image exceeds size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            await CopyBounded(stream, buffer, token);
        }
        else
        {
            if (!Path.IsPathFullyQualified(source)) throw new IOException("Instance image must use an absolute path.");
            await using var stream = File.OpenRead(source);
            if (stream.Length > MaxBytes) throw new IOException("Instance image exceeds preview size limit.");
            await CopyBounded(stream, buffer, token);
        }
        token.ThrowIfCancellationRequested();
        return DecodePreview(buffer.ToArray(), decodeWidth);
    }

    internal static Bitmap DecodePreview(byte[] encoded, int requestedSize)
    {
        if (encoded.Length > MaxBytes) throw new InvalidDataException("预览图片文件过大。");
        var source = ImageBounds.Read(encoded, ImageBounds.PreviewPixels);
        int longest = Math.Max(source.Width, source.Height);
        int limit = Math.Min(longest, Math.Clamp(requestedSize, 64, 2048));
        using var stream = new MemoryStream(encoded, false);
        var preview = source.Width >= source.Height
            ? Bitmap.DecodeToWidth(stream, limit)
            : Bitmap.DecodeToHeight(stream, limit);
        if (preview.PixelSize.Width <= limit && preview.PixelSize.Height <= limit) return preview;
        preview.Dispose();
        throw new InvalidDataException("图片缩略图超过预览尺寸限制。");
    }

    private static async Task CopyBounded(Stream source, Stream target, CancellationToken token)
    {
        byte[] bytes = new byte[32768];
        int count;
        while ((count = await source.ReadAsync(bytes, token)) > 0)
        {
            if (target.Length + count > MaxBytes) throw new IOException("Image exceeds size limit.");
            await target.WriteAsync(bytes.AsMemory(0, count), token);
        }
    }
}
