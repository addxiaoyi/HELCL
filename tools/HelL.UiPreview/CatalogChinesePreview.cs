using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class CatalogChinesePreview
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 中文资源搜索验收";
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:1" } });
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        Console.WriteLine("[DISPLAY] Current window DPI=" + dpi.PixelsPerInchX + "; system scale unchanged");
        var timer = Stopwatch.StartNew();
        var status = await AppServices.Client.CallAsync<System.Text.Json.JsonElement>("catalog_language_status", new { refresh = true });
        Console.WriteLine("[INDEX] " + status + " elapsed=" + timer.ElapsedMilliseconds);
        await VerifyCategories();
        foreach (var sample in new[] {
            (Kind:CatalogKind.Mod, Query:"机械动力", Original:"Create", Source:"Modrinth"),
            (Kind:CatalogKind.Modpack, Query:"精致优化", Original:"Fabulously Optimized", Source:"Modrinth"),
            (Kind:CatalogKind.ResourcePack, Query:"生动动画", Original:"Fresh Animations", Source:"Modrinth"),
            (Kind:CatalogKind.Shader, Query:"互补光影", Original:"Complementary", Source:"Modrinth"),
            (Kind:CatalogKind.Datapack, Query:"连锁采集", Original:"VeinMiner", Source:"Modrinth"),
            (Kind:CatalogKind.World, Query:"空岛", Original:"SkyBlock", Source:"CurseForge") })
        {
            timer.Restart();
            var rows = await AppServices.Client.CallAsync<List<CatalogItem>>(sample.Kind.SearchMethod,
                new { query = sample.Query, source = sample.Source, extra = new { game_version = "", category = "" } }) ?? new();
            var hit = rows.FirstOrDefault(row => row.Name.IndexOf(sample.Original, StringComparison.OrdinalIgnoreCase) >= 0);
            if (hit == null) throw new InvalidOperationException("Chinese query did not find upstream project: " + sample.Kind.Key + " " + sample.Query);
            if (!rows.All(row => row.Source.Equals(sample.Source, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Search ignored selected provider");
            Console.WriteLine($"[SEARCH PASS] {sample.Kind.Key} {sample.Query} => {hit.Name} | {hit.ChineseName} | {hit.Source}:{hit.IdValue ?? hit.Slug}; rows={rows.Count}; ms={timer.ElapsedMilliseconds}");
            if (string.IsNullOrWhiteSpace(hit.ChineseName)) throw new InvalidOperationException("Localized title is missing: " + hit.Name);
            window.Navigate(sample.Kind.Key, instant:true); await Program.WaitForPage(window, sample.Kind.Key);
            var page = RichUiAudit.Find<CatalogPage>(window).Single();
            RichUiAudit.Field<ComboBox>(page, "_source").SelectedItem = sample.Source;
            RichUiAudit.Field<TextBox>(page, "_search").Text = sample.Query;
            RichUiAudit.Field<System.Windows.Threading.DispatcherTimer>(page, "_debounce").Stop();
            await (Task)RichUiAudit.Invoke(page, "SearchAsync")!;
            var displayed = RichUiAudit.Field<ItemsControl>(page, "_results").Items.OfType<CatalogItem>().ToList();
            if (!displayed.Any(row => row.Name == hit.Name) || displayed.Any(row => !row.Source.Equals(sample.Source, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("UI search ignored the Chinese query or source");
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark); window.Width = 800; window.Height = 740;
                await MaterialControlsPreview.Layout(window);
                await WaitForArtwork(page, sample.Kind.Key);
                RichUiAudit.Shot(window, folder, sample.Kind.Key + "-" + dark);
                if (!RichUiAudit.Find<TextBlock>(page).Any(text => text.Text == hit.ChineseName)) throw new InvalidOperationException("Chinese title not rendered");
                if (!RichUiAudit.Find<TextBlock>(page).Any(text => text.Text == hit.Name)) throw new InvalidOperationException("Original title not rendered");
            }
        }
        var filtered = await AppServices.Client.CallAsync<List<CatalogItem>>("search_mods", new { query = "机械动力", source = "CurseForge", extra = new { game_version = "1.20.1", category = "" } }) ?? new();
        if (!filtered.Any(row => row.IdValue?.ToString() == "328085" && row.Name == "Create") || filtered.Any(row => row.Source != "curseforge")) throw new InvalidOperationException("CurseForge alias/filter integration failed");
        Console.WriteLine("[SEARCH PASS] CurseForge + Minecraft 1.20.1 preserves Create project 328085");
        var create = filtered.First(row => row.IdValue?.ToString() == "328085");
        await AppServices.Client.CallAsync("toggle_favorite", new { item = create });
        var favorites = await AppServices.Client.CallAsync<List<CatalogItem>>("catalog_favorites");
        var saved = favorites?.Single(row => row.IdValue?.ToString() == "328085");
        if (saved?.Name != "Create" || saved.ChineseName != "机械动力" || saved.Kind != "mod")
            throw new InvalidOperationException("Favorite lost the project type or Chinese title");
        await AppServices.Client.CallAsync("toggle_favorite", new { item = create });
        Console.WriteLine("[FAVORITE PASS] Chinese title, original title, resource kind and project ID survive saving");
        var details = new ProjectDetailsPanel(create, "1.20.1");
        Dlg.Panel(create.DisplayName, details, 760);
        await details.EnsureLoadedAsync();
        await MaterialControlsPreview.Layout(window);
        await WaitForArtwork(details, "Create details");
        if (!RichUiAudit.Find<TextBlock>(details).Any(text => text.Text == "机械动力") ||
            !RichUiAudit.Find<TextBlock>(details).Any(text => text.Text == "Create")) throw new InvalidOperationException("Detail title lost Chinese or upstream identity");
        RichUiAudit.Shot(window, folder, "create-details");
        CloseDialog(window);
        Console.WriteLine("[DETAIL PASS] Chinese and original names remain consistent in the live CurseForge detail panel");
        await CatalogTranslationAudit.Run(window, folder);
        Console.WriteLine("[CHINESE CATALOG PASS] Six resource types, two themes, source identity and real upstream images verified. No resources installed.");
    }

    private static Task WaitForArtwork(DependencyObject root, string label) => MaterialControlsPreview.Until(() =>
        RichUiAudit.Find<ProjectCover>(root).Any(cover => cover.Ready) && RichUiAudit.Find<ProjectCover>(root)
            .All(cover => !RichUiAudit.Field<TextBlock>(cover, "_fallback").Text.Contains("加载中")), "upstream artwork settled for " + label, 35);

    private static async Task VerifyCategories()
    {
        await Task.Run(() =>
        {
            var dm = new DownloadManager(2L);
            var modrinth = Py.ListOr(dm.FetchJson("https://api.modrinth.com/v2/tag/category")).OfType<PyDict>()
                .Where(row => CatalogFiles.KIND_CF.ContainsKey(Py.S(row.Get("project_type")))).Select(row => Py.S(row.Get("name")));
            var curseforge = Mods.CfItems(Mods.CfFetch(dm, "/categories", Config.CurseforgeKey(), PyDict.Of("gameId", 432L))).OfType<PyDict>()
                .Where(row => CatalogFiles.KIND_CF.Values.Contains(Py.IntOr(row.Get("classId"), 0))).Select(row => Py.S(row.Get("name")));
            var categories = modrinth.Concat(curseforge).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = categories.Where(category => !ResourceNames.ContainsChinese(CatalogPresentation.Category(category))).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Untranslated upstream categories: " + string.Join(", ", missing));
            Console.WriteLine("[CATEGORY PASS] " + categories.Length + " current upstream download categories have Chinese labels");
        });
    }

    internal static void CloseDialog(MainWindow window) => Dlg.Host!.Children[Dlg.Host.Children.Count - 1].RaiseEvent(
        new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0,
            System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
}
