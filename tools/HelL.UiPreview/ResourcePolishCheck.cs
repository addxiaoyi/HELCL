using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class ResourcePolishCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 词表、对照与版本验收"; window.Width = 1180; window.Height = 800;
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:1" } });
        await Names(window, folder);
        await Corrections(window, folder);
        await Versions(window, folder);
        Console.WriteLine("[RESOURCE POLISH PASS] Import/export/conflicts, bilingual corrections, persistence and official version ordering verified");
    }

    private static async Task Names(MainWindow window, string folder)
    {
        var incoming = new CustomResourceName { Kind = "mod", Source = "modrinth", Slug = "sodium", English = "Sodium", Chinese = "导入钠优化", Aliases = new[] { "帧率救星" } };
        CustomResourceNames.Save(incoming);
        string path = Path.Combine(folder, "custom-names.json");
        await AppServices.Client.CallAsync("export_resource_names", new { path });
        incoming.Chinese = "我的钠"; incoming.Aliases = new[] { "本机别名" }; CustomResourceNames.Save(incoming);
        window.Navigate("resourcepack", instant: true); await Program.WaitForPage(window, "resourcepack");
        var page = RichUiAudit.Find<CatalogPage>(window).Single();
        await (Task)RichUiAudit.Invoke(page, "ShowLanguageIndex")!;
        var panel = RichUiAudit.Find<ContentControl>(Dlg.Host!).Single(control => control.GetType().Name == "ResourceNameTransferPanel");
        var import = (Task)RichUiAudit.Invoke(panel, "ShowImport", path)!;
        await Until(() => RichUiAudit.Find<TextBlock>(Dlg.Host!).Any(text => text.Text.Contains("冲突 1 项")), "import preview lists conflict");
        Check(CustomResourceNames.Find("modrinth", "sodium", "")!.Chinese == "我的钠", "preview preserves current names");
        var defaultChoice = RichUiAudit.Find<ComboBox>(Dlg.Host!).First(combo => combo.Items.Contains("保留名称并合并别名"));
        defaultChoice.SelectedIndex = 2;
        await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "import-conflict");
        Named(Dlg.Host!, "确认导入").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await import;
        var merged = CustomResourceNames.Find("modrinth", "sodium", "")!;
        Check(merged.Chinese == "我的钠" && merged.Aliases.Contains("帧率救星") && merged.Aliases.Contains("导入钠优化"), "merge keeps current Chinese and adds imported aliases");
        Check(File.Exists(Path.Combine(Utils.ROOT, "resource-names.json.import-backup")), "import creates backup");
        await AppServices.Client.CallAsync("export_resource_names", new { path = Path.Combine(folder, "merged-names.json") });
        CatalogChinesePreview.CloseDialog(window);
    }

    private static async Task Corrections(MainWindow window, string folder)
    {
        var project = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "3d-crops" })!;
        var panel = new ResourceDocumentPanel(project!, "changelog", version: "iKYRZwnm");
        Dlg.Panel("中英对照验收", panel, 1020);
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Field<Expander>(panel, "_expand").IsExpanded = true;
        await Until(() => RichUiAudit.Field<object>(panel, "_document") != null && !RichUiAudit.Field<bool>(panel, "_loading"), "document loads");
        Named(panel, "中英对照与修订").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await MaterialControlsPreview.Layout(window);
        Named(panel, "修正本段").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "段落中文译文"), "paragraph editor opens");
        var input = RichUiAudit.Find<TextBox>(Dlg.Host!).Single(box => AutomationProperties.GetName(box) == "段落中文译文");
        input.Text = "重新加入了甘蔗模型。";
        RichUiAudit.Shot(window, folder, "paragraph-editor");
        Named(Dlg.Host!, "保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "段落中文译文"), "correction saves");
        var document = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = "modrinth", slug = "3d-crops", section = "changelog", version = "iKYRZwnm" });
        Check(document.GetProperty("translations")[0].GetString() == input.Text && document.GetProperty("manual")[0].GetBoolean(), "correction survives document reload");
        var translated = await AppServices.Client.CallAsync<JsonElement>("translate_catalog_document", new { key = document.GetProperty("key").GetString(), index = 0, original = document.GetProperty("parts")[0].GetString() });
        Check(translated.GetProperty("text").GetString() == input.Text, "automatic translation never overwrites correction");
        foreach (bool dark in new[] { false, true })
            foreach (int width in new[] { 1180, 800 })
            {
                App.ApplyTheme(dark); window.Width = width; await MaterialControlsPreview.Layout(window);
                Check(RichUiAudit.Find<TextBlock>(panel).Any(text => text.Text == "中文 · 手动修订"), "manual attribution visible");
                Check(RichUiAudit.Field<Button>(panel, "_toggle").Visibility == Visibility.Collapsed, "comparison does not show an ineffective single-language toggle");
                RichUiAudit.Shot(window, folder, "comparison-" + dark + "-" + width);
            }
        File.WriteAllText(Path.Combine(folder, "manual-document.json"), document.GetRawText());
        Named(panel, "恢复自动译文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => RichUiAudit.Find<Button>(Dlg.Host!).Any(button => button.Content as string == "确定"), "reset confirmation opens");
        Named(Dlg.Host!, "确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !RichUiAudit.Find<TextBlock>(panel).Any(text => text.Text == "中文 · 手动修订"), "reset restores uncorrected state");
        CatalogChinesePreview.CloseDialog(window);
    }

    private static async Task Versions(MainWindow window, string folder)
    {
        var files = await AppServices.Client.CallAsync<List<CatalogFile>>("list_catalog_files", new { extra = new { source = "modrinth", slug = "translations-for-sodium", kind = "resourcepack" } }) ?? new();
        var direct = await Task.Run(() => Py.ListOr(new DownloadManager().FetchJson("https://api.modrinth.com/v2/project/translations-for-sodium/version", expand: false)).OfType<PyDict>().OrderByDescending(row => CatalogVersions.Timestamp(Py.S(row.Get("date_published")))).First());
        Check(files.Count > 0 && files[0].IdValue?.ToString() == Py.S(direct["id"]), "launcher newest file equals live upstream newest version");
        Check(files[0].VersionSource == "官方实时列表", "official source verified before mirror fallback");
        var page = RichUiAudit.Find<CatalogPage>(window).Single();
        var item = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "translations-for-sodium" }) ?? throw new IOException("Project not found");
        var extra = new Dictionary<string, object?> { ["source"] = "modrinth", ["slug"] = item.Slug, ["name"] = item.Name, ["kind"] = "resourcepack" };
        var picker = (Task<Dictionary<string, object?>?>)RichUiAudit.Invoke(page, "PickVersionFileAsync", item, extra)!;
        await Until(() => RichUiAudit.Find<RadioButton>(Dlg.Host!).Any(radio => radio.Tag is CatalogFile), "latest version picker opens");
        var first = (CatalogFile)RichUiAudit.Find<RadioButton>(Dlg.Host!).First(radio => radio.Tag is CatalogFile).Tag;
        Check(first.IdValue!.ToString() == files[0].IdValue!.ToString(), "visible first version is newest");
        await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "latest-version-picker");
        Named(Dlg.Host!, "安装最新").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var selected = await picker;
        Check(selected? ["version_id"]?.ToString() == files[0].IdValue!.ToString(), "latest action pins the verified displayed file");
        File.WriteAllText(Path.Combine(folder, "latest-version.json"), JsonSerializer.Serialize(new { latestId = files[0].IdValue, files[0].VersionNumber,
            files[0].Date, files[0].VersionSource, count = files.Count, pinnedLatest = true }));
        var cf = await AppServices.Client.CallAsync<List<CatalogFile>>("list_catalog_files", new { extra = new { source = "curseforge", id = 867964, kind = "resourcepack" } }) ?? new();
        Check(cf.Count > 0 && cf.All(file => file.VersionSource.Length > 0), "CurseForge reports the actual metadata source");
        Check(cf.Select(file => CatalogVersions.Timestamp(file.Date)).SequenceEqual(cf.Select(file => CatalogVersions.Timestamp(file.Date)).OrderByDescending(time => time)), "CurseForge files are newest first");
        File.WriteAllText(Path.Combine(folder, "curseforge-latest.json"), JsonSerializer.Serialize(new { id = cf[0].IdValue, cf[0].VersionNumber, cf[0].Date, cf[0].VersionSource, cf[0].VersionWarning }));
    }

    private static Button Named(DependencyObject root, string name) => RichUiAudit.Find<Button>(root).Last(button => button.IsVisible && (button.Content as string == name || AutomationProperties.GetName(button) == name));
    private static Task Until(Func<bool> predicate, string description) => MaterialControlsPreview.Until(predicate, description, 35);
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
}
