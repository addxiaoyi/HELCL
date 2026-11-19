using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private bool launchOptionsReady = true;
    private string SelectedLoader => (LoaderPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";

    private void LoadLaunchOptions(string name)
    {
        var options = new PortableLaunchOptions();
        launchOptionsReady = true;
        LaunchOptionsNotice.Text = "设置随当前实例保存，启动前会再次校验。";
        try { options = launcher.LaunchOptions(name); }
        catch (Exception error) when (error is IOException or System.IO.InvalidDataException)
        {
            launchOptionsReady = false;
            LaunchOptionsNotice.Text = error.Message + " 请填写并保存正确设置。";
        }
        MemoryBox.Text = options.MemoryMb.ToString(CultureInfo.InvariantCulture);
        WindowWidthBox.Text = options.Width.ToString(CultureInfo.InvariantCulture);
        WindowHeightBox.Text = options.Height.ToString(CultureInfo.InvariantCulture);
    }

    private PortableLaunchOptions ReadLaunchOptions()
    {
        if (!int.TryParse(MemoryBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int memory) ||
            !int.TryParse(WindowWidthBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(WindowHeightBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            throw new ArgumentException("内存和窗口尺寸必须是整数。请检查启动设置。");
        return new PortableLaunchOptions(memory, width, height).Validate();
    }

    private async void SaveLaunchOptions(object? sender, RoutedEventArgs e) => await RunTask(_ =>
    {
        string name = SelectedName ?? throw new InvalidOperationException("请先选择实例。");
        launcher.SaveLaunchOptions(name, ReadLaunchOptions());
        launchOptionsReady = true;
        UpdateLaunchButton();
        LaunchOptionsNotice.Text = "启动设置已保存。";
        SetStatus("已保存当前实例的内存和窗口尺寸。");
        return Task.CompletedTask;
    });

    private void LoaderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (LoaderVersionPicker == null || LoaderNotice == null) return;
        ResetLoaderVersions();
    }

    private void ResetLoaderVersions()
    {
        LoaderVersionPicker.ItemsSource = Array.Empty<LoaderVersion>();
        LoaderNotice.Text = SelectedLoader == "none" ? "安装原版 Minecraft。"
            : "先获取与所选 Minecraft 版本兼容的加载器，再选择具体版本。";
        UpdateInstallSelection();
    }

    private void LoaderVersionChanged(object? sender, SelectionChangedEventArgs e) => UpdateInstallSelection();

    private void UpdateInstallSelection()
    {
        if (InstallButton == null || RefreshLoaderButton == null || LoaderVersionPicker == null) return;
        bool selected = RemoteVersions.SelectedItem is string;
        RefreshLoaderButton.IsEnabled = selected && SelectedLoader != "none";
        LoaderVersionPicker.IsEnabled = SelectedLoader != "none";
        InstallButton.IsEnabled = selected && (SelectedLoader == "none" || LoaderVersionPicker.SelectedItem is LoaderVersion);
    }

    private async void RefreshLoaderVersions(object? sender, RoutedEventArgs e) => await RunTask(async token =>
    {
        string version = RemoteVersions.SelectedItem as string ?? throw new InvalidOperationException("请先选择 Minecraft 版本。");
        string loader = SelectedLoader;
        if (loader == "none") return;
        LoaderNotice.Text = "正在获取加载器版本…";
        LoaderVersionPicker.ItemsSource = Array.Empty<LoaderVersion>();
        try
        {
            var versions = await Task.Run(() => launcher.LoaderVersions(version, loader, token), token);
            token.ThrowIfCancellationRequested();
            LoaderVersionPicker.ItemsSource = versions;
            LoaderVersionPicker.SelectedIndex = versions.Count == 0 ? -1 : 0;
            LoaderNotice.Text = versions.Count == 0 ? "此版本没有可用的加载器。请选择其他游戏版本或加载器。"
                : $"找到 {versions.Count} 个加载器版本。确认选择后安装。";
        }
        catch (Exception)
        {
            LoaderNotice.Text = token.IsCancellationRequested ? "获取已取消，可重新获取。" : "加载器版本获取失败，请检查网络后重试。";
            throw;
        }
        finally { UpdateInstallSelection(); }
    });
}
