using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using HelL.Desktop.Controls;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async void RunCatalogSmoke(object? sender, EventArgs e)
    {
        int exit = 1;
        try
        {
            DateTime readyBy = DateTime.UtcNow.AddSeconds(20);
            while (task != null && DateTime.UtcNow < readyBy) await Task.Delay(50);
            if (task != null) throw new TimeoutException("Startup account restore did not finish.");
            if (launcher.InstancesList().Count != 0) throw new IOException("Catalog UI probe requires empty isolated state.");
            Title = "HelL · 资源界面实测（隔离配置）";
            launcher.CreateInstance("资源界面验收");
            string version = Path.Combine(launcher.InstanceFolder("资源界面验收"), "versions", "1.21.1");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\"}");
            File.WriteAllText(Path.Combine(version, "1.21.1.jar"), "metadata fixture; no game is launched");
            ReloadInstances("资源界面验收");
            ShowPage("resources");
            ResourcesView.FindControl<TextBox>("QueryBox")!.Text = "忠实";
            await ResourcesView.Search();
            var results = ResourcesView.FindControl<ListBox>("Results")!;
            var project = results.Items.OfType<CatalogProject>().FirstOrDefault(row => row.Slug == "faithful-64x")
                ?? throw new InvalidOperationException("Live Chinese search did not return the expected project. " + Status.Text);
            await CaptureCatalog("search", 1060);
            await CaptureCatalog("search", 800);
            ResourcesView.PresentProject(project);
            await ResourcesView.LoadReleases();
            if (ResourcesView.FindControl<ComboBox>("ReleasePicker")!.SelectedItem is not CatalogRelease release)
                throw new IOException("No compatible resource release in the actual UI. " + Status.Text);
            var icon = ResourcesView.FindControl<ProjectPicture>("DetailPicture")!;
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!icon.ImageLoaded && DateTime.UtcNow < deadline) await Task.Delay(100);
            if (!icon.ImageLoaded) throw new IOException("Actual upstream project icon did not render.");
            await CaptureCatalog("detail", 1060);
            await CaptureCatalog("detail", 800);
            await ResourcesView.DownloadSelected();
            string installed = Path.Combine(launcher.InstanceFolder("资源界面验收"), "resourcepacks", release.Filename);
            if (!File.Exists(installed)) throw new IOException("UI download did not install the resource file. " + Status.Text);
            await CaptureCatalog("installed", 800);
            File.WriteAllText(Path.Combine(Program.DataRoot, "catalog-ui.json"), JsonSerializer.Serialize(new
                { ok = true, query = "忠实", slug = project.Slug, icon = project.Icon, actualIconRendered = true,
                    release = release.Id, installedBytes = new FileInfo(installed).Length, gameLaunched = false }));
            exit = 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Program.DataRoot, "catalog-ui-error.txt"), error.ToString()); }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exit); }
    }

    private async Task CaptureCatalog(string page, int width)
    {
        Width = width;
        Height = width == 800 ? 600 : 720;
        await Task.Delay(500);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(Path.Combine(Program.DataRoot, $"catalog-{page}-{width}.png"), new PngBitmapEncoderOptions());
    }
}
