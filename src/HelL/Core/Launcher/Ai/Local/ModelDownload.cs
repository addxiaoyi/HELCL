using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class ModelDownload
{
    private readonly HttpClient _http;
    public ModelDownload(HttpClient http) => _http = http;

    public async Task DownloadAsync(Uri uri, string target, long size, string sha256,
        Action<long, long, string> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (size <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(sha256 ?? "", @"\A[a-fA-F0-9]{64}\z"))
            throw new ArgumentException("上游未提供可靠的文件大小或 SHA-256，不能验证此下载。");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length == size && string.Equals(await HashAsync(target, ct).ConfigureAwait(false), sha256, StringComparison.OrdinalIgnoreCase)) return;
            throw new IOException("目标文件已存在且校验不符，请选择其他名称或移走该文件。");
        }
        string partial = target + ".partial";
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > size) { File.Delete(partial); offset = 0; }
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!);
        if (drive.AvailableFreeSpace < size - offset + 256 * 1024 * 1024)
            throw new IOException("磁盘空间不足，请释放空间后重试。已下载部分会保留。");
        if (offset < size)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(TimeSpan.FromSeconds(60));
            using var response = await ModelHttp.GetAsync(_http, uri, offset, idle.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range == null || range.Unit != "bytes" || range.From != offset || range.To != size - 1 || range.Length != size)
                    throw new IOException("镜像返回的续传范围不匹配，已停止下载。");
            }
            else offset = 0; // A server may ignore Range. Restart, never append a full response.
            long? length = response.Content.Headers.ContentLength;
            if (length.HasValue && length.Value != size - offset)
                throw new IOException("上游文件大小已变化，请重新查询模型文件后重试。");
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var cancellation = idle.Token.Register(input.Dispose);
            using var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
            var buffer = new byte[131072];
            while (true)
            {
                idle.CancelAfter(TimeSpan.FromSeconds(60));
                int read = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (offset + read > size) throw new IOException("下载内容超出声明大小，已停止写入。");
                await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                offset += read;
                progress(offset, size, "下载中 · 支持续传");
            }
        }
        ct.ThrowIfCancellationRequested();
        if (offset != size) throw new IOException("下载提前结束，已保留部分文件。点击重试继续下载。");
        progress(size, size, "正在验证 SHA-256");
        string actual = await HashAsync(partial, ct).ConfigureAwait(false);
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new IOException("SHA-256 校验失败，已移除损坏的临时文件。请切换来源后重试。");
        }
        ct.ThrowIfCancellationRequested();
        File.Move(partial, target);
    }

    public static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        using var hash = SHA256.Create();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        var buffer = new byte[131072];
        int read;
        while ((read = await file.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
            hash.TransformBlock(buffer, 0, read, buffer, 0);
        ct.ThrowIfCancellationRequested();
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(hash.Hash!).Replace("-", "").ToLowerInvariant();
    }
}
