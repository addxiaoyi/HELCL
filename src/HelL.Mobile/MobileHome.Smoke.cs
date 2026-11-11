using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Core;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private bool smokeStarted;
    private string EvidenceRoot => MobileRuntime.ReportRoot.Length > 0 ? MobileRuntime.ReportRoot : root;
    private async void RunSmoke(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (smokeStarted) return;
        smokeStarted = true;
        try
        {
            Directory.CreateDirectory(EvidenceRoot);
            await Task.Delay(800);
            if (instances.Count != 0) throw new InvalidOperationException("Mobile smoke requires isolated empty storage.");
            NewName.Text = "手机世界 · 中文验收";
            CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (instances.Count != 1 || launcher.ReferenceVersion(selected) != "1.21.1") throw new IOException("Mobile instance creation failed.");
            InstanceName.Text = "手机生存世界";
            InstanceGroup.Text = "移动工作区";
            SaveInstanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = launcher.LibraryList().Single();
            if (!saved.Pinned || saved.Group != "移动工作区" || saved.Name != "手机生存世界") throw new IOException("Mobile instance edits did not persist.");
            await Capture("library");
            LibraryPage.Offset = new Vector(0, 650);
            await Capture("manage");
            ShowPage("resources");
            await Capture("resources-empty");
            var row = new CatalogProject("fixture", "fixture", "modrinth", "resourcepack", "手机资源列表 · 很长的中文项目名称", "Mobile fixture", "验收夹具，不是上游资源", "检查中文换行、触控布局和文件选择。", "Mobile layout fixture; no unrelated image is used.", "", 0);
            Projects.ItemsSource = new[] { row };
            Projects.SelectedIndex = 0;
            await Capture("resources");
            PresentProject(row);
            Releases.ItemsSource = new[] { new CatalogRelease("fixture-file", "测试版本", "fixture.zip", ["1.21.1"], ["minecraft"], "2026-11-11", 0, new PyDict()) };
            Releases.SelectedIndex = 0;
            if (!DownloadButton.IsEnabled) throw new IOException("Reference version should enable resource downloads.");
            OriginalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ProjectDescription.Text!.Contains("Mobile layout")) throw new IOException("Mobile original-text toggle failed.");
            await Capture("detail");
            ResourcesPage.Offset = new Vector(0, 400);
            await Capture("download");
            ShowPage("about");
            await Capture("about");
            if (MobileRuntime.LiveSmoke) await CheckNativeDownload();
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                width = Bounds.Width, height = Bounds.Height, created = true, renamed = true, grouped = true, pinned = true,
                referenceVersion = reference, resources = true, gameExecutionSupported = false,
                nativeResourceDownload = MobileRuntime.LiveSmoke,
                persistedInstances = launcher.LibraryList().Count
            }));
            Status.Text = "移动页面验收完成。";
        }
        catch (Exception error)
        {
            Status.Text = "验收失败：" + error.Message;
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-smoke-error.txt"), error.ToString());
        }
    }

    private async Task Capture(string page)
    {
        await Task.Delay(350);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(Path.Combine(EvidenceRoot, $"mobile-{page}.png"), new PngBitmapEncoderOptions());
    }

    private async Task CheckNativeDownload()
    {
        ShowPage("resources");
        ClearProjects();
        Query.Text = "忠实";
        SearchResources(this, new RoutedEventArgs());
        await WaitOperation();
        var pack = Projects.Items.OfType<CatalogProject>().FirstOrDefault(row => row.Slug == "faithful-64x")
            ?? throw new IOException("Native Chinese resource search failed: " + Status.Text);
        Projects.SelectedItem = pack;
        OpenDetails(this, new RoutedEventArgs());
        await WaitOperation();
        if (Releases.SelectedItem is not CatalogRelease release) throw new IOException("Native compatible release lookup failed: " + Status.Text);
        DateTime pictureDeadline = DateTime.UtcNow.AddSeconds(20);
        while (!ProjectIcon.ImageLoaded && DateTime.UtcNow < pictureDeadline) await Task.Delay(100);
        if (!ProjectIcon.ImageLoaded) throw new IOException("Native upstream project icon did not load.");
        await Capture("live-detail");
        DownloadResource(this, new RoutedEventArgs());
        await WaitOperation();
        string file = Path.Combine(launcher.InstanceFolder(selected), "resourcepacks", release.Filename);
        if (!File.Exists(file)) throw new IOException("Native resource download failed: " + Status.Text);
        string expected = Py.S((release.File.Get("hashes") as PyDict ?? release.File).Get("sha512"));
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) throw new IOException("Native downloaded resource checksum mismatch.");
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-catalog-live.json"), JsonSerializer.Serialize(new
            { ok = true, query = "忠实", slug = pack.Slug, release = release.Id, iconRendered = true, bytes = new FileInfo(file).Length, sha512 = actual }));
        await Capture("live-installed");

        async Task WaitOperation()
        {
            DateTime deadline = DateTime.UtcNow.AddMinutes(3);
            while (operation != null && DateTime.UtcNow < deadline) await Task.Delay(100);
            if (operation != null) { operation.Cancel(); throw new TimeoutException("Native catalog operation timed out."); }
        }
    }
}
