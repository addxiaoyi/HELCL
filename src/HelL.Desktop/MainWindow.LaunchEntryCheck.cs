using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Media.Imaging;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckLaunchEntryAsync()
    {
        var versions = InstalledVersions.ItemsSource;
        var selected = InstalledVersions.SelectedItem;
        string page = currentPage;
        double width = Width;
        bool? demo = DemoMode.IsChecked;
        string instance = SelectedName ?? throw new IOException("The launch entry check needs an isolated instance.");
        try
        {
            ShowPage("launch");
            Width = 800;
            InstalledVersions.ItemsSource = Array.Empty<string>();
            UpdateLaunchButton();
            await SettleReading();
            if (!FirstInstallButton.IsVisible || !FirstInstallButton.IsEnabled || LaunchButton.IsVisible || InstallAnotherVersionButton.IsVisible)
                throw new IOException("An empty instance does not expose installation as its only primary action.");
            SaveIconWindow(Path.Combine(Program.DataRoot, "desktop-first-install-800.png"));
            FirstInstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!InstallerView.IsVisible || (InstallerView.FindControl<ComboBox>("InstancePicker")!.SelectedItem as InstanceEntry)?.Name != instance)
                throw new IOException("The first-install action did not open the installer for the current instance.");

            ShowPage("launch");
            InstalledVersions.ItemsSource = new[] { "1.21.1" };
            InstalledVersions.SelectedIndex = 0;
            DemoMode.IsChecked = true;
            UpdateLaunchButton();
            if (FirstInstallButton.IsVisible || !LaunchButton.IsVisible || !LaunchButton.IsEnabled || !InstallAnotherVersionButton.IsVisible)
                throw new IOException("A prepared instance did not restore the launch and additional-install actions.");
            InstalledVersions.SelectedIndex = -1;
            if (FirstInstallButton.IsVisible || LaunchButton.IsEnabled || !LaunchButton.IsVisible)
                throw new IOException("Clearing the selected installed version incorrectly offered first installation.");

            if (ToolTip.GetTip(InstanceNameButton)?.ToString() != instance || AutomationProperties.GetHelpText(InstanceNameButton) != instance)
                throw new IOException("The truncated instance heading lost its full-name hint.");
            var flyout = (Flyout)InstanceNameButton.Flyout!;
            flyout.ShowAt(InstanceNameButton);
            await SettleReading();
            if (!flyout.IsOpen || flyout.Content is not SelectableTextBlock name || name.Text != instance
                || name.Bounds.Width <= 0 || name.Bounds.Height <= 0)
                throw new IOException("The full-name flyout did not contain the selected instance name.");
            var popup = TopLevel.GetTopLevel(name) ?? throw new IOException("The full-name flyout has no visible popup.");
            using (var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)), new Vector(96, 96)))
            {
                image.Render(popup);
                image.Save(Path.Combine(Program.DataRoot, "desktop-full-instance-name.png"), new PngBitmapEncoderOptions());
            }
            flyout.Hide();
            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-launch-entry.json"), JsonSerializer.Serialize(new
            { ok = true, firstInstallTarget = true, installedLaunchRestored = true, emptySelectionHandled = true, fullNameHintAndFlyout = true, gameLaunched = false }));
        }
        finally
        {
            InstanceNameButton.Flyout?.Hide();
            InstalledVersions.ItemsSource = versions;
            InstalledVersions.SelectedItem = selected;
            DemoMode.IsChecked = demo;
            Width = width;
            UpdateLaunchButton();
            ShowPage(page);
        }
    }
}
