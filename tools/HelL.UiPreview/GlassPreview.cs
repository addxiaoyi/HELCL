using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HelL;
using HelL.Services;

internal static class GlassPreview
{
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        Motion.Enabled = false;
        window.Width = 900;
        window.Height = 580;
        string wallpaper = Path.Combine(folder, "wallpaper-fixture.png");
        SaveWallpaper(wallpaper);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            string theme = dark ? "dark" : "light";
            window.Navigate("instance", instant: true);
            await Program.WaitForPage(window, "instance");
            Button more = NamedButton(window, "更多操作");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Program.SaveSnapshot(more.ContextMenu, Path.Combine(folder, $"menu-{theme}.png"));
            more.ContextMenu.IsOpen = false;
            NamedButton(window, "新建实例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Program.SaveSnapshot(window, Path.Combine(folder, $"dialog-{theme}.png"));
            NamedButton(window, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.Navigate("launch", instant: true);
            await Program.WaitForPage(window, "launch");
            Wallpaper.SetSource(wallpaper);
            SideFrost.SetEnabled(true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Program.SaveSnapshot(window, Path.Combine(folder, $"wallpaper-{theme}.png"));
            SideFrost.SetEnabled(false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Program.SaveSnapshot(window, Path.Combine(folder, $"solid-sidebar-{theme}.png"));
            SideFrost.SetEnabled(true);
            Wallpaper.SetSource("");
        }
    }

    private static Button NamedButton(DependencyObject root, string name) => Descendants(root).OfType<Button>()
        .Single(button => button.IsVisible && AutomationProperties.GetName(button) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void SaveWallpaper(string path)
    {
        // Hard edges and both extremes expose leakage through translucent reading surfaces.
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            Brush[] stripes = { Brushes.Black, Brushes.White, Brushes.SeaGreen, Brushes.SlateBlue };
            for (int i = 0; i < stripes.Length; i++) drawing.DrawRectangle(stripes[i], null, new Rect(i * 300, 0, 300, 800));
        }
        var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
