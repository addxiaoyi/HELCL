using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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

internal static class ResourceWorkflowCheck
{
    internal static async Task Run(MainWindow window, string folder, bool documentsOnly = false)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Width = 800; window.Height = 740; window.Title = "HelL · 中文资源完整流程验收";
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:1" } });
        const string instanceName = "中文资源安装验收";
        await AppServices.Client.CallAsync("create_instance", new { name = instanceName });
        window.Navigate("resourcepack", instant: true); await Program.WaitForPage(window, "resourcepack");
        var page = RichUiAudit.Find<CatalogPage>(window).Single();
        RichUiAudit.Field<ComboBox>(page, "_inst").SelectedItem = instanceName;
        var project = await Search(page, "钠汉化", "Modrinth");
        Check(project.Name == "Translations for Sodium", "Chinese search finds the original project");
        if (documentsOnly)
        {
            await Documents(window, project, folder);
            Console.WriteLine("[RESOURCE DOCUMENT PASS] Full description, selected changelog, stop, resume, cache and originals verified");
            return;
        }
        await NameEditor(window, page, project, folder);
        await Install(window, page, instanceName, "Modrinth", folder);
        await Install(window, page, instanceName, "CurseForge", folder);
        await Documents(window, project, folder);
        Console.WriteLine("[RESOURCE WORKFLOW PASS] Custom names, documents, Chinese search, cancellation, pinned versions and two verified downloads completed in isolated profile " + Utils.ROOT);
    }

    private static async Task<CatalogItem> Search(CatalogPage page, string query, string source)
    {
        typeof(CatalogPage).GetField("_sync", RichUiAudit.Hidden)!.SetValue(page, true);
        RichUiAudit.Field<ComboBox>(page, "_source").SelectedItem = source;
        RichUiAudit.Field<TextBox>(page, "_search").Text = query;
        RichUiAudit.Field<System.Windows.Threading.DispatcherTimer>(page, "_debounce").Stop();
        typeof(CatalogPage).GetField("_sync", RichUiAudit.Hidden)!.SetValue(page, false);
        await (Task)RichUiAudit.Invoke(page, "SearchAsync")!;
        var rows = RichUiAudit.Field<ListBox>(page, "_results").Items.OfType<CatalogItem>().ToArray();
        var project = rows.FirstOrDefault(row => row.Slug == "translations-for-sodium");
        if (project == null) throw new InvalidOperationException("Search missing target: " + source + " " + query + "; game=" + RichUiAudit.Invoke(page, "GameVersionFilter") +
            "; rows=" + string.Join(",", rows.Take(8).Select(row => row.Slug)) + "; status=" + string.Join(" | ", RichUiAudit.Find<TextBlock>(RichUiAudit.Field<SPanel>(page, "_placeholder")).Select(text => text.Text)));
        return project;
    }

    private static async Task NameEditor(MainWindow window, CatalogPage page, CatalogItem project, string folder)
    {
        await (Task)RichUiAudit.Invoke(page, "ShowDetails", project)!;
        var detail = RichUiAudit.Find<ProjectDetailsPanel>(Dlg.Host!).Last();
        Named(detail, "修改中文名与别名").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "自定义中文名"), "name editor opens");
        RichUiAudit.Find<TextBox>(Dlg.Host!).Single(box => AutomationProperties.GetName(box) == "自定义中文名").Text = "我的钠汉化包";
        RichUiAudit.Find<TextBox>(Dlg.Host!).Single(box => AutomationProperties.GetName(box) == "自定义搜索别名").Text = "我的界面汉化\nmysodiumui";
        await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "custom-name-editor");
        Named(Dlg.Host!, "保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "自定义中文名"), "name editor saves");
        Check(project.ChineseName == "我的钠汉化包" && project.Name == "Translations for Sodium", "custom name updates list without renaming upstream project");
        CatalogChinesePreview.CloseDialog(window);
        var renamed = await Search(page, "mysodiumui", "Modrinth");
        Check(renamed.ChineseName == "我的钠汉化包", "custom English shorthand searches the upstream project");
        renamed = await Search(page, "我的界面汉化", "Modrinth");
        Check(renamed.Name == project.Name, "custom Chinese alias searches the upstream project");
        await (Task)RichUiAudit.Invoke(page, "ShowDetails", renamed)!;
        detail = RichUiAudit.Find<ProjectDetailsPanel>(Dlg.Host!).Last();
        Named(detail, "修改中文名与别名").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "自定义中文名"), "name editor reloads saved correction");
        Named(Dlg.Host!, "恢复默认名称与别名").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Named(Dlg.Host!, "保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !RichUiAudit.Find<TextBox>(Dlg.Host!).Any(box => AutomationProperties.GetName(box) == "自定义中文名"), "defaults restored");
        Check(renamed.ChineseName == "钠界面汉化", "reset restores builtin Chinese name");
        CatalogChinesePreview.CloseDialog(window);
    }

    private static async Task Documents(MainWindow window, CatalogItem project, string folder)
    {
        var panel = new ResourceDocumentPanel(project, "description");
        Dlg.Panel("完整介绍验收", panel, 720);
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Field<Expander>(panel, "_expand").IsExpanded = true;
        await Until(() => RichUiAudit.Field<object>(panel, "_document") != null && !RichUiAudit.Field<bool>(panel, "_loading"), "full description loads");
        var source = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = project.Source, slug = project.Slug, section = "description" });
        Check(source.GetProperty("original").GetString()!.Length > 600, "full description is not the short summary");
        await (Task)RichUiAudit.Invoke(panel, "Translate")!;
        Check(RichUiAudit.Field<TextBlock>(panel, "_status").Text.Contains("暂停"), "translation failure preserves original with retry");
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = Environment.GetEnvironmentVariable("HELL_TRANSLATION_ENDPOINT") ?? "http://127.0.0.1:11434" } });
        Task partial = (Task)RichUiAudit.Invoke(panel, "Translate")!;
        RichUiAudit.Field<Button>(panel, "_stop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await partial;
        var paused = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = project.Source, slug = project.Slug, section = "description" });
        Check(paused.GetProperty("translations").EnumerateArray().Count(value => !string.IsNullOrEmpty(value.GetString())) == 1, "stop preserves completed paragraph without translating the next one");
        await (Task)RichUiAudit.Invoke(panel, "Translate")!;
        Console.WriteLine("[DOCUMENT STATUS] " + RichUiAudit.Field<TextBlock>(panel, "_status").Text);
        var translated = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = project.Source, slug = project.Slug, section = "description" });
        File.WriteAllText(Path.Combine(folder, "description.json"), translated.GetRawText());
        Check(translated.GetProperty("translations").EnumerateArray().All(value => !string.IsNullOrEmpty(value.GetString())), "all description chunks translated and cached");
        foreach (bool dark in new[] { false, true }) { App.ApplyTheme(dark); await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "document-" + dark); }
        Named(panel, "原文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(RichUiAudit.Field<bool>(panel, "_original"), "full original remains accessible");
        CatalogChinesePreview.CloseDialog(window);

        var logProject = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "3d-crops" }) ?? throw new InvalidOperationException("Changelog project missing");
        var releases = await AppServices.Client.CallAsync<List<CatalogFile>>("list_catalog_files", new { extra = new { source = logProject.Source, slug = logProject.Slug, kind = "resourcepack" } });
        var chosen = releases![1];
        panel = new ResourceDocumentPanel(logProject, "changelog", version: chosen.IdValue!.ToString()!);
        Dlg.Panel("所选版本更新日志", panel, 720); await MaterialControlsPreview.Layout(window); RichUiAudit.Field<Expander>(panel, "_expand").IsExpanded = true;
        await Until(() => RichUiAudit.Field<object>(panel, "_document") != null && !RichUiAudit.Field<bool>(panel, "_loading"), "pinned changelog loads");
        await (Task)RichUiAudit.Invoke(panel, "Translate")!;
        var changelog = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = logProject.Source, slug = logProject.Slug, section = "changelog", version = chosen.IdValue!.ToString() });
        Check(changelog.GetProperty("version").GetString() == chosen.IdValue!.ToString() &&
            changelog.GetProperty("translations").EnumerateArray().All(value => !string.IsNullOrEmpty(value.GetString())), "selected version changelog translated and cached");
        File.WriteAllText(Path.Combine(folder, "changelog.json"), changelog.GetRawText());
        await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "changelog"); CatalogChinesePreview.CloseDialog(window);
        foreach (string section in new[] { "description", "changelog" })
        {
            var cf = await AppServices.Client.CallAsync<JsonElement>("catalog_document", new { source = "curseforge", id = "867964", section });
            Check(cf.GetProperty("original").GetString()!.Length > 0, "CurseForge " + section + " loads from its real endpoint");
            File.WriteAllText(Path.Combine(folder, "cf-" + section + ".json"), cf.GetRawText());
        }
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:1" } });
    }

    private static async Task Install(MainWindow window, CatalogPage page, string instanceName, string source, string folder)
    {
        var project = await Search(page, "钠汉化", source);
        var target = Path.Combine(new Instance(instanceName).path, "resourcepacks");
        int before = Directory.Exists(target) ? Directory.GetFiles(target).Length : 0;
        Task start = (Task)RichUiAudit.Invoke(page, "InstallAsync", project, new Button())!;
        await Until(() => RichUiAudit.Find<RadioButton>(Dlg.Host!).Any(radio => radio.Tag is CatalogFile), "version files shown for " + source);
        Named(Dlg.Host!, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await start;
        Check((Directory.Exists(target) ? Directory.GetFiles(target).Length : 0) == before, "cancelled version selection writes nothing for " + source);

        start = (Task)RichUiAudit.Invoke(page, "InstallAsync", project, new Button())!;
        await Until(() => RichUiAudit.Find<RadioButton>(Dlg.Host!).Any(radio => radio.Tag is CatalogFile), "version picker reopens for " + source);
        var options = RichUiAudit.Find<RadioButton>(Dlg.Host!).Where(radio => radio.Tag is CatalogFile).ToArray();
        var radio = options[Math.Min(source == "Modrinth" ? 1 : 2, options.Length - 1)];
        radio.IsChecked = true; var chosen = (CatalogFile)radio.Tag;
        Check(chosen.Filename.Length > 0, "selected file has upstream filename");
        await MaterialControlsPreview.Layout(window); RichUiAudit.Shot(window, folder, "version-picker-" + source);
        var oldTasks = TaskStore.Rows.Select(row => row.Id).ToArray();
        Named(Dlg.Host!, "安装所选").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await start;
        await Until(() => TaskStore.Rows.Any(row => !oldTasks.Contains(row.Id)), "install task created for " + source);
        string task = TaskStore.Rows.Last(row => !oldTasks.Contains(row.Id)).Id;
        var outcome = await AppServices.Client.CallAsync<OpResult>("wait_task", new { task_id = task, timeout = 120 });
        Check(outcome?.Ok == true, "install task succeeded for " + source);
        string installed = Path.Combine(target, chosen.Filename);
        Check(File.Exists(installed), "selected file installed for " + source);
        using (var zip = ZipFile.OpenRead(installed)) Check(zip.Entries.Any(entry => entry.FullName == "pack.mcmeta"), "installed archive is a real resource pack");
        var dm = new DownloadManager(2L); string expected, actual;
        if (source == "Modrinth")
        {
            var release = (PyDict)dm.FetchJson("https://api.modrinth.com/v2/version/" + chosen.IdValue);
            var file = Py.ListOr(release["files"]).OfType<PyDict>().Single(row => Py.S(row["filename"]) == chosen.Filename);
            expected = Py.S(((PyDict)file["hashes"])["sha512"]);
            using var sha = SHA512.Create(); using var stream = File.OpenRead(installed); actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        else
        {
            var response = (PyDict)Mods.CfFetch(dm, "/mods/" + project.IdValue + "/files/" + chosen.IdValue, Config.CurseforgeKey());
            var file = (PyDict)response["data"];
            expected = Py.S(Py.ListOr(file["hashes"]).OfType<PyDict>().Single(row => Py.Int(row["algo"]) == 1)["value"]);
            using var sha = SHA1.Create(); using var stream = File.OpenRead(installed); actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        Check(actual.Equals(expected, StringComparison.OrdinalIgnoreCase), "installed file hash matches selected upstream release for " + source);
        File.WriteAllText(Path.Combine(folder, "installed-" + source + ".json"), JsonSerializer.Serialize(new { source, project = project.Name, projectId = project.IdValue,
            version = chosen.IdValue, filename = chosen.Filename, installed, bytes = new FileInfo(installed).Length, hash = actual, hashMatches = true, cancelledWroteNothing = true }));
    }

    private static Button Named(DependencyObject root, string name) => RichUiAudit.Find<Button>(root).Last(button => button.IsVisible && (button.Content as string == name || AutomationProperties.GetName(button) == name));
    private static Task Until(Func<bool> predicate, string message) => MaterialControlsPreview.Until(predicate, message, 50);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
}
