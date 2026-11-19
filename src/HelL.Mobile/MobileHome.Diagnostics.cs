using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core;
using HelL.Core.Diagnostics;
using HelL.Core.Launcher;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private readonly StackPanel diagnosticLayout = new() { Spacing = 12 };
    private readonly StackPanel diagnosticChecks = new() { Spacing = 6 };
    private readonly StackPanel diagnosticFields = new() { Spacing = 4 };
    private readonly TextBlock diagnosticStatus = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "caption" } };
    private readonly TextBox diagnosticPreview = new()
    {
        AcceptsReturn = true,
        IsReadOnly = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Height = 240,
        MinHeight = 120,
        FontFamily = "monospace"
    };
    private readonly CheckBox includeApp = IncludeCheck("应用版本", true);
    private readonly CheckBox includeEnvironment = IncludeCheck("系统与 .NET 环境", true);
    private readonly CheckBox includeStorage = IncludeCheck("可用磁盘空间", true);
    private readonly CheckBox includeLibrary = IncludeCheck("实例数量", true);
    private readonly CheckBox includeLaunchChecks = IncludeCheck("启动预检代码", false);
    private readonly CheckBox includeNetwork = IncludeCheck("下载源连通状态（会发送无身份的探测请求）", false);
    private readonly CheckBox includeRecentIssues = IncludeCheck("最近失败摘要（脱敏后）", false);
    private readonly Queue<DiagnosticIssue> recentDiagnosticIssues = new();
    private DiagnosticReport? diagnosticReport;
    private DiagnosticReport? fullDiagnosticReport;
    private bool diagnosticBusy;

    private void InitializeDiagnostics()
    {
        if (MorePage.Content is not StackPanel more) throw new InvalidOperationException("The settings page is unavailable.");
        var button = new Button { Content = "预览并导出诊断包", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Classes = { "row-action" } };
        button.Click += OpenDiagnostics;
        more.Children.Add(button);

        diagnosticLayout.Children.Add(new TextBlock { Text = "诊断包", Classes = { "title" } });
        diagnosticLayout.Children.Add(new TextBlock
        {
            Text = "选择要包含的诊断项目。导出前会显示 JSON 预览；文件只保存在你选择的位置，不会自动上传。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Classes = { "caption" }
        });
        diagnosticLayout.Children.Add(diagnosticStatus);
        diagnosticChecks.Children.Add(includeApp);
        diagnosticChecks.Children.Add(includeEnvironment);
        diagnosticChecks.Children.Add(includeStorage);
        diagnosticChecks.Children.Add(includeLibrary);
        diagnosticChecks.Children.Add(includeLaunchChecks);
        diagnosticChecks.Children.Add(includeNetwork);
        diagnosticChecks.Children.Add(includeRecentIssues);
        diagnosticLayout.Children.Add(new Border { Classes = { "glass" }, Padding = new Avalonia.Thickness(14), Child = diagnosticChecks });
        diagnosticLayout.Children.Add(new Expander { Header = "选择要保留的字段", Content = diagnosticFields });
        diagnosticLayout.Children.Add(new TextBlock { Text = "导出预览", Classes = { "field-label" } });
        diagnosticLayout.Children.Add(diagnosticPreview);
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(DiagnosticButton("刷新预览", RefreshDiagnosticPreview));
        actions.Children.Add(DiagnosticButton("保存 ZIP 诊断包", ExportDiagnosticPackage));
        actions.Children.Add(DiagnosticButton("返回设置", ReturnFromDiagnostics));
        diagnosticLayout.Children.Add(actions);

        var diagnosticsPage = new ScrollViewer
        {
            IsVisible = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = diagnosticLayout,
            Margin = new Avalonia.Thickness(20, 20, 20, 30)
        };
        Pages.Children.Add(diagnosticsPage);
        diagnosticPage = diagnosticsPage;
        foreach (CheckBox check in diagnosticChecks.Children.OfType<CheckBox>()) check.IsCheckedChanged += DiagnosticSelectionChanged;
    }

    private ScrollViewer? diagnosticPage;

    private async void OpenDiagnostics(object? sender, RoutedEventArgs e)
    {
        ShowPage("more");
        MorePage.IsVisible = false;
        if (diagnosticPage is not null) diagnosticPage.IsVisible = true;
        ChromeTitle.Text = "诊断包";
        await RefreshDiagnosticPreview();
    }

    private async void DiagnosticSelectionChanged(object? sender, RoutedEventArgs e) => await RefreshDiagnosticPreview();

    private async void RefreshDiagnosticPreview(object? sender, RoutedEventArgs e) => await RefreshDiagnosticPreview();

    private async Task RefreshDiagnosticPreview()
    {
        if (disposed || operation is not null || diagnosticBusy) return;
        diagnosticBusy = true;
        diagnosticStatus.Text = "正在收集所选诊断信息…";
        diagnosticPreview.Text = "";
        Pages.IsEnabled = false;
        bool acquired = false;
        try
        {
            await session.Operations.WaitAsync();
            acquired = true;
            if (disposed) return;
            bool collectLaunchChecks = includeLaunchChecks.IsChecked == true;
            bool collectNetwork = includeNetwork.IsChecked == true;
            string selectedInstance = selected;
            string selectedVersion = reference;
            DiagnosticIssue[] recentIssues = recentDiagnosticIssues.ToArray();
            DiagnosticSnapshot snapshot = await Task.Run(() => CaptureDiagnosticSnapshot(collectLaunchChecks, collectNetwork,
                selectedInstance, selectedVersion, recentIssues));
            if (disposed) return;
            DiagnosticSections sections = SelectedDiagnosticSections();
            diagnosticReport = fullDiagnosticReport = DiagnosticReportBuilder.Build(snapshot, sections, DateTimeOffset.UtcNow);
            diagnosticFields.Children.Clear();
            foreach (DiagnosticField field in fullDiagnosticReport.Fields)
            {
                var check = IncludeCheck(field.Key + " — " + field.Value, true);
                check.Tag = field.Key;
                check.IsCheckedChanged += SelectDiagnosticFields;
                diagnosticFields.Children.Add(check);
            }
            diagnosticPreview.Text = diagnosticReport.ToJson();
            diagnosticStatus.Text = "预览已更新。检查内容后再手动保存。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            diagnosticReport = null;
            diagnosticStatus.Text = "诊断信息收集失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message);
        }
        finally
        {
            if (acquired) session.Operations.Release();
            diagnosticBusy = false;
            if (!disposed) Pages.IsEnabled = true;
        }
    }

    private void SelectDiagnosticFields(object? sender, RoutedEventArgs e)
    {
        if (fullDiagnosticReport is null) return;
        string[] keys = diagnosticFields.Children.OfType<CheckBox>().Where(check => check.IsChecked == true)
            .Select(check => (string)check.Tag!).ToArray();
        diagnosticReport = fullDiagnosticReport.SelectFields(keys);
        diagnosticPreview.Text = diagnosticReport.ToJson();
    }

    private DiagnosticSnapshot CaptureDiagnosticSnapshot(bool collectLaunchChecks, bool collectNetwork,
        string selectedInstance, string selectedVersion, IReadOnlyList<DiagnosticIssue> recentIssues)
    {
        var baseSnapshot = DiagnosticSnapshot.Collect(root,
            GetType().Assembly.GetName().Version?.ToString() ?? "unknown", instances.Count);
        IReadOnlyList<DiagnosticFinding> findings = [];
        if (collectLaunchChecks && selectedInstance.Length > 0)
        {
            PyDict report = Preflight.CheckLaunch(new Instance(selectedInstance), selectedVersion,
                java_exe: launcher.JavaPreference(selectedInstance));
            findings = (report.Get("items") as IEnumerable<object> ?? Array.Empty<object>())
                .OfType<PyDict>()
                .Select(item => new DiagnosticFinding(Py.S(item.Get("code")), Py.S(item.Get("level"))))
                .ToArray();
        }

        IReadOnlyList<DiagnosticMirror> mirrors = [];
        if (collectNetwork)
        {
            var report = MirrorHealth.Refresh(false);
            mirrors = (report.Get("samples") as IEnumerable<object> ?? Array.Empty<object>())
                .OfType<PyDict>()
                .Select(sample => new DiagnosticMirror(Py.S(sample.Get("provider")), Py.Truthy(sample.Get("available")), Py.Int(sample.Get("latency_ms"))))
                .ToArray();
        }
        return baseSnapshot with
        {
            LaunchFindings = findings,
            Mirrors = mirrors,
            RecentIssues = recentIssues
        };
    }

    private DiagnosticSections SelectedDiagnosticSections()
    {
        DiagnosticSections sections = DiagnosticSections.None;
        if (includeApp.IsChecked == true) sections |= DiagnosticSections.Application;
        if (includeEnvironment.IsChecked == true) sections |= DiagnosticSections.Environment;
        if (includeStorage.IsChecked == true) sections |= DiagnosticSections.Storage;
        if (includeLibrary.IsChecked == true) sections |= DiagnosticSections.Library;
        if (includeLaunchChecks.IsChecked == true) sections |= DiagnosticSections.LaunchChecks;
        if (includeNetwork.IsChecked == true) sections |= DiagnosticSections.Network;
        if (includeRecentIssues.IsChecked == true) sections |= DiagnosticSections.RecentIssues;
        return sections;
    }

    private async void ExportDiagnosticPackage(object? sender, RoutedEventArgs e)
    {
        if (diagnosticReport is null)
        {
            diagnosticStatus.Text = "请先刷新并检查预览。";
            return;
        }
        DiagnosticReport selectedReport = diagnosticReport;
        try
        {
            await using var package = new MemoryStream();
            await selectedReport.WritePackageAsync(package);
            package.Position = 0;
            var top = TopLevel.GetTopLevel(this) ?? throw new IOException("The file picker is unavailable.");
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存诊断包",
                SuggestedFileName = $"hell-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            await using Stream stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await package.CopyToAsync(stream);
            await stream.FlushAsync();
            diagnosticStatus.Text = "诊断包已保存到你选择的位置；没有上传任何内容。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            diagnosticStatus.Text = "保存失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message);
        }
    }

    private void ReturnFromDiagnostics(object? sender, RoutedEventArgs e) => ShowPage("more");

    private void RememberDiagnosticIssue(Exception error)
    {
        (string code, string summary) = error switch
        {
            UnauthorizedAccessException => ("permission_denied", "A local operation was denied by the operating system."),
            IOException => ("file_operation_failed", "A local file operation failed."),
            HttpRequestException => ("network_request_failed", "A network request failed."),
            OperationCanceledException => ("operation_cancelled", "The operation was cancelled."),
            _ => ("operation_failed", "An operation failed.")
        };
        recentDiagnosticIssues.Enqueue(new DiagnosticIssue(code, summary));
        while (recentDiagnosticIssues.Count > 8) recentDiagnosticIssues.Dequeue();
    }

    private static CheckBox IncludeCheck(string label, bool selected) => new() { Content = label, IsChecked = selected };
    private static Button DiagnosticButton(string label, EventHandler<RoutedEventArgs> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Classes = { "secondary" } };
        button.Click += action;
        return button;
    }
}
