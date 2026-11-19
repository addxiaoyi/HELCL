using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace HelL.WallpaperRuntime;

public static class VideoEncoder
{
    public static async Task<string> Encode(string source, string destination, IProgress<double> progress, CancellationToken cancellation)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("缓存不能覆盖原视频。");
        using var inputFile = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var input = inputFile.AsRandomAccessStream();
        var original = await MediaEncodingProfile.CreateFromStreamAsync(input).AsTask(cancellation);
        if (original.Video == null) throw new InvalidDataException("所选文件不包含视频画面。");
        var video = original.Video;
        if (video.Width == 0 || video.Height == 0) throw new InvalidDataException("无法读取视频尺寸。");
        if (video.Width <= 1920 && video.Height <= 1080 && video.Subtype.Equals("H264", StringComparison.OrdinalIgnoreCase)
            && video.FrameRate.Numerator <= 30 * video.FrameRate.Denominator) return source;
        double scale = Math.Min(1, Math.Min(1920d / video.Width, 1080d / video.Height));
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)Math.Max(2, Math.Floor(video.Width * scale / 2) * 2);
        profile.Video.Height = (uint)Math.Max(2, Math.Floor(video.Height * scale / 2) * 2);
        profile.Video.Bitrate = 6000000;
        bool limitFps = video.FrameRate.Denominator == 0 || video.FrameRate.Numerator > 30 * video.FrameRate.Denominator;
        profile.Video.FrameRate.Numerator = limitFps ? 30 : video.FrameRate.Numerator;
        profile.Video.FrameRate.Denominator = limitFps ? 1 : video.FrameRate.Denominator;
        bool complete = false, created = false;
        try
        {
            using var outputFile = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true;
            using var output = outputFile.AsRandomAccessStream();
            input.Seek(0);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
            var prepared = await transcoder.PrepareStreamTranscodeAsync(input, output, profile).AsTask(cancellation);
            if (!prepared.CanTranscode) throw new NotSupportedException("系统无法优化此视频：" + prepared.FailureReason);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var limited = new EncodeProgress(destination, progress, budget);
            try { await prepared.TranscodeAsync().AsTask(budget.Token, limited); }
            catch (OperationCanceledException) when (limited.Exceeded) { throw new IOException("此视频的缓存超过 256 MB，保留原视频播放。"); }
            complete = true; return destination;
        }
        finally { if (created && !complete && File.Exists(destination)) File.Delete(destination); }
    }

    private sealed class EncodeProgress : IProgress<double>
    {
        private readonly string _path;
        private readonly IProgress<double> _progress;
        private readonly CancellationTokenSource _budget;
        public bool Exceeded { get; private set; }
        public EncodeProgress(string path, IProgress<double> progress, CancellationTokenSource budget) { _path = path; _progress = progress; _budget = budget; }
        public void Report(double value)
        {
            if (new FileInfo(_path).Length > 256L * 1024 * 1024) { Exceeded = true; _budget.Cancel(); }
            _progress.Report(value);
        }
    }
}
