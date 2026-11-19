using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using HelL.Services;

internal static class VideoCacheProbe
{
    internal static async Task Run(string folder)
    {
        Directory.CreateDirectory(folder);
        string video = WallpaperEngine.Read(WallpaperEngine.DiscoverConfigs().First()).First(paper => paper.Kind == "video").File;
        using var stream = typeof(Wallpaper).Assembly.GetManifestResourceStream("HelL.WallpaperCapture.dll")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var encoder = Assembly.Load(bytes.ToArray()).GetType("HelL.WallpaperRuntime.VideoEncoder", true)!;
        var clock = Stopwatch.StartNew(); int last = -1;
        var progress = new Progress<double>(value => { int bucket = (int)value / 10; if (value < 0 || bucket != last) { last = bucket; Console.WriteLine("[TRANSCODE] " + value.ToString("F1") + "%"); } });
        string path = Path.Combine(folder, "video-" + Guid.NewGuid().ToString("N") + ".mp4");
        var pending = (Task<string>)encoder.GetMethod("Encode")!.Invoke(null, new object[] { video, path, progress, CancellationToken.None })!;
        string output = await pending;
        Console.WriteLine("[TRANSCODE DONE] " + output + "; seconds=" + clock.Elapsed.TotalSeconds + "; bytes=" + new FileInfo(output).Length);
        File.WriteAllText(Path.Combine(folder, "video-path.txt"), output);
    }
}
