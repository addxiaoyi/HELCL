using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher.Ai.Minecraft;

internal static class MinecraftNetworkCheck
{
    internal static async Task Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses).Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork).Distinct().Take(8).ToList();
        var receipts = new List<object>();
        foreach (var address in addresses)
        {
            var listener = new TcpListener(address, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serving = Task.Run(async () =>
            {
                while (true)
                {
                    using var peer = await listener.AcceptTcpClientAsync();
                    byte[] request = new byte[512]; await peer.GetStream().ReadAsync(request, 0, request.Length);
                    byte[] payload = Packet(); await peer.GetStream().WriteAsync(payload, 0, payload.Length);
                }
            });
            try
            {
                var scan = MinecraftLanDiscovery.DiscoverAsync();
                using var udp = new UdpClient(new IPEndPoint(address, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes()); udp.MulticastLoopback = true;
                byte[] announcement = Encoding.UTF8.GetBytes("[MOTD]interface fixture[/MOTD][AD]" + port + "[/AD]");
                for (int i = 0; i < 12; i++) { await udp.SendAsync(announcement, announcement.Length, new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445)); await Task.Delay(120); }
                var found = await scan;
                bool discovered = Py.ListOr(found.Get("servers")).OfType<PyDict>().Any(row => Py.S(row.Get("host")) == address.ToString() && Py.Int(row.Get("port")) == port);
                receipts.Add(PyDict.Of("address", address.ToString(), "discovered", discovered, "networks", found.Get("networks"), "warnings", found.Get("warnings"), "unreachable", found.Get("unreachable")));
                Console.WriteLine("[NETWORK] " + address + " multicast discovery=" + discovered);
            }
            finally
            {
                listener.Stop();
                try { await serving; } catch (SocketException) { } catch (ObjectDisposedException) { }
            }
        }
        File.WriteAllText(Path.Combine(folder, "network-check.json"), PyJson.Dumps(PyDict.Of("interfaces", receipts, "scope", "same-host multicast through actual IPv4 interfaces; not cross-computer or firewall validation"), 2));
        if (!receipts.OfType<PyDict>().Any(row => Py.Truthy(row.Get("discovered")))) throw new IOException("No local interface received the multicast fixture.");
    }
    private static byte[] Packet()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"version\":{\"name\":\"1.21.1\",\"protocol\":767},\"description\":\"interface fixture\"}");
        using var body = new MemoryStream(); body.WriteByte(0); VarInt(body, json.Length); body.Write(json, 0, json.Length);
        using var packet = new MemoryStream(); VarInt(packet, (int)body.Length); body.WriteTo(packet); return packet.ToArray();
    }
    private static void VarInt(Stream stream, int value) { do { byte part = (byte)(value & 127); value >>= 7; stream.WriteByte((byte)(part | (value > 0 ? 128 : 0))); } while (value > 0); }
}
