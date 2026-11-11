using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HelL.Core.Launcher;

namespace HelL.Desktop.Controls;

/// <summary>Displays only an instance image or the selected project's actual upstream artwork.</summary>
public sealed class ProjectPicture : Border
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<ProjectPicture, string?>(nameof(Source));
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Slots = new(4);
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly Image picture = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock placeholder = new() { Text = "暂无图片", FontSize = 11, Foreground = Brushes.Gray,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private CancellationTokenSource? loading;
    private Bitmap? bitmap;
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    internal bool ImageLoaded => bitmap != null;

    public ProjectPicture()
    {
        ClipToBounds = true;
        CornerRadius = new CornerRadius(6);
        Background = new SolidColorBrush(Color.Parse("#202930"));
        Child = new Grid { Children = { placeholder, picture } };
        AttachedToVisualTree += (_, _) => Refresh();
        DetachedFromVisualTree += (_, _) => Clear();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && IsLoaded) Refresh();
    }

    private void Clear()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        picture.Source = null;
        bitmap?.Dispose();
        bitmap = null;
        placeholder.IsVisible = true;
    }

    private async void Refresh()
    {
        Clear();
        string source = Source ?? "";
        if (source.Length == 0) return;
        var cancellation = new CancellationTokenSource();
        loading = cancellation;
        var token = cancellation.Token;
        try
        {
            await Slots.WaitAsync(token);
            Bitmap decoded;
            try { decoded = await Task.Run(() => Read(source, token), token); }
            finally { Slots.Release(); }
            if (token.IsCancellationRequested) { decoded.Dispose(); return; }
            bitmap = decoded;
            picture.Source = bitmap;
            placeholder.IsVisible = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                placeholder.Text = "图片未加载";
                System.Diagnostics.Trace.TraceWarning("ProjectPicture: " + error.Message);
            }
        }
    }

    private static async Task<Bitmap> Read(string source, CancellationToken token)
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
        buffer.Position = 0;
        return Bitmap.DecodeToWidth(buffer, 320);
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
