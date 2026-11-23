using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private PortableAccount? account;
    private Uri? authorizationUri;

    private async void RestoreAccount(object? sender, EventArgs e)
    {
        if (Program.UiSmoke) return;
        var vault = new AccountVault(DataRoot, SystemSecretStore.Create());
        account = new PortableAccount(vault);
        if (!vault.HasSavedAccount) { UpdateAccount(); return; }
        await RunTask(async token =>
        {
            Status.Text = "正在恢复保存的账号…";
            await Task.Run(account.Restore, token);
            UpdateAccount();
            Status.Text = account.Notice;
        });
    }

    private async void LoginMicrosoft(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        account ??= new PortableAccount(new AccountVault(Program.DataRoot, SystemSecretStore.Create()));
        bool remember = RememberAccount.IsChecked == true;
        Status.Text = "正在请求微软授权码…";
        try
        {
            await Task.Run(() => account.Login(remember, ShowAuthorization,
                message => Dispatcher.UIThread.Post(() => { if (task != null && !token.IsCancellationRequested) Status.Text = message; }), token), token);
            DemoMode.IsChecked = false;
            Status.Text = "微软登录完成。";
        }
        finally { ClearAuthorization(); UpdateAccount(); }
    });

    private void ShowAuthorization(DeviceAuthorization authorization) => Dispatcher.UIThread.Post(() =>
    {
        if (task == null || task.IsCancellationRequested) return;
        authorizationUri = authorization.Uri;
        DeviceCode.Text = authorization.Code;
        DeviceAddress.Text = authorization.Uri.AbsoluteUri;
        DeviceExpiry.Text = $"请在 {Math.Ceiling(authorization.ExpiresIn / 60.0)} 分钟内完成授权。可随时取消并重新获取代码。";
        AuthorizationPanel.IsVisible = true;
        AuthorizationPanel.BringIntoView();
    });

    private void OpenAuthorization(object? sender, RoutedEventArgs e)
    {
        if (authorizationUri == null) return;
        try
        {
            var uri = PortableAccount.ValidateDeviceUri(authorizationUri.AbsoluteUri);
            var start = new ProcessStartInfo { UseShellExecute = OperatingSystem.IsWindows() };
            if (OperatingSystem.IsWindows()) start.FileName = uri.AbsoluteUri;
            else
            {
                start.FileName = OperatingSystem.IsMacOS() ? "/usr/bin/open" : "/usr/bin/xdg-open";
                start.ArgumentList.Add(uri.AbsoluteUri);
            }
            using var browser = Process.Start(start);
        }
        catch (Exception) { Status.Text = "无法自动打开浏览器，请复制上方微软地址手动打开，代码仍然有效。"; }
    }

    private async void RefreshAccount(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        if (account == null) throw new InvalidOperationException("请先登录微软账号。");
        Status.Text = "正在刷新微软授权…";
        await Task.Run(() => account.ForLaunch(token, forceRefresh: true), token);
        UpdateAccount();
        Status.Text = "授权刷新完成。";
    });

    private async void LogoutAccount(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        if (account != null) await Task.Run(account.Logout, token);
        DemoMode.IsChecked = true;
        UpdateAccount();
        Status.Text = "已退出登录。";
    });

    private void UpdateAccount()
    {
        bool signedIn = account?.IsSignedIn == true;
        AccountName.Text = signedIn ? account!.DisplayName : "微软账号";
        AccountNotice.Text = account?.Notice ?? "尚未登录，可使用试玩模式。";
        RefreshAccountButton.IsVisible = LogoutButton.IsVisible = signedIn;
        LoginButton.Content = signedIn ? "重新登录" : "登录微软";
        UpdateLaunchButton();
    }

    private void ClearAuthorization()
    {
        AuthorizationPanel.IsVisible = false;
        DeviceCode.Text = DeviceAddress.Text = DeviceExpiry.Text = "";
        authorizationUri = null;
    }

    private void SwitchLaunchMode(object? sender, RoutedEventArgs e) => UpdateLaunchButton();

    private void UpdateLaunchButton()
    {
        if (LaunchButton == null || FirstInstallButton == null || InstallAnotherVersionButton == null || DemoMode == null || LaunchNotice == null) return;
        bool hasVersions = InstalledVersions.ItemCount > 0;
        bool installed = InstalledVersions.SelectedItem != null;
        bool demo = DemoMode.IsChecked == true;
        bool signedIn = account?.IsSignedIn == true;
        LaunchButton.Content = DemoMode.IsChecked == true ? "启动试玩" : "启动游戏";
        FirstInstallButton.IsVisible = !hasVersions;
        FirstInstallButton.IsEnabled = SelectedName != null && !IsWorking;
        LaunchButton.IsVisible = hasVersions;
        InstallAnotherVersionButton.IsVisible = hasVersions;
        LaunchButton.IsEnabled = installed && launchOptionsReady && (demo || signedIn);
        LaunchNotice.Text = !launchOptionsReady ? "启动设置读取失败。请展开内存与窗口，修复并保存后再启动。"
            : !hasVersions ? "先安装游戏版本，即可启动此实例。"
            : !installed ? "请选择已安装的游戏版本。"
            : !demo && !signedIn ? "启动正式游戏需要登录微软账号，也可以勾选试玩模式。"
            : $"Minecraft {InstalledVersions.SelectedItem} · {(demo ? "试玩模式" : "微软账号启动")}";
    }
}
