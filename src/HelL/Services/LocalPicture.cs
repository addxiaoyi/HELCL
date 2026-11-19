using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL.Core.Launcher;

namespace HelL.Services;

public sealed class LocalPicture : Grid
{
    private static readonly SemaphoreSlim Gate = new SemaphoreSlim(3);
    private readonly Image _image = new Image { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _placeholder = Ui.Txt("", 11, false, "B.InkMuted");
    private int _generation;
    private readonly int _decodeWidth;
    public double Radius { get; set; } = 8;
    public bool Ready => _image.Source != null;

    public LocalPicture(double width, double height, string placeholder, bool contain = false, int decodeWidth = 480)
    {
        if (decodeWidth < 64 || decodeWidth > 2048) throw new ArgumentOutOfRangeException(nameof(decodeWidth));
        _decodeWidth = decodeWidth; _image.Stretch = contain ? Stretch.Uniform : Stretch.UniformToFill;
        Width = width; Height = height;
        SetResourceReference(BackgroundProperty, "B.Paper2");
        _placeholder.Text = placeholder;
        _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.TextAlignment = TextAlignment.Center;
        _placeholder.TextWrapping = TextWrapping.Wrap;
        _placeholder.Margin = new Thickness(6);
        Children.Add(_placeholder); Children.Add(_image);
        SizeChanged += (_, _) => Clip = Radius > 0 ? new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), Radius, Radius) : null;
    }

    public async Task SetPath(string path, string root, string label)
    {
        int generation = ++_generation;
        _image.Source = null;
        _placeholder.Visibility = Visibility.Visible;
        AutomationProperties.SetName(this, label);
        if (string.IsNullOrWhiteSpace(path)) return;
        await Gate.WaitAsync();
        try
        {
            var picture = await Task.Run(() => Read(path, root, _decodeWidth));
            if (generation != _generation || picture == null) return;
            _image.Source = picture;
            _placeholder.Visibility = Visibility.Collapsed;
            ToolTip = label;
        }
        catch (Exception ex) { Smoke.Note("local-picture", ex.Message); }
        finally { Gate.Release(); }
    }

    private static BitmapSource? Read(string path, string root, int decodeWidth)
    {
        if (!InstanceMedia.IsLocalImage(path, root)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > InstanceMedia.MaxImageBytes) return null;
        var frame = BitmapFrame.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if ((long)frame.PixelWidth * frame.PixelHeight > 64000000) return null;
        file.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = Math.Min(frame.PixelWidth, decodeWidth);
        bitmap.StreamSource = file; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
}
