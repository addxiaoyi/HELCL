#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HelL.Core.Platform;

namespace HelL.Portable;

public static class SystemSecretStore
{
    public static ISecretStore Create() => OperatingSystem.IsWindows() ? new WinCredSecretStore()
        : OperatingSystem.IsMacOS() ? new MacKeychainStore() : new LinuxSecretStore();
}

/// <summary>Only short encryption keys are stored here; tokens never appear in process arguments.</summary>
internal sealed class LinuxSecretStore : ISecretStore
{
    private const string Tool = "/usr/bin/secret-tool";

    public string GetPassword(string service, string username) => Run(null, "lookup", "service", service, "account", username);
    public void SetPassword(string service, string username, string password) =>
        Run(password, "store", "--label=HelL portable account key", "service", service, "account", username);
    public void DeletePassword(string service, string username) => Run(null, "clear", "service", service, "account", username);

    private static string Run(string? password, params string[] arguments)
    {
        if (!System.IO.File.Exists(Tool))
            throw new InvalidOperationException("系统凭据库不可用：请安装 libsecret-tools，并在桌面会话中解锁登录钥匙环。账号仍可仅在本次会话使用。");
        var start = new ProcessStartInfo(Tool)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法访问系统凭据库。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (password != null) process.StandardInput.WriteLine(password);
        process.StandardInput.Close();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("系统钥匙环未及时响应，请解锁后重新登录；本次不会明文保存令牌。");
        }
        string output = stdout.GetAwaiter().GetResult().TrimEnd('\r', '\n');
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode == 0) return output;
        if (process.ExitCode == 1 && error.Length == 0 && arguments[0] != "store") return "";
        throw new InvalidOperationException("系统凭据库拒绝访问，请确认桌面密钥环已解锁。令牌不会明文保存。");
    }
}

internal sealed class MacKeychainStore : ISecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const int Missing = -25300;

    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain,
        uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr password, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
        uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] password, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] password);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr password);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);

    public string GetPassword(string service, string username)
    {
        int status = Find(service, username, out uint length, out IntPtr password, out IntPtr item);
        try
        {
            if (status == Missing) return "";
            Check(status);
            if (length > 4096) throw new InvalidOperationException("钥匙串中的应用密钥格式异常，请重新登录。");
            byte[] bytes = new byte[length];
            try { Marshal.Copy(password, bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { Release(password, item); }
    }

    public void SetPassword(string service, string username, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int status = Find(service, username, out _, out IntPtr password, out IntPtr item);
        try
        {
            if (status == Missing)
            {
                byte[] serviceBytes = Encoding.UTF8.GetBytes(service), accountBytes = Encoding.UTF8.GetBytes(username);
                Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
                    (uint)accountBytes.Length, accountBytes, (uint)bytes.Length, bytes, out item));
            }
            else { Check(status); Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes)); }
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); Release(password, item); }
    }

    public void DeletePassword(string service, string username)
    {
        int status = Find(service, username, out _, out IntPtr password, out IntPtr item);
        try { if (status == Missing) return; Check(status); Check(SecKeychainItemDelete(item)); }
        finally { Release(password, item); }
    }

    private static int Find(string service, string account, out uint length, out IntPtr password, out IntPtr item)
    {
        byte[] serviceBytes = Encoding.UTF8.GetBytes(service), accountBytes = Encoding.UTF8.GetBytes(account);
        return SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes,
            (uint)accountBytes.Length, accountBytes, out length, out password, out item);
    }

    private static void Release(IntPtr password, IntPtr item)
    {
        if (password != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, password);
        if (item != IntPtr.Zero) CFRelease(item);
    }

    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"无法访问 macOS 登录钥匙串（状态 {status}）。请解锁钥匙串或允许 HelL 访问后重试。");
    }
}
