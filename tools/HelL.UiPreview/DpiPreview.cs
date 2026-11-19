using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL;

internal static class DpiPreview
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PrivateExtractIconsW(string path, int index, int width, int height,
        out IntPtr icon, out uint id, uint count, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    internal static void Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        DpiScale actual = VisualTreeHelper.GetDpi(window);
        Console.WriteLine($"[DISPLAY_DPI] {actual.PixelsPerInchX} x {actual.PixelsPerInchY}; other scales below are offscreen renders");
        var artwork = new BitmapImage(new Uri("pack://application:,,,/HelL;component/Resources/Brand/Helstera.png"));
        CheckAlpha(artwork, "Title artwork");
        using (Stream icon = Application.GetResourceStream(new Uri("/HelL;component/HelL.ico", UriKind.Relative)).Stream)
        {
            var decoder = BitmapDecoder.Create(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int[] expected = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            if (!decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(size => size).SequenceEqual(expected))
                throw new InvalidOperationException("The embedded icon is missing a required DPI size.");
            foreach (BitmapFrame frame in decoder.Frames) CheckAlpha(frame, "Embedded ICO " + frame.PixelWidth);
        }

        var sheet = new Grid { Width = 960, Height = 420 };
        for (int column = 0; column < 4; column++) sheet.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 2; row++) sheet.RowDefinitions.Add(new RowDefinition());
        double[] scales = { 1, 1.25, 1.5, 2 };
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            window.UpdateLayout();
            for (int index = 0; index < scales.Length; index++)
            {
                double scale = scales[index];
                string theme = dark ? "dark" : "light";
                Program.SaveSnapshot(window, Path.Combine(folder, $"launch-{theme}-{scale * 100:0}.png"), scale);
                var brand = (FrameworkElement)window.FindName("BrandHost");
                Program.SaveSnapshot(brand, Path.Combine(folder, $"brand-{theme}-{scale * 100:0}.png"), scale);
                int smallSize = (int)(16 * scale), taskSize = (int)(32 * scale);
                BitmapSource small = Extract(smallSize), task = Extract(taskSize);
                Save(task, Path.Combine(folder, $"exe-{taskSize}.png"));
                var stack = new StackPanel { Margin = new Thickness(16) };
                Brush ink = dark ? Brushes.WhiteSmoke : Brushes.Black;
                stack.Children.Add(new TextBlock { Text = $"{scale * 100:0}% · {96 * scale:0} DPI", Foreground = ink, FontSize = 16 });
                stack.Children.Add(new TextBlock { Text = $"EXE: {smallSize}px / {taskSize}px", Foreground = ink, Margin = new Thickness(0, 8, 0, 8) });
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Height = 70 };
                icons.Children.Add(Picture(small, smallSize));
                icons.Children.Add(Picture(task, taskSize));
                icons.Children.Add(Picture(artwork, taskSize));
                stack.Children.Add(icons);
                stack.Children.Add(new TextBlock { Text = "Window / Taskbar / Title PNG", Foreground = ink, FontSize = 11 });
                var tile = new Border { Background = new SolidColorBrush(dark ? Color.FromRgb(24, 32, 28) : Color.FromRgb(245, 248, 246)), Child = stack };
                Grid.SetRow(tile, dark ? 1 : 0);
                Grid.SetColumn(tile, index);
                sheet.Children.Add(tile);
            }
        }
        sheet.Measure(new Size(960, 420));
        sheet.Arrange(new Rect(0, 0, 960, 420));
        Program.SaveSnapshot(sheet, Path.Combine(folder, "icons-at-scale.png"));
        Console.WriteLine("[DPI PASS] 9 embedded frames, native EXE extraction and 100/125/150/200% light/dark renders");
    }

    private static Image Picture(BitmapSource source, int size)
    {
        var image = new Image { Source = source, Width = size, Height = size, Margin = new Thickness(0, 0, 12, 0) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static BitmapSource Extract(int size)
    {
        string executable = typeof(MainWindow).Assembly.Location;
        uint count = PrivateExtractIconsW(executable, 0, size, size, out IntPtr icon, out _, 1, 0);
        if (count != 1 || icon == IntPtr.Zero) throw new InvalidOperationException("Cannot extract EXE icon at " + size);
        try
        {
            BitmapSource pixels = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            pixels.Freeze();
            if (pixels.PixelWidth != size || pixels.PixelHeight != size) throw new InvalidOperationException("Incorrect EXE icon dimensions.");
            CheckAlpha(pixels, "Native EXE " + size);
            return pixels;
        }
        finally
        {
            if (!DestroyIcon(icon)) throw new InvalidOperationException("Cannot release the extracted icon.");
        }
    }

    private static void CheckAlpha(BitmapSource bitmap, string label)
    {
        var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = pixels.PixelWidth * 4;
        var bytes = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(bytes, stride, 0);
        int[] corners = { 3, stride - 1, bytes.Length - stride + 3, bytes.Length - 1 };
        if (corners.Any(offset => bytes[offset] != 0)) throw new InvalidOperationException(label + " has an opaque background corner.");
        int visible = Enumerable.Range(0, bytes.Length / 4).Count(index => bytes[index * 4 + 3] > 128);
        if (visible < pixels.PixelWidth * pixels.PixelHeight / 10) throw new InvalidOperationException(label + " has no usable artwork.");
        Console.WriteLine($"[ICON PASS] {label}: {pixels.PixelWidth} x {pixels.PixelHeight}, transparent corners, {visible} visible pixels");
    }

    private static void Save(BitmapSource pixels, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pixels));
        using (Stream stream = File.Create(path)) encoder.Save(stream);
    }
}
