#nullable enable
using System;
using System.Threading;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record DeviceAuthorization(string Code, Uri Uri, long ExpiresIn);

public sealed class PortableAccount
{
    private readonly AccountVault vault;
    private PyDict? account;
    private bool remember;
    public string DisplayName => account == null ? "" : Py.S(account.Get("name"));
    public bool IsSignedIn => account != null;
    public string Notice { get; private set; } = "尚未登录，可使用试玩模式。";

    public PortableAccount(AccountVault vault) => this.vault = vault;

    public void Restore()
    {
        try
        {
            account = vault.Load();
            if (account == null) return;
            Validate(account);
            remember = true;
            Notice = "账号已恢复，启动游戏前会检查授权。";
        }
        catch (Exception) { account = null; Notice = "未能恢复账号：钥匙环不可用、数据来自另一台电脑或记录损坏。请重新登录。"; }
    }

    public void Login(bool save, Action<DeviceAuthorization> onCode, Action<string> onStatus, CancellationToken token)
    {
        var auth = new MicrosoftAuthenticator(Config.MsClientId(), cancellation: token);
        PyDict signedIn = auth.Login((code, uri, expiry) => onCode(new DeviceAuthorization(Py.S(code), ValidateDeviceUri(Py.S(uri)), Py.Int(expiry))),
            onStatus, open_browser: false);
        token.ThrowIfCancellationRequested();
        Validate(signedIn);
        account = signedIn;
        remember = save;
        if (save) Persist();
        else
        {
            try { vault.Remove(); Notice = "已登录，仅在本次会话使用。"; }
            catch (Exception) { Notice = "本次登录仅存于内存；旧账号记录无法移除，请检查数据目录权限。"; }
        }
    }

    public PyDict ForLaunch(CancellationToken token, bool forceRefresh = false)
    {
        token.ThrowIfCancellationRequested();
        if (account == null) throw new InvalidOperationException("请先登录微软账号，或选择试玩模式。");
        if (forceRefresh || Py.Float(account.Get("expires_at", 0L)) <= Py.Time() + 60)
        {
            var auth = new MicrosoftAuthenticator(Config.MsClientId(), cancellation: token);
            PyDict refreshed = auth.Refresh(new PyDict(account));
            auth.CheckEntitlements(refreshed["access_token"]);
            token.ThrowIfCancellationRequested();
            Validate(refreshed);
            if (!Guid.Parse(Py.S(account.Get("uuid"))).Equals(Guid.Parse(Py.S(refreshed.Get("uuid")))))
                throw new InvalidOperationException("刷新后的玩家身份发生变化，请重新登录。");
            account = refreshed;
            if (remember) Persist();
            else Notice = "授权已刷新，仅在本次会话使用。";
        }
        return PyDict.Of("name", account["name"], "uuid", account["uuid"], "token", account["access_token"],
            "user_type", "msa", "xuid", account.Get("xuid", ""));
    }

    public void Logout()
    {
        vault.Remove();
        account = null;
        remember = false;
        Notice = vault.CleanupWarning ?? "已退出登录，保存的账号密文已移除。";
    }

    private void Persist()
    {
        try { vault.Save(account!); Notice = vault.CleanupWarning ?? "已使用本机系统凭据库保护登录。换电脑后需要重新登录。"; }
        catch (Exception) { Notice = "已登录，但系统凭据库或数据目录不可用，本次更新仅保留在内存；关闭后可能需要重新登录。"; }
    }

    private static void Validate(PyDict value)
    {
        if (Py.S(value.Get("type")) != "microsoft" || string.IsNullOrWhiteSpace(Py.S(value.Get("name")))
            || !Guid.TryParse(Py.S(value.Get("uuid")), out _) || string.IsNullOrEmpty(Py.S(value.Get("access_token")))
            || double.IsNaN(Py.Float(value.Get("expires_at", 0L))) || double.IsInfinity(Py.Float(value.Get("expires_at", 0L))))
            throw new InvalidOperationException("账号信息无效，请重新登录。");
    }

    public static Uri ValidateDeviceUri(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || (uri.Host != "www.microsoft.com" && uri.Host != "microsoft.com"
                && uri.Host != "login.live.com" && uri.Host != "login.microsoftonline.com"))
            throw new InvalidOperationException("登录服务返回了非预期的授权地址，请稍后重试。");
        return uri;
    }
}
