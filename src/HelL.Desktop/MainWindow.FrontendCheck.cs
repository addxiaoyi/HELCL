using System.Reflection;
using Avalonia;
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

        AiView.PrefillPrompt("preserved draft", log: false);
        bool loaded = await AiView.LoadLogReviewAsync(instance, "rotated.log", _ => Task.FromException<string>(new FileNotFoundException("private-log-path")));
        if (loaded || AiView.FindControl<TextBox>("PromptInput")!.Text != "preserved draft" || Status.Text?.Contains("private-log-path") == true)
            throw new IOException("A rotated log replaced the draft or leaked its read failure.");
        await CheckImportTarget(instance);
        await CheckResourceBinding();
        await CheckDocumentReleaseBinding();
        await CheckDesktopRecheckSafetyAsync();
        await CheckDesktopDetailsAsync();
        await CheckDesktopIconsAsync();
        await CheckDesktopRefinementAsync();
        await CheckDesktopReadingAsync();

        var document = new MessageDocument { Text = "# Heading\ntext\n```sh\necho fixture\n```" };
        if (document.Content is not StackPanel { Children.Count: > 2 }) throw new IOException("Structured AI document did not render its code block.");
        Width = 800; Height = 600;
        await Task.Delay(150);
        ApplyFrontendLayout();
        if (ShellColumns.ColumnDefinitions[0].Width.Value > 164 || !Navigation.IsEnabled)
            throw new IOException("Narrow desktop navigation did not reflow.");
        ShowPage("launch");
        Width = 800; Height = 760;
        ApplyFrontendLayout();
        await Task.Delay(120);
        static Rect BoundsIn(Control control, Control parent)
        {
            Point position = control.TranslatePoint(default, parent) ?? default;
            return new Rect(position, control.Bounds.Size);
        }
        static bool Overlaps(Rect left, Rect right) =>
            left.Left < right.Right - 1 && left.Right > right.Left + 1 &&
            left.Top < right.Bottom - 1 && left.Bottom > right.Top + 1;
        bool narrowWorkspaceStacked = LaunchWorkspaceColumns.ColumnDefinitions[1].Width.Value == 0
            && Grid.GetRow(LaunchQuickPanel) == 1 && Grid.GetColumn(LaunchQuickPanel) == 0;
        if (!narrowWorkspaceStacked)
            throw new IOException("At 800px, launcher environment cards must move below the settings form.");

        Rect accountName = BoundsIn(AccountName, AccountPanel);
        Rect accountNotice = BoundsIn(AccountNotice, AccountPanel);
        Rect demoMode = BoundsIn(DemoMode, AccountPanel);
        Rect loginButton = BoundsIn(LoginButton, AccountPanel);
        Rect rememberAccount = BoundsIn(RememberAccount, AccountPanel);
        bool accountFits = demoMode.Right <= AccountPanel.Bounds.Width + 1
            && loginButton.Right <= AccountPanel.Bounds.Width + 1
            && demoMode.Top >= Math.Max(accountName.Bottom, accountNotice.Bottom) - 1
            && loginButton.Top >= Math.Max(accountName.Bottom, accountNotice.Bottom) - 1
            && rememberAccount.Top >= Math.Max(demoMode.Bottom, loginButton.Bottom) - 1;
        if (!accountFits)
            throw new IOException("At 800px, account actions or the remember option overlap account identity text.");

        Rect memory = BoundsIn(MemoryBox, LaunchOptionsPanel);
        Rect widthField = BoundsIn(WindowWidthBox, LaunchOptionsPanel);
        Rect heightField = BoundsIn(WindowHeightBox, LaunchOptionsPanel);
        Rect saveOptions = BoundsIn(SaveLaunchOptionsButton, LaunchOptionsPanel);
        Rect javaPath = BoundsIn(JavaPath, JavaSettingsPanel);
        Rect detectJava = BoundsIn(DetectJavaButton, JavaSettingsPanel);
        Rect browseJava = BoundsIn(BrowseJavaButton, JavaSettingsPanel);
        bool launchOptionsFit = !Overlaps(memory, widthField) && !Overlaps(widthField, heightField)
            && saveOptions.Left >= Math.Max(memory.Right, Math.Max(widthField.Right, heightField.Right)) - 1
            && detectJava.Left >= javaPath.Right - 1 && browseJava.Left >= detectJava.Right - 1;
        if (!launchOptionsFit)
            throw new IOException("At 800px, memory, game dimensions, save and Java controls must not overlap.");
        File.WriteAllText(Path.Combine(Program.DataRoot, "frontend-recovery.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = true, filteredBatch = true, cancelledDeletePreservesFile = true,
            structuredCodeBlock = true, narrowNavigation = true, persistentTaskHistory = File.Exists(Path.Combine(Program.DataRoot, "desktop-task-history.json")),
            logRotationPreservesDraft = true, fixedImportTarget = true, declinedOverwritePreservesFile = true,
            invalidImportTargetNotRecreated = true, narrowLaunchReflow = true, narrowAccountNoOverlap = true,
            narrowLaunchOptionsNoOverlap = true, staleSearchRejected = true, staleReleasesRejected = true, staleDocumentsRejected = true,
            nativeScreenReaderVerified = false
        }));
    }

    private async Task CheckImportTarget(string original)
    {
        const string other = "frontend-other-import-target";
        launcher.CreateInstance(other);
        string source = Path.Combine(Program.DataRoot, "import-fixture.jar");
        File.WriteAllText(source, "incoming bytes");
        string originalFolder = Path.Combine(launcher.InstanceFolder(original), "mods");
        string destination = Path.Combine(originalFolder, Path.GetFileName(source));
        ModsView.Reload(other);
        await ModsView.ImportPathsAsync(original, "mods", [source]);
        if (!File.Exists(destination) || File.Exists(Path.Combine(launcher.InstanceFolder(other), "mods", Path.GetFileName(source))))
            throw new IOException("Import used the newly selected instance instead of its original target.");
        File.WriteAllText(destination, "installed bytes");
        DesktopDialogs.Confirmation = (_, body) => Task.FromResult(false);
        try { await ModsView.ImportPathsAsync(original, "mods", [source]); }
        finally { DesktopDialogs.Confirmation = null; }
        if (File.ReadAllText(destination) != "installed bytes") throw new IOException("A declined overwrite replaced an installed file.");
        string removedFolder = launcher.InstanceFolder(other);
        Directory.Delete(removedFolder, true);
        bool refused = false;
        try
        {
            await ModsView.ImportPathsAsync(other, "mods", [source]);
        }
        catch (IOException) { refused = true; }
        if (!refused || Directory.Exists(removedFolder)) throw new IOException("A removed import target was accepted or recreated.");
    }
}
