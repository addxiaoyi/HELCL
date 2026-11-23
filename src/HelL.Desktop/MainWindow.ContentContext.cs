using Avalonia.Controls;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, string> contentVersions = new(StringComparer.Ordinal);
    private bool changingInstance;

    internal Instance ContentInstance(string name, string kind = "")
    {
        var instance = new Instance(name);
        string? version = name == SelectedName ? InstalledVersions.SelectedItem as string : contentVersions.GetValueOrDefault(name);
        if (!string.IsNullOrEmpty(version) && instance.HasVersion(version))
        {
            var settings = VersionSettings.Load(instance, version);
            string game = VersionSettings.GameDir(instance, version, settings);
            string isolation = Py.S(settings.Get("isolation"));
            bool shared = isolation == "saves" && VersionSettings.SHARED_LINKS.Contains(kind)
                || isolation == "mods" && kind is "saves" or "resourcepacks" or "shaderpacks" or "screenshots";
            if (shared)
            {
                string linked = Path.Combine(game, kind);
                // Isolation preserves a populated real directory instead of replacing it with a shared link.
                if (Directory.Exists(linked) && (File.GetAttributes(linked) & FileAttributes.ReparsePoint) == 0
                    && Directory.EnumerateFileSystemEntries(linked).Any())
                {
                    instance.path = game;
                    return instance;
                }
                if (Directory.Exists(linked) && (File.GetAttributes(linked) & FileAttributes.ReparsePoint) != 0)
                {
                    string expected = Path.GetFullPath(Path.Combine(instance.path, kind));
                    string? resolved = Directory.ResolveLinkTarget(linked, true)?.FullName;
                    if (!string.Equals(expected, resolved, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new IOException("共享目录指向了预期之外的位置，请先修复版本隔离设置。");
                }
                DependencyTarget.RequireLocalPath(Path.Combine(instance.path, kind));
            }
            else instance.path = game;
        }
        return instance;
    }

    internal string ContentLabel(string name)
    {
        string? version = name == SelectedName ? InstalledVersions.SelectedItem as string : contentVersions.GetValueOrDefault(name);
        return string.IsNullOrEmpty(version) ? name + " · 实例共享目录" : name + " · " + version;
    }

    private void RefreshContentPages()
    {
        if (SelectedName is not { } name) return;
        if (ModsView.IsVisible) ModsView.Reload(name);
        if (SavesView.IsVisible) SavesView.Reload(name);
        if (ServersView.IsVisible) ServersView.Reload(name);
    }

    internal void ChooseContentVersion()
    {
        ShowPage("launch");
        InstalledVersions.BringIntoView();
        InstalledVersions.Focus();
    }
}
