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
            ShowPage("home");
            await Capture("home");
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-typeface.json"), JsonSerializer.Serialize(new
            {
                heading = BrandTitle.FontFamily.Name,
                resolved = new Avalonia.Media.Typeface(BrandTitle.FontFamily, weight: BrandTitle.FontWeight).GlyphTypeface.FamilyName,
                headingWeight = new Avalonia.Media.Typeface(BrandTitle.FontFamily, weight: BrandTitle.FontWeight).GlyphTypeface.Weight.ToString(),
                bodyWeight = new Avalonia.Media.Typeface(NewName.FontFamily).GlyphTypeface.Weight.ToString(),
                button = CreateButton.FontFamily.Name,
                input = NewName.FontFamily.Name
            }));
            var bodyFont = new Avalonia.Media.Typeface(NewName.FontFamily).GlyphTypeface;
            var headingFont = new Avalonia.Media.Typeface(BrandTitle.FontFamily, weight: BrandTitle.FontWeight).GlyphTypeface;
            if (bodyFont.FamilyName != "Noto Sans SC" || bodyFont.Weight != Avalonia.Media.FontWeight.Normal
                || headingFont.Weight < Avalonia.Media.FontWeight.Medium)
                throw new IOException("The bundled Chinese family or its weights were not resolved.");
            if ((!NavigationRail.IsVisible && !BottomBar.IsVisible) || Shell.RowDefinitions[0].ActualHeight < 40)
                throw new IOException("Navigation or chrome is missing.");
            foreach (Button tab in BottomNavigation.Children.OfType<Button>())
            {
                tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!tab.Classes.Contains("current")) throw new IOException("Bottom navigation selection did not follow its page.");
            }
            ShowPage("home");
            await CheckLocalCover();
            ShowPage("library");
            await Capture("library");
            var addPosition = LibraryAddDock.TranslatePoint(default, Pages)
                ?? throw new IOException("The new-instance action is detached.");
            if (addPosition.Y + LibraryAddDock.Bounds.Height > Pages.Bounds.Height + 1
                || Pages.Bounds.Height - addPosition.Y - LibraryAddDock.Bounds.Height > 30)
                throw new IOException("The new-instance action must remain above the navigation.");
            LibraryAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!CreatePage.IsVisible || LibraryWorkspace.IsVisible) throw new IOException("The floating create action did not navigate.");
            Status.Text = "可收起的提示";
            DismissStatusButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (StatusBar.IsVisible) throw new IOException("The message dismissal left an empty status panel.");
            ShowPage("manage");
            await Capture("manage");
            ShowPage("create");
            NewName.Focus();
            await Capture("create");
            ShowPage("resources");
            OpenPackImport(this, new RoutedEventArgs());
            if (SelectedKind != "modpack" || !SearchFilters.IsVisible)
                throw new IOException("The modpack entry did not set its target before opening resources.");
            Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "mod");
            SearchFilters.IsVisible = false;
            Status.Text = "";
            await Capture("resources-empty");
            ToggleSearch(this, new RoutedEventArgs());
            await Capture("resources-filters");
            if (!SearchFilters.IsVisible || !Query.IsFocused)
                throw new IOException("Opening resource filters did not focus the search field.");
            ToggleSearch(this, new RoutedEventArgs());
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
            PresentProject(row with { Slug = "second-fixture", Kind = "mod" });
            if (!string.IsNullOrEmpty(DownloadNotice.Text) || DownloadButton.IsEnabled || Releases.ItemCount != 0)
                throw new IOException("Switching resources retained the previous file or download destination.");
            PresentProject(row);
            ResourcesPage.Offset = new Vector(0, 400);
            await Capture("download");
            ShowPage("about");
            await Capture("about");
            ShowPage("account");
            await Capture("account");
            using (var authorization = new CancellationTokenSource())
            {
                operation = authorization;
                ShowAuthorization(new DeviceAuthorization("TEST-CODE", new Uri("https://www.microsoft.com/link"), 900), authorization.Token);
                if (!AuthorizationPanel.IsVisible || DeviceCode.Text != "TEST-CODE")
                    throw new IOException("The device authorization code did not appear.");
                Status.Text = "等待微软授权";
                ShowPage("home");
                if (!StatusBar.IsVisible || !Status.Text.Contains("等待"))
                    throw new IOException("Navigation hid the running authorization status.");
                ShowPage("account");
                await Capture("account-code");
                authorization.Cancel();
                ClearAuthorization();
                ShowAuthorization(new DeviceAuthorization("STALE-CODE", new Uri("https://www.microsoft.com/link"), 900), authorization.Token);
                if (AuthorizationPanel.IsVisible || !string.IsNullOrEmpty(DeviceCode.Text))
                    throw new IOException("A cancelled authorization callback revived its code.");
                operation = null;
                Status.Text = "";
                UpdateAccount();
            }
            ShowPage("more");
            await Capture("more");
            ThemePicker.SelectedIndex = 1;
            if (Application.Current?.RequestedThemeVariant != Avalonia.Styling.ThemeVariant.Light
                || Py.S(HelL.Core.Launcher.Config.CONFIG.Get("mobile_theme", "")) != "light")
                throw new IOException("The mobile light theme was not applied and saved.");
            ShowPage("home");
            await Capture("home-light");
            ThemePicker.SelectedIndex = 0;
            if (Application.Current?.RequestedThemeVariant != Avalonia.Styling.ThemeVariant.Dark)
                throw new IOException("The mobile dark theme did not restore.");
            ShowPage("ai");
            await Capture("ai-empty");
            if (!AiPage.IsVisible || !AiSettingsPanel.IsVisible || BottomNavigation.Children.OfType<Button>().Count() != 5)
                throw new IOException("The AI page did not expose configuration and its real navigation entry.");
            AiSettingsPanel.IsVisible = false;
            AiMessages.ItemsSource = new[]
            {
                new Ai.ChatEntry("user", "Fabric 和 Forge 的模组可以混用吗？", "complete"),
                new Ai.ChatEntry("assistant", "通常不能直接混用。先确认实例的 Minecraft 版本和加载器，再选择与两者兼容的模组版本。这是隔离界面验收文本，没有调用模型服务。", "complete")
            };
            AiEmpty.IsVisible = false;
            await Capture("ai-chat-layout");
            UpdateAi();
            await CheckAiChat();
            if (MobileRuntime.LiveCatalogPreview) await CaptureLiveCatalog();
            if (MobileRuntime.LiveSmoke) await CheckNativeDownload();
            await CheckResponsiveNavigation();
            await CheckActivitySession();
            await CheckFrontendRemediationAsync();
            await CheckSystemTextLayoutAsync();
            File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                width = Bounds.Width, height = Bounds.Height, created = true, renamed = true, grouped = true, pinned = true,
                referenceVersion = reference, resources = true, gameExecutionSupported = false, rotation = true,
                chineseFont = true, floatingCreate = true, dismissibleFeedback = true, searchFocus = true,
                adaptiveShell = true, bottomNavigation = true, localCover = true, deviceAuthorizationUi = true, appearance = true, activitySession = true, aiCloudChatUi = true, aiTransportReplay = MobileRuntime.BeginAiSmoke != null,
                nativeResourceDownload = MobileRuntime.LiveSmoke,
                persistedInstances = launcher.LibraryList().Count
            }));
            ShowPage("home");
            if (MobileRuntime.KeepPreviewOpen) MobileRuntime.Smoke = false;
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

    private async Task CheckLocalCover()
    {
        string imagePath = Path.Combine(EvidenceRoot, "isolated-cover-fixture.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(64, 48), new Vector(96, 96)))
        {
            using (var drawing = bitmap.CreateDrawingContext())
                drawing.FillRectangle(Avalonia.Media.Brushes.SlateGray, new Rect(0, 0, 64, 48));
            bitmap.Save(imagePath, new PngBitmapEncoderOptions());
        }
        launcher.SetInstanceCover(selected, imagePath);
        Reload(selected);
        DateTime timeout = DateTime.UtcNow.AddSeconds(8);
        while (!HomeCover.IsVisible && DateTime.UtcNow < timeout) await Task.Delay(50);
        if (!HomeCover.ImageLoaded || !HomeCover.IsVisible)
            throw new IOException("The current instance cover did not appear in the desktop summary.");
        string storedCover = launcher.LibraryList().Single().Cover;
        File.WriteAllText(storedCover, "invalid PNG");
        Reload(selected);
        await Task.Delay(200);
        if (HomeCover.IsVisible) throw new IOException("A corrupt cover displaced the launch summary.");
        launcher.ResetInstanceCover(selected);
        Reload(selected);
    }

    private async Task CaptureLiveCatalog()
    {
        ShowPage("resources");
        Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "mod");
        Query.Text = "";
        SearchResources(this, new RoutedEventArgs());
        DateTime deadline = DateTime.UtcNow.AddSeconds(90);
        while (operation != null && DateTime.UtcNow < deadline) await Task.Delay(100);
        if (operation != null) { operation.Cancel(); throw new TimeoutException("Live catalog search timed out."); }
        if (searchResults.Count == 0) throw new IOException("Live catalog did not return results: " + Status.Text);
        Status.Text = "";
        var pictures = Projects.GetVisualDescendants().OfType<HelL.Desktop.Controls.ProjectPicture>().ToArray();
        deadline = DateTime.UtcNow.AddSeconds(20);
        while (pictures.Length > 0 && pictures.Take(6).Any(picture => !picture.ImageLoaded) && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        ResourcesPage.Offset = Vector.Zero;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        pictures = Projects.GetVisualDescendants().OfType<HelL.Desktop.Controls.ProjectPicture>().ToArray();
        if (!pictures.Any(picture => picture.ImageLoaded)) throw new IOException("No upstream icon was rendered.");
        await Capture("resources-live");
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-live-catalog.json"), JsonSerializer.Serialize(new
        {
            ok = true, count = searchResults.Count, source = SelectedProvider,
            imagesLoaded = pictures.Count(picture => picture.ImageLoaded),
            titles = searchResults.Take(5).Select(row => row.Title)
        }));
    }

    private async Task CheckResponsiveNavigation()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        double originalWidth = window.Width, originalHeight = window.Height;
        ShowPage("home");
        window.Width = 844;
        window.Height = 390;
        await Task.Delay(250);
        if (!NavigationRail.IsVisible || BottomBar.IsVisible || !HomeHeader.IsVisible)
            throw new IOException("Landscape navigation is missing.");
        window.Width = 1180;
        window.Height = 740;
        await Task.Delay(250);
        if (Shell.ColumnDefinitions[0].ActualWidth < 180 || !RailCaption.IsVisible
            || Grid.GetColumn(InstanceContent) != 1 || Grid.GetRow(InstanceContent) != 0)
            throw new IOException("Wide mode did not restore the labelled desktop sidebar and launch options.");
        await Capture("tablet-1180");
        window.Width = 390;
        window.Height = 844;
        await Task.Delay(250);
        if (NavigationRail.IsVisible || !BottomBar.IsVisible || Shell.ColumnDefinitions[0].ActualWidth > 1
            || Grid.GetRow(InstanceContent) != 1 || Grid.GetColumn(InstanceContent) != 0)
            throw new IOException("Portrait did not restore bottom navigation and stacked launch options.");
        window.Width = originalWidth;
        window.Height = originalHeight;
        await Task.Delay(250);
    }

    private async Task CheckActivitySession()
    {
        var replacement = new MobileHome(root);
        if (ReferenceEquals(this, replacement) || !ReferenceEquals(session, replacement.session)
            || replacement.instances.Single().Name != selected)
            throw new IOException("Activity recreation did not retain one data owner and instance state.");
        using var cancellation = new CancellationTokenSource();
        replacement.operation = cancellation;
        replacement.ShowAuthorization(new DeviceAuthorization("RECREATE", new Uri("https://www.microsoft.com/link"), 900), cancellation.Token);
        replacement.Dispose();
        if (!cancellation.IsCancellationRequested || replacement.AuthorizationPanel.IsVisible)
            throw new IOException("Discarding an Activity did not cancel its operation and clear the authorization code.");
        replacement.ShowAuthorization(new DeviceAuthorization("STALE", new Uri("https://www.microsoft.com/link"), 900), cancellation.Token);
        if (replacement.AuthorizationPanel.IsVisible) throw new IOException("A disposed Activity accepted an authorization callback.");
        var thirdView = new MobileHome(root);
        bool entered = false;
        await session.Operations.WaitAsync();
        Task pending = thirdView.Run(_ => { entered = true; return Task.CompletedTask; }, allowNavigation: true);
        await Task.Delay(50);
        if (entered) throw new IOException("A recreated Activity bypassed the previous operation gate.");
        thirdView.Dispose();
        session.Operations.Release();
        await pending;
        if (entered) throw new IOException("A disposed Activity ran a queued command.");
    }

    private async Task CheckAiChat()
    {
        if (MobileRuntime.BeginAiSmoke == null) return;
        using var fixture = MobileRuntime.BeginAiSmoke();
        ShowPage("ai");
        AiMode.SelectedIndex = 0;
        AiAddress.Text = "https://fixture.invalid/v1";
        AiModel.Text = "fixture-model";
        AiKey.Text = "fictional-preview-key";
        AiSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAiOperation();
        if (AiKey.Text?.Length > 0 || File.ReadAllText(HelL.Core.Launcher.Config.CONFIG_FILE).Contains("fictional-preview-key"))
            throw new IOException("The AI connection exposed its API key in the form or configuration file.");
        AiPrompt.Text = "这组模组应该如何排查兼容性？";
        AiSendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAiOperation();
        var reply = session.Chat.Entries.LastOrDefault();
        if (reply?.State != "complete" || !reply.Text.Contains("本机隔离 HTTP 回放") || !string.IsNullOrEmpty(AiPrompt.Text))
            throw new IOException("The AI send button did not complete its isolated streamed response: " + AiChatNotice.Text);
        await Capture("ai-replay");
        ClearAiChat(this, new RoutedEventArgs());
        CompleteConfirmation(true);
        await Task.Delay(50);
        if (!AiEmpty.IsVisible || session.Chat.Entries.Count != 0) throw new IOException("The AI clear action did not reset its session.");

        async Task WaitAiOperation()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (operation != null && DateTime.UtcNow < deadline) await Task.Delay(40);
            if (operation != null) { operation.Cancel(); throw new TimeoutException("The AI UI replay timed out."); }
        }
    }

    private async Task CheckNativeDownload()
    {
        ShowPage("resources");
        Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "resourcepack");
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
