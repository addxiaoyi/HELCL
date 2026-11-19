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
        AcceptsReturn = true, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Height = 240, MinHeight = 120, FontFamily = "monospace"
    };
    private readonly CheckBox includeApp = IncludeCheck("应用版本", true);
    private readonly CheckBox includeEnvironment = IncludeCheck("系统与 .NET 环境", true);
    private readonly CheckBox includeStorage = IncludeCheck("可用磁盘空间", true);
    private readonly CheckBox includeLibrary = IncludeCheck("实例数量", true);
    private readonly CheckBox includeLaunchChecks = IncludeCheck("启动预检结果", false);
    private readonly CheckBox includeNetwork = IncludeCheck("下载源连通状态（会发送匿名探测请求）", false);
    private readonly CheckBox includeRecentIssues = IncludeCheck("最近失败摘要（脱敏后）", false);
    private readonly Button diagnosticRefresh = DiagnosticButton("刷新预览", null);
    private readonly Button diagnosticExport = DiagnosticButton("保存 ZIP 诊断包", null);
    private readonly Button diagnosticCancel = DiagnosticButton("取消当前诊断操作", null);
    private readonly Queue<DiagnosticIssue> recentDiagnosticIssues = new();
    private DiagnosticReport? diagnosticReport;
    private DiagnosticReport? fullDiagnosticReport;
    private bool diagnosticBusy;
    private CancellationTokenSource? diagnosticOperation;
    private ScrollViewer? diagnosticPage;

    private void InitializeDiagnostics()
    {
        if (MorePage.Content is not StackPanel more) throw new InvalidOperationException("The settings page is unavailable.");
        var entry = new Button { Content = "预览并导出诊断包", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Classes = { "row-action" } };
        entry.Click += OpenDiagnostics;
        more.Children.Add(entry);
        diagnosticLayout.Children.Add(new TextBlock { Text = "诊断包", Classes = { "title" } });
        diagnosticLayout.Children.Add(new TextBlock
        {
            Text = "选择要包含的诊断项目，保存前会显示 JSON 预览。文件只保存到你选择的位置，不会自动上传。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "caption" }
        });
        diagnosticLayout.Children.Add(diagnosticStatus);
        foreach (CheckBox check in new[] { includeApp, includeEnvironment, includeStorage, includeLibrary, includeLaunchChecks, includeNetwork, includeRecentIssues })
            diagnosticChecks.Children.Add(check);
        diagnosticLayout.Children.Add(new Border { Classes = { "glass" }, Padding = new Avalonia.Thickness(14), Child = diagnosticChecks });
        diagnosticLayout.Children.Add(new Expander { Header = "选择要保留的字段", Content = diagnosticFields });
        diagnosticLayout.Children.Add(new TextBlock { Text = "导出预览", Classes = { "field-label" } });
        diagnosticLayout.Children.Add(diagnosticPreview);
        diagnosticRefresh.Click += RefreshDiagnosticPreview;
        diagnosticExport.Click += ExportDiagnosticPackage;
        diagnosticCancel.Click += CancelDiagnosticOperation;
        var actions = new StackPanel { Spacing = 8 };
        actions.Children.Add(diagnosticRefresh);
        actions.Children.Add(diagnosticExport);
        actions.Children.Add(diagnosticCancel);
        actions.Children.Add(DiagnosticButton("返回设置", ReturnFromDiagnostics));
        diagnosticLayout.Children.Add(actions);
        diagnosticPage = new ScrollViewer
        {
            IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = diagnosticLayout, Margin = new Avalonia.Thickness(20, 20, 20, 30)
        };
        Pages.Children.Add(diagnosticPage);
        foreach (CheckBox check in diagnosticChecks.Children.OfType<CheckBox>()) check.IsCheckedChanged += DiagnosticSelectionChanged;
        SetDiagnosticBusy(false);
    }

    private async void OpenDiagnostics(object? sender, RoutedEventArgs e)
    {
        ShowPage("diagnostics");
        await RefreshDiagnosticPreview();
    }

    private async void DiagnosticSelectionChanged(object? sender, RoutedEventArgs e) => await RefreshDiagnosticPreview();
    private async void RefreshDiagnosticPreview(object? sender, RoutedEventArgs e) => await RefreshDiagnosticPreview();

    private Task RefreshDiagnosticPreview() => RefreshDiagnosticPreviewAsync(null);

    private async Task RefreshDiagnosticPreviewAsync(Func<CancellationToken, Task<DiagnosticSnapshot>>? capture)
    {
        if (disposed || diagnosticBusy) return;
        if (operation is not null) { diagnosticStatus.Text = "请等待当前启动器任务完成后刷新诊断预览。"; return; }
        using var pending = new CancellationTokenSource();
        diagnosticOperation = pending;
        SetDiagnosticBusy(true);
        diagnosticStatus.Text = "正在收集所选诊断信息…";
        diagnosticPreview.Text = "";
        diagnosticReport = fullDiagnosticReport = null;
        diagnosticFields.Children.Clear();
        bool acquired = false;
        try
        {
            await session.Operations.WaitAsync(pending.Token);
            acquired = true;
            pending.Token.ThrowIfCancellationRequested();
            bool collectLaunchChecks = includeLaunchChecks.IsChecked == true;
            bool collectNetwork = includeNetwork.IsChecked == true;
            string selectedInstance = selected;
            string selectedVersion = reference;
            int instanceCount = instances.Count;
            DiagnosticSections sections = SelectedDiagnosticSections();
            DiagnosticIssue[] issues = recentDiagnosticIssues.ToArray();
            DiagnosticSnapshot snapshot = capture is null
                ? await Task.Run(() => CaptureDiagnosticSnapshot(collectLaunchChecks, collectNetwork,
                    selectedInstance, selectedVersion, instanceCount, issues, pending.Token), pending.Token)
                : await capture(pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (disposed || diagnosticPage?.IsVisible != true) return;
            diagnosticReport = fullDiagnosticReport = DiagnosticReportBuilder.Build(snapshot, sections, DateTimeOffset.UtcNow);
            foreach (DiagnosticField field in fullDiagnosticReport.Fields)
            {
                var check = IncludeCheck(field.Key + " · " + field.Value, true);
                check.Tag = field.Key;
                check.IsCheckedChanged += SelectDiagnosticFields;
                diagnosticFields.Children.Add(check);
            }
            diagnosticPreview.Text = diagnosticReport.ToJson();
            diagnosticStatus.Text = "预览已更新，检查内容并选择字段后保存。";
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        { if (!disposed && diagnosticPage?.IsVisible == true) diagnosticStatus.Text = "诊断收集已取消，可重新刷新预览。"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && diagnosticPage?.IsVisible == true && !pending.IsCancellationRequested)
                diagnosticStatus.Text = "诊断信息收集失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message);
        }
        finally
        {
            if (acquired) session.Operations.Release();
            if (ReferenceEquals(diagnosticOperation, pending)) diagnosticOperation = null;
            if (!disposed) SetDiagnosticBusy(false);
            else diagnosticBusy = false;
        }
    }

    private void SelectDiagnosticFields(object? sender, RoutedEventArgs e)
    {
        if (fullDiagnosticReport is null || diagnosticBusy || disposed) return;
        string[] keys = diagnosticFields.Children.OfType<CheckBox>().Where(check => check.IsChecked == true)
            .Select(check => (string)check.Tag!).ToArray();
        diagnosticReport = fullDiagnosticReport.SelectFields(keys);
        diagnosticPreview.Text = diagnosticReport.ToJson();
    }

    private DiagnosticSnapshot CaptureDiagnosticSnapshot(bool collectLaunchChecks, bool collectNetwork,
        string selectedInstance, string selectedVersion, int instanceCount, IReadOnlyList<DiagnosticIssue> issues,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        DiagnosticSnapshot snapshot = DiagnosticSnapshot.Collect(root, GetType().Assembly.GetName().Version?.ToString() ?? "unknown", instanceCount);
        IReadOnlyList<DiagnosticFinding> findings = [];
        if (collectLaunchChecks && selectedInstance.Length > 0)
        {
            PyDict preflight = Preflight.CheckLaunch(new Instance(selectedInstance), selectedVersion, java_exe: launcher.JavaPreference(selectedInstance));
            findings = (preflight.Get("items") as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>()
                .Select(item => new DiagnosticFinding(Py.S(item.Get("code")), Py.S(item.Get("level")))).ToArray();
        }
        cancellation.ThrowIfCancellationRequested();
        IReadOnlyList<DiagnosticMirror> mirrors = [];
        if (collectNetwork)
        {
            PyDict health = MirrorHealth.Refresh(false);
            mirrors = (health.Get("samples") as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>()
                .Select(sample => new DiagnosticMirror(Py.S(sample.Get("provider")), Py.Truthy(sample.Get("available")), Py.Int(sample.Get("latency_ms")))).ToArray();
        }
        cancellation.ThrowIfCancellationRequested();
        return snapshot with { LaunchFindings = findings, Mirrors = mirrors, RecentIssues = issues };
    }

    private DiagnosticSections SelectedDiagnosticSections()
    {
        DiagnosticSections selected = DiagnosticSections.None;
        if (includeApp.IsChecked == true) selected |= DiagnosticSections.Application;
        if (includeEnvironment.IsChecked == true) selected |= DiagnosticSections.Environment;
        if (includeStorage.IsChecked == true) selected |= DiagnosticSections.Storage;
        if (includeLibrary.IsChecked == true) selected |= DiagnosticSections.Library;
        if (includeLaunchChecks.IsChecked == true) selected |= DiagnosticSections.LaunchChecks;
        if (includeNetwork.IsChecked == true) selected |= DiagnosticSections.Network;
        if (includeRecentIssues.IsChecked == true) selected |= DiagnosticSections.RecentIssues;
        return selected;
    }

    private async void ExportDiagnosticPackage(object? sender, RoutedEventArgs e) => await ExportDiagnosticPackageAsync(async (report, cancellation) =>
    {
        await using var package = new MemoryStream();
        await report.WritePackageAsync(package, cancellation);
        package.Position = 0;
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("The file picker is unavailable.");
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存诊断包", SuggestedFileName = $"hell-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            DefaultExtension = "zip", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
        });
        using (file)
        {
            cancellation.ThrowIfCancellationRequested();
            if (file is null) return false;
            await using Stream stream = await file.OpenWriteAsync();
            cancellation.ThrowIfCancellationRequested();
            if (stream.CanSeek) stream.SetLength(0);
            await package.CopyToAsync(stream, cancellation);
            await stream.FlushAsync(cancellation);
            return true;
        }
    });

    private async Task ExportDiagnosticPackageAsync(Func<DiagnosticReport, CancellationToken, Task<bool>> save)
    {
        if (disposed || diagnosticBusy) return;
        if (diagnosticReport is null) { diagnosticStatus.Text = "请先刷新并检查预览。"; return; }
        DiagnosticReport selected = diagnosticReport;
        using var pending = new CancellationTokenSource();
        diagnosticOperation = pending;
        SetDiagnosticBusy(true);
        diagnosticStatus.Text = "请选择保存位置，导出期间预览内容已固定。";
        try
        {
            if (!await save(selected, pending.Token))
            {
                if (!disposed && diagnosticPage?.IsVisible == true) diagnosticStatus.Text = "未选择保存位置，可再次保存。";
                return;
            }
            pending.Token.ThrowIfCancellationRequested();
            if (!disposed && diagnosticPage?.IsVisible == true) diagnosticStatus.Text = "诊断包已保存，没有上传任何数据。";
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        { if (!disposed && diagnosticPage?.IsVisible == true) diagnosticStatus.Text = "诊断导出已取消，可再次保存。"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && diagnosticPage?.IsVisible == true && !pending.IsCancellationRequested)
                diagnosticStatus.Text = "导出失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message);
        }
        finally
        {
            if (ReferenceEquals(diagnosticOperation, pending)) diagnosticOperation = null;
            if (!disposed) SetDiagnosticBusy(false);
            else diagnosticBusy = false;
        }
    }

    private void SetDiagnosticBusy(bool busy)
    {
        diagnosticBusy = busy;
        diagnosticChecks.IsEnabled = diagnosticFields.IsEnabled = diagnosticRefresh.IsEnabled = !busy;
        diagnosticExport.IsEnabled = !busy && diagnosticReport is not null;
        diagnosticCancel.IsVisible = diagnosticCancel.IsEnabled = busy;
    }

    private void CancelDiagnosticOperation(object? sender, RoutedEventArgs e) => diagnosticOperation?.Cancel();
    private void DiagnosticPageChanged(bool visible) { if (!visible) diagnosticOperation?.Cancel(); }
    private void DisposeDiagnostics() => diagnosticOperation?.Cancel();
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
    private static Button DiagnosticButton(string label, EventHandler<RoutedEventArgs>? action)
    {
        var button = new Button { Content = label, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Classes = { "secondary" } };
        if (action is not null) button.Click += action;
        return button;
    }
}
