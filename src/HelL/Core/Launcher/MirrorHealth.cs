using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher;

public sealed class MirrorSample
{
    public string Provider { get; }
    public string Group { get; }
    public double Milliseconds { get; }
    public bool Available { get; }
    public string Detail { get; }
    public MirrorSample(string provider, string group, double milliseconds, bool available, string detail = "")
    { Provider = provider; Group = group; Milliseconds = milliseconds; Available = available; Detail = detail; }
}

public sealed class MirrorRanking
{
    private sealed class Score
    {
        public double Latency;
        public DateTimeOffset Checked;
        public DateTimeOffset RetryAt;
        public bool Available;
        public string Detail = "";
    }
    private readonly object _gate = new object();
    private readonly Dictionary<string, Score> _scores = new Dictionary<string, Score>();
    private string _network = "";

    public void Reset(string network) { lock (_gate) { _scores.Clear(); _network = network; } }
    public void Record(string network, MirrorSample sample, DateTimeOffset now, bool failed = false)
    {
        lock (_gate)
        {
            if (_network != network) { _scores.Clear(); _network = network; }
            string key = sample.Group + ":" + sample.Provider;
            double latency = sample.Milliseconds;
            if (sample.Available && _scores.TryGetValue(key, out var previous) && previous.Available && now - previous.Checked < TimeSpan.FromMinutes(10))
                latency = previous.Latency * 0.7 + latency * 0.3;
            _scores[key] = new Score { Latency = latency, Available = sample.Available,
                Checked = now, RetryAt = failed || !sample.Available ? now.AddSeconds(90) : now, Detail = sample.Detail };
        }
    }
    public List<string> Order(IEnumerable<string> urls, string group, string network, string preferred, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_network != network) { _scores.Clear(); _network = network; }
            return urls.Distinct().Select((url, index) => new { Url = url, Index = index, Provider = FileMirrors.Provider(url) })
                .OrderBy(entry => ScoreFor(entry.Provider, group, preferred, now)).ThenBy(entry => entry.Index).Select(entry => entry.Url).ToList();
        }
    }
    private double ScoreFor(string provider, string group, string preferred, DateTimeOffset now)
    {
        if (preferred != "auto") return provider == preferred ? -1 : provider == "official" ? 1000 : 2000;
        bool exact = _scores.TryGetValue(group + ":" + provider, out var score);
        if (!exact) _scores.TryGetValue("all:" + provider, out score);
        if (score == null || now - score.Checked > TimeSpan.FromMinutes(10)) return 30000;
        if (score.RetryAt > now) return 60000;
        return score.Available ? score.Latency : 30000;
    }
}

public static class MirrorHealth
{
    private const int ProbeBytes = 888;
    private const string ProbeHash = "bd65e7d2e3c237be76cfbef4c2405033d7f91521";
    private const string ProbeOrigin = "https://piston-data.mojang.com/v1/objects/" + ProbeHash + "/client-1.12.xml";
    private static readonly object Gate = new object();
    private static readonly SemaphoreSlim ProbeGate = new SemaphoreSlim(1);
    private static readonly MirrorRanking Ranking = new MirrorRanking();
    private static List<MirrorSample> _samples = new List<MirrorSample>();
    private static DateTimeOffset _checked;
    private static long _generation;
    private static string _network = "";
    private static bool _networkEvents;
    private static int _warming;

    public static string NetworkKey => Interlocked.Read(ref _generation) + ":" + Net.ForceDirect() + ":" + Net.ReplayBase;
    public static void Invalidate()
    {
        lock (Gate) { Interlocked.Increment(ref _generation); _checked = default; _samples.Clear(); Ranking.Reset(NetworkKey); }
    }
    public static void Warmup()
    {
        if (Interlocked.Exchange(ref _warming, 1) != 0) return;
        Task.Run(() => { try { Refresh(); } catch (Exception ex) { Trace.TraceWarning("Mirror warmup: " + ex.Message); } finally { Volatile.Write(ref _warming, 0); } });
    }
    public static List<string> Order(IEnumerable<string> urls, string upstream, string preferred)
    {
        var candidates = urls.Distinct().ToList();
        if (preferred == "auto" && Source.ProbeHook == null) Refresh();
        return Ranking.Order(candidates, FileMirrors.Group(upstream), NetworkKey, preferred, DateTimeOffset.UtcNow);
    }

    public static PyDict Refresh(bool force = false)
    {
        EnsureNetworkEvents();
        string network = NetworkKey;
        lock (Gate) { if (!force && _network == network && _checked != default && DateTimeOffset.UtcNow - _checked < TimeSpan.FromMinutes(10)) return Snapshot(); }
        ProbeGate.Wait();
        try
        {
            network = NetworkKey;
            lock (Gate) { if (!force && _network == network && _checked != default && DateTimeOffset.UtcNow - _checked < TimeSpan.FromMinutes(10)) return Snapshot(); }
            var probes = new[] { ("official", ProbeOrigin), ("bmclapi", Source.BMCLAPI + "/v1/objects/" + ProbeHash + "/client-1.12.xml"),
                ("mc_mirror", FileMirrors.Main + "/logging/client-1.12.xml"), ("mc_mirror_west", FileMirrors.West + "/logging/client-1.12.xml") };
            var samples = Task.WhenAll(probes.Select(probe => Task.Run(() => Probe(probe.Item1, probe.Item2)))).GetAwaiter().GetResult().ToList();
            lock (Gate)
            {
                if (network != NetworkKey) return Snapshot();
                _samples = samples; _network = network; _checked = DateTimeOffset.UtcNow;
                foreach (var sample in samples) Ranking.Record(network, sample, _checked);
                return Snapshot();
            }
        }
        finally { ProbeGate.Release(); }
    }

    public static void ReportFailure(string url, string upstream, Exception failure, string network = null)
    {
        if (Downloader.IsCancel(failure) || failure is OperationCanceledException) return;
        if (network != null && network != NetworkKey) return;
        string provider = FileMirrors.Provider(url); if (provider.Length == 0) return;
        // Missing files are local to a URL; only penalize connection, protocol or integrity failures.
        if (failure.Message.Contains("404") || failure.Message.Contains("416")) return;
        Ranking.Record(NetworkKey, new MirrorSample(provider, FileMirrors.Group(upstream), 0, false, "下载失败，暂时降低优先级"), DateTimeOffset.UtcNow, true);
    }

    public static void ReportSuccess(string url, string upstream, double milliseconds, string network = null)
    {
        if (network != null && network != NetworkKey) return;
        string provider = FileMirrors.Provider(url); if (provider.Length == 0) return;
        Ranking.Record(NetworkKey, new MirrorSample(provider, FileMirrors.Group(upstream), Math.Max(1, milliseconds), true), DateTimeOffset.UtcNow);
    }

    public static bool IsKnown(string url) => FileMirrors.Provider(url).Length > 0;

    private static PyDict Snapshot()
    {
        var rows = _samples.OrderBy(sample => !sample.Available).ThenBy(sample => sample.Milliseconds).Select(sample => (object)PyDict.Of(
            "provider", sample.Provider, "name", FileMirrors.Label(sample.Provider), "available", sample.Available,
            "latency_ms", (long)sample.Milliseconds, "message", sample.Detail)).ToList();
        return PyDict.Of("checked_at", _checked == default ? "" : _checked.ToString("O"), "samples", rows,
            "preferred", _samples.Where(sample => sample.Available).OrderBy(sample => sample.Milliseconds).FirstOrDefault()?.Provider ?? "",
            "message", _samples.Any(sample => sample.Available) ? "已按当前网络响应排序，下载失败会自动切换。" : "暂未测得可用源，下载时会依次尝试并保留重试入口。");
    }

    private static MirrorSample Probe(string provider, string url)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            using var response = Net.Request(url, new Net.RequestOptions { ConnectTimeout = 2, ReadTimeout = 2, Token = timeout.Token, Headers = new Dictionary<string, string> { ["Accept-Encoding"] = "identity" } });
            response.RaiseForStatus();
            if (response.StatusCode != 200) throw new IOException("检测响应无效");
            using var bytes = new MemoryStream(); var buffer = new byte[ProbeBytes + 1]; int read;
            while ((read = response.Read(buffer, 0, buffer.Length)) > 0)
            {
                bytes.Write(buffer, 0, read); if (bytes.Length > ProbeBytes) throw new IOException("检测内容不匹配");
            }
            using var sha = SHA1.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            if (hash != ProbeHash) throw new IOException("检测内容校验失败");
            return new MirrorSample(provider, "all", watch.Elapsed.TotalMilliseconds, true, "连接与内容校验通过");
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Mirror probe " + provider + ": " + ex.GetType().Name);
            return new MirrorSample(provider, "all", watch.Elapsed.TotalMilliseconds, false, timeout.IsCancellationRequested ? "连接超时" : "暂不可用或内容校验失败");
        }
    }

    private static void EnsureNetworkEvents()
    {
        lock (Gate)
        {
            if (_networkEvents) return;
            NetworkChange.NetworkAddressChanged += (_, _) => Invalidate();
            NetworkChange.NetworkAvailabilityChanged += (_, _) => Invalidate();
            _networkEvents = true;
        }
    }
}
