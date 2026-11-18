using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftLanDiscovery
{
    private const int AdvertisementPort = 4445;
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    private static readonly Regex Announcement = new Regex(@"\A\[MOTD\](.{0,512}?)\[/MOTD\]\[AD\]([0-9]{1,5})\[/AD\]\z", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static PyDict? Parse(byte[] bytes, IPEndPoint sender)
    {
        if (bytes.Length > 2048 || sender.Address.AddressFamily != AddressFamily.InterNetwork || sender.Address.Equals(IPAddress.Any) || sender.Address.Equals(IPAddress.Broadcast) || sender.Address.GetAddressBytes()[0] >= 224) return null;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { return null; }
        var match = Announcement.Match(text);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int port) || port < 1 || port > 65535) return null;
        return PyDict.Of("host", sender.Address.ToString(), "port", port, "name", News.PlainText(match.Groups[1].Value, 120), "source", "lan");
    }

    public static async Task<PyDict> DiscoverAsync(CancellationToken token = default)
    {
        var warnings = new List<string>();
        var networks = new List<object>();
        var broadcast = await Task.Run(() => Listen(3500, warnings, networks, token), token).ConfigureAwait(false);
        var found = await VerifyCandidatesAsync(broadcast.Concat(LocalJavaEndpoints(warnings)).OfType<PyDict>(), token).ConfigureAwait(false);
        found["warnings"] = warnings.Cast<object>().ToList(); found["networks"] = networks;
        found["message"] = Py.ListOr(found.Get("servers")).Count > 0 ? "选择一个世界，地址和端口会自动填入。" : Py.ListOr(found.Get("unreachable")).Count > 0
            ? "发现了世界广播，但无法连接游戏端口。请确认双方处于同一网络，并允许对方的 Java 通过防火墙。"
            : "未发现可用世界。请在 Minecraft 中“对局域网开放”，然后再次查找；也可以手动输入服务器地址。";
        return found;
    }

    public static async Task<PyDict> VerifyCandidatesAsync(IEnumerable<PyDict> announcements, CancellationToken token = default)
    {
        var candidates = announcements
            .GroupBy(row => Py.S(row.Get("host")) + ":" + Py.S(row.Get("port"))).Select(group => group.First()).Take(32).ToList();
        using var slots = new SemaphoreSlim(4);
        var checkedServers = await Task.WhenAll(candidates.Select(async candidate =>
        {
            await slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var status = await MinecraftServerProbe.CheckAsync(Py.S(candidate.Get("host")), (int)Py.Int(candidate.Get("port")), token: token, timeoutMs: 1500).ConfigureAwait(false);
                status["name"] = Py.S(status.Get("motd")).Length > 0 ? status.Get("motd") : candidate.Get("name");
                status["source"] = candidate.Get("source"); return status;
            }
            finally { slots.Release(); }
        })).ConfigureAwait(false);
        return PyDict.Of("servers", checkedServers.Where(row => Py.Truthy(row.Get("online"))).Cast<object>().ToList(),
            "unreachable", checkedServers.Where(row => !Py.Truthy(row.Get("online")) && Py.S(row.Get("source")) == "lan").Cast<object>().ToList(), "checked_at", DateTime.UtcNow.ToString("O"));
    }

    private static List<object> Listen(int milliseconds, List<string> warnings, List<object> networks, CancellationToken token)
    {
        var rows = new Dictionary<string, object>();
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.ExclusiveAddressUse = false; udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, AdvertisementPort)); udp.Client.ReceiveTimeout = 200;
            int memberships = 0;
            var local = NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(adapter => adapter.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new { adapter.Name, address.Address })).GroupBy(entry => entry.Address).Select(group => group.First()).Take(32).ToList();
            foreach (var network in local)
            {
                var entry = PyDict.Of("name", network.Name, "address", network.Address.ToString(), "joined", false); networks.Add(entry);
                try { udp.JoinMulticastGroup(Group, network.Address); memberships++; entry["joined"] = true; }
                catch (SocketException ex) { entry["error"] = ex.SocketErrorCode.ToString(); System.Diagnostics.Trace.TraceWarning("Minecraft LAN multicast: " + network.Name + " " + ex.SocketErrorCode); }
            }
            if (networks.Any(entry => !Py.Truthy(((PyDict)entry).Get("joined")))) warnings.Add("部分网络接口无法接收局域网广播，其他接口仍在查找；也可以手动输入地址。");
            if (memberships == 0) udp.JoinMulticastGroup(Group);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var sender = new IPEndPoint(IPAddress.Any, 0); var bytes = udp.Receive(ref sender);
                    var row = Parse(bytes, sender); if (row == null) continue;
                    if (rows.Count < 32) rows[Py.S(row.Get("host")) + ":" + Py.S(row.Get("port"))] = row;
                }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock) { }
            }
        }
        catch (Exception ex) when (ex is SocketException || ex is NetworkInformationException)
        { warnings.Add("局域网广播暂不可用，已尝试检测本机 Java 服务；可以手动填写地址。"); System.Diagnostics.Trace.TraceWarning("Minecraft LAN scan: " + ex.Message); }
        return rows.Values.ToList();
    }

    private static IEnumerable<object> LocalJavaEndpoints(List<string> warnings)
    {
        var pids = new HashSet<int>();
        foreach (string name in new[] { "java", "javaw" })
            foreach (var process in Process.GetProcessesByName(name)) using (process) pids.Add(process.Id);
        if (pids.Count == 0) return Array.Empty<object>();
        var rows = new List<object>();
        try
        {
            ReadListeners(2, pids, rows); ReadListeners(23, pids, rows);
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException)
        { warnings.Add("本机端口枚举不可用，局域网广播结果仍可使用。"); System.Diagnostics.Trace.TraceWarning("Minecraft Java listener lookup: " + ex.Message); }
        return rows.Take(24);
    }

    private static void ReadListeners(int family, HashSet<int> javaPids, List<object> rows)
    {
        int size = 0; uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
        if (status == 0 && size <= 4) return;
        if (status != 122 || size < 4 || size > 4 * 1024 * 1024) throw new InvalidOperationException("Java listener table: " + status);
        IntPtr table = Marshal.AllocHGlobal(size);
        try
        {
            status = GetExtendedTcpTable(table, ref size, false, family, 3, 0);
            if (status != 0) throw new InvalidOperationException("Java listener table changed: " + status);
            int count = Marshal.ReadInt32(table), stride = family == 2 ? 24 : 56;
            if (count < 0 || count > (size - 4) / stride) return;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = IntPtr.Add(table, 4 + i * stride);
                int pid = Marshal.ReadInt32(row, family == 2 ? 20 : 52);
                if (!javaPids.Contains(pid)) continue;
                int rawPort = Marshal.ReadInt32(row, family == 2 ? 8 : 20);
                int port = (ushort)IPAddress.NetworkToHostOrder((short)rawPort);
                if (port == 0) continue;
                byte[] bytes = new byte[family == 2 ? 4 : 16]; Marshal.Copy(IntPtr.Add(row, family == 2 ? 4 : 0), bytes, 0, bytes.Length);
                var ip = new IPAddress(bytes);
                string host = ip.Equals(IPAddress.Any) ? "127.0.0.1" : ip.Equals(IPAddress.IPv6Any) ? "::1" : ip.ToString();
                if (host.Contains("%")) continue;
                rows.Add(PyDict.Of("host", host, "port", port, "name", "本机 Minecraft 世界", "source", "local"));
            }
        }
        finally { Marshal.FreeHGlobal(table); }
    }
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sorted, int family, int tableClass, uint reserved);
}
