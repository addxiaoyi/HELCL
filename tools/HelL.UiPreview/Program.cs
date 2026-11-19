using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine("[PREVIEW] Starting isolated UI preview");
        try { return Run(args); }
        catch (Exception ex)
        {
            WriteError(ex);
            return 1;
        }
    }

    internal static void WriteError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
            Console.Error.WriteLine(current.StackTrace);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        string? Option(string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0) return null;
            if (index + 1 == args.Length) throw new ArgumentException("Missing value for " + key);
            return args[index + 1];
        }

        bool dark = args.Contains("--dark");
        bool verify = args.Contains("--verify");
        bool nativeChecks = args.Contains("--native-checks");
        string? snapshot = Option("--snapshot");
        string? gallery = Option("--gallery");
        string? glassGallery = Option("--glass-gallery");
        string? dpiGallery = Option("--dpi-gallery");
        string? desktopGallery = Option("--desktop-gallery");
        string? aiGallery = Option("--ai-gallery");
        string? aiLiveCheck = Option("--ai-live-check");
        string? aiSelfTest = Option("--ai-ui-selftest");
        string? queueCheck = Option("--ai-queue-check");
        string? livePicker = Option("--ai-picker-live");
        string? newsGallery = Option("--news-gallery");
        string? libraryGallery = Option("--library-gallery");
        string? continueGame = Option("--continue-game");
        string? microsoftLive = Option("--microsoft-live");
        string? catalogLive = Option("--catalog-live");
        string? minecraftLive = Option("--minecraft-live");
        string? gameModel = Option("--prepare-game-model");
        string? feiyuLive = Option("--feiyu-live");
        string? extensionsUi = Option("--extensions-ui");
        string? minecraftRecovery = Option("--minecraft-recovery");
        string? minecraftConnection = Option("--minecraft-connection");
        bool launchRecovery = args.Contains("--launch-recovery");
        string? networkCheck = Option("--minecraft-network");
        string? retryPreview = Option("--download-retry-ui");
        string? mirrorCheck = Option("--mirror-live");
        string? customize = Option("--customize-ui");
        string? engineFrames = Option("--engine-frames");
        string? axolotl = Option("--axolotl-ui");
        string? pluginMarket = Option("--plugin-market-live");
        string? captureCheck = Option("--capture-permissions");
        string? libraryWorkspace = Option("--library-workspace");
        string? pluginScreens = Option("--plugin-screenshots");
        string? pluginDetails = Option("--plugin-details");
        string? homeRedesign = Option("--home-redesign");
        string? desktopWorkflow = Option("--desktop-workflow");
        string? materialCheck = Option("--material-check");
        string? materialLive = Option("--material-live");
        string? materialControls = Option("--material-controls");
        string? wallpaperPower = Option("--wallpaper-power");
        string? videoCacheProbe = Option("--video-cache-probe");
        string? displays = Option("--multi-display");
        string? workspaceMaterial = Option("--workspace-material");
        string? videoLifecycle = Option("--video-lifecycle");
        string? cacheSettings = Option("--cache-settings");
        string? cachePolicy = Option("--cache-policy");
        string? catalogNarrow = Option("--catalog-narrow");
        string? typeAudit = Option("--type-audit");
        string? richAudit = Option("--rich-ui");
        string? interactionAudit = Option("--interaction-audit");
        string? appearanceAudit = Option("--appearance-audit");
        string? nativeScale = Option("--native-scale");
        string? chineseCatalog = Option("--chinese-catalog");
        string? catalogTranslation = Option("--catalog-translation-live");
        string? resourceWorkflow = Option("--resource-workflow");
        string? packGame = Option("--resource-pack-game");
        string? resourcePolish = Option("--resource-polish");
        string? workspaceViews = Option("--workspace-views");
        string? workspaceRefinement = Option("--workspace-refinement");
        string? taskWorkspace = Option("--task-workspace");
        string? materialPerformance = Option("--material-performance");
        string performanceMode = Option("--performance-mode") ?? "image";
        string? transparentDesktop = Option("--transparent-desktop");
        string? transparencyCheck = Option("--transparency-check");
        string? appearanceRestart = Option("--appearance-restart") ?? Environment.GetEnvironmentVariable("HELL_APPEARANCE_RESTART");
        string page = Option("--page") ?? "launch";
        double width = double.Parse(Option("--width") ?? "1180", CultureInfo.InvariantCulture);
        double height = double.Parse(Option("--height") ?? "740", CultureInfo.InvariantCulture);

        // Every run gets an empty profile, so previews cannot overwrite accounts or layouts.
        string previewRoot = Environment.GetEnvironmentVariable("HELL_PREVIEW_ROOT") ?? Path.Combine(Environment.CurrentDirectory, "TestResults", "ui-preview-home");
        string home = appearanceRestart == null ? Path.Combine(Path.GetFullPath(previewRoot), Guid.NewGuid().ToString("N"))
            : AppearanceRestartCheck.Prepare(appearanceRestart, args.Contains("--appearance-restart"));
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("TIPCL_HOME", home);
        if (appearanceRestart == null) File.WriteAllText(Path.Combine(home, "config.json"),
            "{\"first_run\":false,\"feedback_consent\":false,\"feedback_heartbeat\":false,\"auto_check_update\":false,\"ui_dark\":" + (dark ? "true" : "false") + "}");
        if (continueGame != null) ContinueGameCheck.Configure(home, continueGame);
        if (microsoftLive != null) ContinueGameCheck.Configure(home, microsoftLive);
        string? appearanceSource = Option("--appearance-source");
        if (appearanceSource != null) DesktopWorkflowPreview.CopyAppearance(appearanceSource, home);
        if (transparentDesktop != null || materialPerformance != null && performanceMode == "desktop")
            File.WriteAllText(Path.Combine(home, "config.json"), "{\"first_run\":false,\"feedback_consent\":false,\"auto_check_update\":false,\"ui_wallpaper_mode\":\"transparent\",\"ui_glass_transparency\":100,\"ui_glass_frost\":0}");

        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/HelL;component/Themes/Palette.Light.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/HelL;component/Themes/Controls.xaml", UriKind.Relative)
        });
        typeof(App).GetMethod("InitPlatform", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        I18n.Init(Array.Empty<string>());
        app.DispatcherUnhandledException += (_, e) =>
        {
            WriteError(e.Exception);
            e.Handled = true;
            app.Shutdown(1);
        };
        if (verify) return WpfChecks.Run(app);
        if (packGame != null) return ResourcePackGameCheck.Run(packGame, Option("--pack-file")!, Option("--pack-output")!, args.Contains("--keep-game")).GetAwaiter().GetResult();
        if (videoCacheProbe != null) { VideoCacheProbe.Run(videoCacheProbe).GetAwaiter().GetResult(); return 0; }
        if (engineFrames != null) { EngineFrameCheck.Run(engineFrames).GetAwaiter().GetResult(); return 0; }
        if (pluginMarket != null) { PluginMarketCheck.Run(pluginMarket, Option("--plugin-baseline")).GetAwaiter().GetResult(); return 0; }
        if (queueCheck != null) return ModelQueueProcessCheck.Run(queueCheck, args.Contains("--recover"));
        if (aiSelfTest != null)
        {
            var selfTest = typeof(AiPage).Assembly.GetType("HelL.Pages.Ai.AiSelfTest", true)!;
            return (int)selfTest.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { args })!;
        }
        if (aiLiveCheck != null) return LocalAiLiveCheck.Run(aiLiveCheck).GetAwaiter().GetResult();
        if (networkCheck != null) { MinecraftNetworkCheck.Run(networkCheck).GetAwaiter().GetResult(); return 0; }

        var window = new MainWindow { Title = "HelL · UI Preview", Width = width, Height = height };
        if (args.Contains("--primary-display")) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = 32; window.Top = 32; }
        app.MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            try
            {
                if (await Task.WhenAny(window.BridgeSettled, Task.Delay(20000)) != window.BridgeSettled)
                    throw new TimeoutException("The isolated launcher did not connect within 20 seconds.");
                if (nativeChecks)
                {
                    await NativeShellChecks.Run(window);
                    app.Shutdown(0);
                    return;
                }
                if (displays != null)
                {
                    await MultiDisplayChecks.Run(window, displays, args.Contains("--display-live"), args.Contains("--type-displays")); if (!args.Contains("--display-live")) app.Shutdown(0); return;
                }
                if (transparentDesktop != null) { await TransparentDesktopCheck.Run(window, transparentDesktop); return; }
                if (appearanceRestart != null) { await AppearanceRestartCheck.Run(window, appearanceRestart); return; }
                if (transparencyCheck != null) { await TransparencyCheck.Run(window, transparencyCheck); app.Shutdown(0); return; }
                if (workspaceViews != null) { await WorkspaceViewsCheck.Run(window, workspaceViews); app.Shutdown(0); return; }
                if (workspaceRefinement != null) { await WorkspaceRefinementCheck.Run(window, workspaceRefinement, args.Contains("--audit-live")); if (!args.Contains("--audit-live")) app.Shutdown(0); return; }
                if (taskWorkspace != null) { await TaskWorkspaceCheck.Run(window, taskWorkspace); app.Shutdown(0); return; }
                if (materialPerformance != null) { await MaterialPerformanceCheck.Run(window, materialPerformance, performanceMode); app.Shutdown(0); return; }
                if (workspaceMaterial != null) { await WorkspaceSurfacePreview.Run(window, workspaceMaterial); app.Shutdown(0); return; }
                if (videoLifecycle != null) { await VideoLifecycleCheck.Run(window, videoLifecycle, Option("--video-cache-seed")!); app.Shutdown(0); return; }
                if (cacheSettings != null) { await CacheSettingsPreview.Run(window, cacheSettings, Option("--video-cache-seed")!); app.Shutdown(0); return; }
                if (cachePolicy != null) { await CachePolicyPreview.Run(window, cachePolicy); app.Shutdown(0); return; }
                if (catalogNarrow != null) { await CatalogNarrowPreview.Run(window, catalogNarrow); app.Shutdown(0); return; }
                if (args.Contains("--type-check")) { await TypographyChecks.Run(window); app.Shutdown(0); return; }
                if (chineseCatalog != null) { await CatalogChinesePreview.Run(window, chineseCatalog); app.Shutdown(0); return; }
                if (catalogTranslation != null) { await CatalogTranslationAudit.Live(window, catalogTranslation); app.Shutdown(0); return; }
                if (resourceWorkflow != null) { await ResourceWorkflowCheck.Run(window, resourceWorkflow, args.Contains("--documents-only")); app.Shutdown(0); return; }
                if (resourcePolish != null) { await ResourcePolishCheck.Run(window, resourcePolish); app.Shutdown(0); return; }
                if (nativeScale != null) { await NativeScaleAudit.Run(window, int.Parse(nativeScale), Option("--scale-output")!); if (!args.Contains("--audit-live")) app.Shutdown(0); return; }
                if (interactionAudit != null) { await InteractionAudit.Run(window, interactionAudit); app.Shutdown(0); return; }
                if (appearanceAudit != null) { await AppearanceAudit.Run(window, appearanceAudit, Option("--appearance-pages")?.Split(',')); if (!args.Contains("--audit-live")) app.Shutdown(0); return; }
                if (richAudit != null) { await RichUiAudit.Run(window, richAudit, args.Contains("--audit-live")); if (!args.Contains("--audit-live")) app.Shutdown(0); return; }
                if (typeAudit != null) { await TypographyAudit.Run(window,typeAudit,args.Contains("--audit-live"));if(!args.Contains("--audit-live"))app.Shutdown(0);return; }
                if (captureCheck != null)
                {
                    await CapturePermissionCheck.Run(window, captureCheck, args.Contains("--picker")); app.Shutdown(0); return;
                }
                if (homeRedesign != null)
                {
                    await HomeRedesignPreview.Run(window, homeRedesign); app.Shutdown(0); return;
                }
                if (desktopWorkflow != null)
                {
                    await DesktopWorkflowPreview.Run(window, desktopWorkflow); app.Shutdown(0); return;
                }
                if (materialCheck != null)
                {
                    await MaterialPreview.Run(window, materialCheck); app.Shutdown(0); return;
                }
                if (materialControls != null)
                {
                    await MaterialControlsPreview.Run(window, materialControls); app.Shutdown(0); return;
                }
                if (wallpaperPower != null)
                {
                    await WallpaperPowerCheck.Run(window, wallpaperPower, args.Contains("--optimized-video"), Option("--video-cache-seed")); app.Shutdown(0); return;
                }
                if (materialLive != null)
                {
                    await MaterialPreview.Live(window, materialLive, args.Contains("--native-material")); return;
                }
                if (libraryWorkspace != null)
                {
                    await LibraryWorkspacePreview.Run(window, libraryWorkspace); app.Shutdown(0); return;
                }
                if (pluginScreens != null)
                {
                    await LibraryWorkspacePreview.Plugins(window, pluginScreens, Option("--plugin-source")!); app.Shutdown(0); return;
                }
                if (pluginDetails != null)
                {
                    await PluginDetailsPreview.Run(window, pluginDetails, Option("--plugin-market")!); app.Shutdown(0); return;
                }
                if (extensionsUi != null)
                {
                    await ExtensionsPreview.Capture(window, extensionsUi);
                    app.Shutdown(0); return;
                }
                if (minecraftRecovery != null)
                {
                    await MinecraftRecoveryCheck.Run(window, minecraftRecovery);
                    app.Shutdown(0); return;
                }
                if (minecraftConnection != null)
                {
                    await MinecraftLiveCheck.Run(window, minecraftConnection, connectionOnly: true);
                    app.Shutdown(0); return;
                }
                if (retryPreview != null)
                {
                    await DownloadRetryPreview.Run(window, retryPreview);
                    app.Shutdown(0); return;
                }
                if (mirrorCheck != null)
                {
                    await MirrorLiveCheck.Run(window, mirrorCheck);
                    app.Shutdown(0); return;
                }
                if (customize != null)
                {
                    await CustomizationPreview.Capture(window, customize);
                    app.Shutdown(0); return;
                }
                if (axolotl != null)
                {
                    await AxolotlPreview.Run(window, axolotl);
                    app.Shutdown(0); return;
                }
                if (newsGallery != null)
                {
                    await NewsPreview.Capture(window, newsGallery);
                    app.Shutdown(0);
                    return;
                }
                if (libraryGallery != null)
                {
                    await LibraryPreview.Capture(window, libraryGallery);
                    app.Shutdown(0);
                    return;
                }
                if (catalogLive != null)
                {
                    await CatalogLiveCheck.Run(window, catalogLive);
                    app.Shutdown(0);
                    return;
                }
                if (minecraftLive != null)
                {
                    await MinecraftLiveCheck.Run(window, minecraftLive);
                    app.Shutdown(0);
                    return;
                }
                if (feiyuLive != null)
                {
                    await MinecraftLiveCheck.Run(window, feiyuLive, true);
                    app.Shutdown(0); return;
                }
                if (gameModel != null)
                {
                    Directory.CreateDirectory(gameModel);
                    string task = await AppServices.Client.StartTaskAsync("minecraft_prepare_model");
                    var outcome = await AppServices.Client.CallAsync<HelL.Models.OpResult>("wait_task", new { task_id = task, timeout = 1200 });
                    if (outcome?.Ok != true) throw new InvalidOperationException(outcome?.Message ?? "Model setup failed");
                    File.WriteAllText(Path.Combine(gameModel, "model-setup.json"), HelL.Core.PyJson.Dumps(HelL.Core.Launcher.Ai.Minecraft.MinecraftModels.Status()));
                    Console.WriteLine("[GAME MODEL] Independent runtime and Qwen3 1.7B are ready.");
                    HelL.Core.Launcher.Ai.Minecraft.MinecraftModels.Stop();
                    app.Shutdown(0); return;
                }
                if (continueGame != null)
                {
                    await ContinueGameCheck.Run(window, continueGame, launchRecovery);
                    app.Shutdown(0);
                    return;
                }
                if (microsoftLive != null)
                {
                    await MicrosoftLiveCheck.Run(window, microsoftLive);
                    app.Shutdown(0);
                    return;
                }
                if (dpiGallery != null)
                {
                    window.Navigate("launch", instant: true);
                    await WaitForPage(window, "launch");
                    DpiPreview.Capture(window, dpiGallery);
                    app.Shutdown(0);
                    return;
                }
                if (desktopGallery != null)
                {
                    await DesktopPreview.Capture(window, desktopGallery);
                    app.Shutdown(0);
                    return;
                }
                if (aiGallery != null)
                {
                    await LocalAiPreview.Capture(window, aiGallery);
                    app.Shutdown(0);
                    return;
                }
                if (livePicker != null)
                {
                    await ModelSelectionLiveCheck.Run(window, livePicker);
                    app.Shutdown(0);
                    return;
                }
                if (gallery != null)
                {
                    await CaptureGallery(window, gallery);
                    app.Shutdown(0);
                    return;
                }
                if (glassGallery != null)
                {
                    await GlassPreview.Capture(window, glassGallery);
                    app.Shutdown(0);
                    return;
                }
                window.Navigate(page, instant: true);
                await WaitForPage(window, page);
                if (snapshot == null) return;
                SaveSnapshot(window, snapshot);
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                WriteError(ex);
                app.Shutdown(1);
            }
        };
        return app.Run(window);
    }

    private static async Task CaptureGallery(MainWindow window, string folder)
    {
        string[] pages = { "launch", "instance", "version", "mod", "mods", "modpack", "datapack", "resourcepack", "shader", "world", "account", "java", "ai", "multiplayer", "servers", "playtime", "feedback", "settings", "tasks" };
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (var size in new[] { (Width: 1180, Height: 740), (Width: 900, Height: 580) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                foreach (string page in pages)
                {
                    window.Navigate(page, instant: true);
                    await WaitForPage(window, page);
                    SaveSnapshot(window, Path.Combine(folder, $"{page}-{(dark ? "dark" : "light")}-{size.Width}.png"));
                }
            }
        }
    }

    internal static async Task WaitForPage(MainWindow window, string page)
    {
        var host = (System.Windows.Controls.ContentControl)window.FindName("PageHost");
        if (host.Content is PageBase content)
        {
            var timer = Stopwatch.StartNew();
            Task loading = content.EnsureLoadedAsync();
            bool settled = await Task.WhenAny(loading, Task.Delay(15000)) == loading;
            if (settled)
            {
                await loading;
                while (HasPendingRequest(content) && timer.ElapsedMilliseconds < 15000) await Task.Delay(100);
            }
            bool pending = !settled || HasPendingRequest(content);
            Console.WriteLine($"[PAGE_STATE] {page}: {(pending ? "still loading after 15 seconds" : "settled")}");
        }
        await Task.Delay(200);
    }

    private static bool HasPendingRequest(PageBase page)
    {
        string? flag = page is CatalogPage ? "_searchInFlight" : page is VersionPage ? "_fetchInFlight" : null;
        if (flag == null) return false;
        var field = page.GetType().GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(page.GetType().Name, flag);
        return (bool)field.GetValue(page)!;
    }

    internal static void SaveSnapshot(FrameworkElement surface, string path, double scale = 1)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 || scale > 4)
            throw new ArgumentOutOfRangeException(nameof(scale));
        surface.UpdateLayout();
        int width = (int)Math.Ceiling(surface.ActualWidth * scale);
        int height = (int)Math.Ceiling(surface.ActualHeight * scale);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("The preview has no rendered size.");
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        // RenderTargetBitmap cannot capture DWM. Use the app's fallback color for layout-only snapshots.
        if (surface is Window)
        {
            var backdrop = new DrawingVisual();
            using (DrawingContext drawing = backdrop.RenderOpen())
                drawing.DrawRectangle(new SolidColorBrush(Glass.Base(App.IsDark)), null, new Rect(0, 0, width / scale, height / scale));
            bitmap.Render(backdrop);
        }
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using (var stream = File.Create(full)) encoder.Save(stream);
        Console.WriteLine("[SNAPSHOT_SAVED] " + full);
    }
}
