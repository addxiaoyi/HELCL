using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Desktop.Controls;
using HelL.Desktop.Services;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckFrontendRecovery()
    {
        string instance = SelectedName ?? throw new IOException("The frontend check needs a selected isolated instance.");
        string mods = Path.Combine(launcher.InstanceFolder(instance), "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(mods, "visible-fixture.jar"), [0x50, 0x4b]);
        File.WriteAllBytes(Path.Combine(mods, "hidden-fixture.jar"), [0x50, 0x4b]);
        ShowPage("mods");
        ModsView.FindControl<TextBox>("SearchBox")!.Text = "visible-fixture";
        var disable = typeof(Views.ModsManagerView).GetMethod("DisableAllMods", BindingFlags.Instance | BindingFlags.NonPublic)!;
        disable.Invoke(ModsView, [this, new RoutedEventArgs()]);
        for (int attempt = 0; attempt < 100 && IsWorking; attempt++) await Task.Delay(20);
        if (!File.Exists(Path.Combine(mods, "visible-fixture.jar.disabled")) || !File.Exists(Path.Combine(mods, "hidden-fixture.jar")))
            throw new IOException("Filtered batch operation affected a hidden resource.");

        int prompts = 0;
        DesktopDialogs.Confirmation = (_, _) => { prompts++; return Task.FromResult(false); };
        try
        {
            var remove = typeof(Views.ModsManagerView).GetMethod("DeleteContentRow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            remove.Invoke(ModsView, [new Button { Tag = "hidden-fixture.jar" }, new RoutedEventArgs()]);
            await Task.Delay(50);
            if (prompts != 1 || !File.Exists(Path.Combine(mods, "hidden-fixture.jar")))
                throw new IOException("Cancelled destructive confirmation removed content.");
        }
        finally { DesktopDialogs.Confirmation = null; }

        var document = new MessageDocument { Text = "# Heading\ntext\n```sh\necho fixture\n```" };
        if (document.Content is not StackPanel { Children.Count: > 2 }) throw new IOException("Structured AI document did not render its code block.");
        Width = 800; Height = 600;
        await Task.Delay(150);
        ApplyFrontendLayout();
        if (ShellColumns.ColumnDefinitions[0].Width.Value > 164 || !Navigation.IsEnabled)
            throw new IOException("Narrow desktop navigation did not reflow.");
        File.WriteAllText(Path.Combine(Program.DataRoot, "frontend-recovery.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = true, filteredBatch = true, cancelledDeletePreservesFile = true,
            structuredCodeBlock = true, narrowNavigation = true, persistentTaskHistory = File.Exists(Path.Combine(Program.DataRoot, "desktop-task-history.json")),
            nativeScreenReaderVerified = false
        }));
    }
}
