using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private PortableAccount? account => session.ExistingAccount;
    private Uri? authorizationUri;
    private PortableAccount Account => session.Account;

    private async void RestoreAccount(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (session.AccountRestored) { UpdateAccount(); return; }
        await Run(async token =>
        {
            if (session.AccountRestored) { UpdateAccount(); return; }
            await Task.Run(Account.Restore, token);
            session.AccountRestored = true;
            UpdateAccount();
        }, allowNavigation: true);
    }

    private async void LoginMicrosoft(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        var session = Account;
        bool remember = RememberAccount.IsChecked == true;
        Status.Text = "正在请求微软授权码…";
        try
        {
            await Task.Run(() => session.Login(remember,
                authorization => Dispatcher.UIThread.Post(() => ShowAuthorization(authorization, token)),
                message => Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && !token.IsCancellationRequested && operation?.Token == token) Status.Text = message;
                }), token), token);
            Status.Text = "微软登录完成。";
        }
        finally { ClearAuthorization(); UpdateAccount(); }
    }, allowNavigation: true);

    private void ShowAuthorization(DeviceAuthorization authorization, CancellationToken token)
    {
        if (disposed || token.IsCancellationRequested || operation == null || operation.Token != token) return;
        authorizationUri = PortableAccount.ValidateDeviceUri(authorization.Uri.AbsoluteUri);
        DeviceCode.Text = authorization.Code;
        DeviceAddress.Text = authorizationUri.AbsoluteUri;
        DeviceExpiry.Text = $"请在 {Math.Ceiling(authorization.ExpiresIn / 60.0)} 分钟内完成授权。";
        AuthorizationPanel.IsVisible = true;
        AuthorizationPanel.BringIntoView();
    }

    private async void OpenAuthorization(object? sender, RoutedEventArgs e)
    {
        if (authorizationUri == null) return;
        try
        {
            var top = TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("界面尚未就绪。");
            bool opened = await top.Launcher.LaunchUriAsync(PortableAccount.ValidateDeviceUri(authorizationUri.AbsoluteUri));
            if (!opened) throw new InvalidOperationException("系统未打开浏览器。");
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("MobileHome.Account OpenAuthorization: " + error.Message);
            Status.Text = "无法打开浏览器，请复制上方微软地址手动打开，验证码仍然有效。";
        }
    }

    private async void CopyAuthorizationCode(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(DeviceCode.Text)) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("系统剪贴板不可用。");
            await clipboard.SetTextAsync(DeviceCode.Text);
            Status.Text = "验证码已复制，在微软页面粘贴即可。";
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("MobileHome.Account CopyAuthorizationCode: " + error.Message);
            Status.Text = "无法复制验证码，请手动输入上方代码。";
        }
    }

    private async void RefreshAccount(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        if (account?.IsSignedIn != true) throw new InvalidOperationException("请先登录微软账号。");
        Status.Text = "正在刷新微软授权…";
        await Task.Run(() => account.ForLaunch(token, forceRefresh: true), token);
        Status.Text = "授权刷新完成。";
    }, allowNavigation: true);

    private async void LogoutAccount(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        if (account != null) await Task.Run(account.Logout, token);
        Status.Text = account?.Notice ?? "已退出登录。";
    }, allowNavigation: true);

    private void UpdateAccount()
    {
        bool signedIn = account?.IsSignedIn == true;
        bool busy = operation != null;
        AccountName.Text = signedIn ? account!.DisplayName : "微软账号";
        AccountNotice.Text = account?.Notice ?? "在系统浏览器完成授权，HelL 不会读取你的密码。";
        if (HomeAccountLabel != null) HomeAccountLabel.Text = signedIn ? account!.DisplayName : "未登录";
        LoginButton.Content = signedIn ? "重新登录" : "登录微软账号";
        RefreshAccountButton.IsVisible = LogoutButton.IsVisible = signedIn;
        LoginButton.IsEnabled = RefreshAccountButton.IsEnabled = LogoutButton.IsEnabled = RememberAccount.IsEnabled = !busy;
    }

    private void ClearAuthorization()
    {
        AuthorizationPanel.IsVisible = false;
        DeviceCode.Text = DeviceAddress.Text = DeviceExpiry.Text = "";
        authorizationUri = null;
    }
}
