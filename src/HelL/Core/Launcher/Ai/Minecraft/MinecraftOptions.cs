using System;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftOptions
{
    public static PyDict Validate(string host, int port, string name, string auth, string version)
    {
        host = (host ?? "").Trim(); name = (name ?? "").Trim(); version = (version ?? "").Trim();
        if (host.Length == 0 || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains("%"))
            throw new ArgumentException("请输入服务器主机名或 IP，不要带协议和端口。");
        if (port < 1 || port > 65535) throw new ArgumentException("端口必须在 1 至 65535 之间。");
        if (auth != "offline" && auth != "microsoft") throw new ArgumentException("登录方式无效。");
        if (auth == "offline" && !Regex.IsMatch(name, @"\A[A-Za-z0-9_]{3,16}\z")) throw new ArgumentException("玩家名需为 3 至 16 位字母、数字或下划线。");
        if (auth == "microsoft" && (name.Length < 1 || name.Length > 128 || name.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
            throw new ArgumentException("请填写此机器人账号的本地标识，用于区分登录缓存。");
        if (!Regex.IsMatch(version, @"\A(?:\d+\.\d+(?:\.\d+)?)?\z")) throw new ArgumentException("游戏版本应为 1.21.1 这样的版本号，或留空自动检测。");
        return PyDict.Of("host", host, "port", port, "name", name, "auth", auth, "version", version);
    }
}
