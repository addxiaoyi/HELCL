using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using HelL.Core.Launcher;
using HelL.Services;

internal static class EngineFrameCheck
{
    internal static async Task Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var configs = WallpaperEngine.DiscoverConfigs(); Console.WriteLine("[ENGINE CONFIGS] " + string.Join(", ", configs));
        foreach (string config in configs)
        {
            foreach (var wallpaper in WallpaperEngine.Read(config).Where(paper => !paper.Direct))
            {
                try
                {
                    string project = Path.Combine(Path.GetDirectoryName(wallpaper.File)!, "project.json");
                    using var window = await EngineWallpaperWindow.Open(config, File.Exists(project) ? project : wallpaper.File);
                    using var live = new EngineCapture(window.Handle); var frame = await live.FirstFrame();
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
                    using var file = File.Create(Path.Combine(folder, wallpaper.Monitor + "-live.png")); encoder.Save(file);
                    Console.WriteLine("[LIVE FRAME] " + wallpaper.Monitor + " " + wallpaper.Kind + " " + frame.PixelWidth + "x" + frame.PixelHeight);
                }
                catch (Exception ex) { Console.WriteLine("[LIVE FAILED] " + wallpaper.Monitor + " " + ex); }
            }
        }
    }
}
