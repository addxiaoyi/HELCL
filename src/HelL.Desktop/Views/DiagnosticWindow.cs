using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Core.Diagnostics;

namespace HelL.Desktop.Views;

internal sealed class DiagnosticWindow : Window
{
    private readonly Dictionary<CheckBox, DiagnosticSections> sections = new();
    private readonly StackPanel fields = new() { Spacing = 4 };
    private readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 260 };
    private readonly TextBlock notice = new() { Text = "选择诊断项目并检查预览后手动保存；不会自动上传。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button save = new() { Content = "保存 ZIP 诊断包", IsEnabled = false };
    private readonly Button refresh = new() { Content = "刷新预览" };
    private readonly Button cancel = new() { Content = "取消当前操作", IsVisible = false };
    private readonly IReadOnlyList<DiagnosticIssue> recentIssues;
    private DiagnosticReport? fullReport;
    private DiagnosticReport? report;
    private CancellationTokenSource? operation;
    private bool busy;
    private bool closed;

    public DiagnosticWindow(IReadOnlyList<DiagnosticIssue> recentIssues)
    {
        this.recentIssues = recentIssues;
        Title = "诊断包";
        Width = 760; Height = 700; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        body.Children.Add(notice);
        AddSection(body, "应用版本", DiagnosticSections.Application, true);
        AddSection(body, "系统与 .NET 环境", DiagnosticSections.Environment, true);
        AddSection(body, "可用磁盘空间", DiagnosticSections.Storage, true);
        AddSection(body, "实例数量", DiagnosticSections.Library, true);
        AddSection(body, "启动预检结果", DiagnosticSections.LaunchChecks, false);
        AddSection(body, "下载源状态（会发送匿名探测请求）", DiagnosticSections.Network, false);
        AddSection(body, "最近失败类别（当前会话）", DiagnosticSections.RecentIssues, false);
        body.Children.Add(new Expander { Header = "选择要保留的字段", Content = fields });
        body.Children.Add(preview);
        var actions = new StackPanel { Spacing = 8 };
        refresh.Click += Refresh;
        save.Click += Save;
        cancel.Click += (_, _) => operation?.Cancel();
        actions.Children.Add(refresh); actions.Children.Add(save); actions.Children.Add(cancel);
        body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Opened += Refresh;
        Closed += (_, _) => { closed = true; operation?.Cancel(); };
    }

    private void AddSection(Panel body, string label, DiagnosticSections value, bool selected)
    {
        var check = new CheckBox { Content = label, IsChecked = selected };
        sections.Add(check, value);
        check.IsCheckedChanged += Refresh;
        body.Children.Add(check);
    }

    private async void Refresh(object? sender, EventArgs e)
    {
        if (busy || closed) return;
        using var pending = new CancellationTokenSource();
        operation = pending;
        SetBusy(true);
        report = fullReport = null;
        preview.Text = "";
        fields.Children.Clear();
        notice.Text = "正在收集所选诊断信息…";
        try
        {
            DiagnosticSections selected = sections.Where(entry => entry.Key.IsChecked == true)
                .Aggregate(DiagnosticSections.None, (flags, entry) => flags | entry.Value);
            DiagnosticReport captured = await Task.Run(() => DiagnosticCapture.Collect(selected, recentIssues, pending.Token), pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (closed) return;
            report = fullReport = captured;
            foreach (DiagnosticField field in captured.Fields)
            {
                var check = new CheckBox { Content = field.Key + " · " + field.Value, IsChecked = true, Tag = field.Key };
                check.IsCheckedChanged += SelectFields;
                fields.Children.Add(check);
            }
            preview.Text = report.ToJson();
            notice.Text = "预览已更新，检查并选择字段后保存。";
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        { if (!closed) notice.Text = "诊断收集已取消，可重新刷新预览。"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!closed) notice.Text = "收集失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
        finally { Finish(pending); }
    }

    private void SelectFields(object? sender, RoutedEventArgs e)
    {
        if (fullReport is null || busy || closed) return;
        report = fullReport.SelectFields(fields.Children.OfType<CheckBox>().Where(check => check.IsChecked == true).Select(check => (string)check.Tag!));
        preview.Text = report.ToJson();
    }

    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (report is null || busy || closed) return;
        DiagnosticReport selected = report;
        using var pending = new CancellationTokenSource();
        operation = pending;
        SetBusy(true);
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存诊断包", SuggestedFileName = $"hell-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
                DefaultExtension = "zip", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
            });
            pending.Token.ThrowIfCancellationRequested();
            if (file is null) { notice.Text = "未选择保存位置，可再次保存。"; return; }
            string? path = file.TryGetLocalPath();
            if (path is null) throw new IOException("请选择本机文件夹，以便安全地保存诊断包。");
            await selected.SavePackageAsync(path, pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (!closed) notice.Text = "诊断包已保存，没有上传任何数据。";
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        { if (!closed) notice.Text = "诊断导出已取消，可再次保存。"; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!closed) notice.Text = "保存失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
        finally { Finish(pending); }
    }

    private void Finish(CancellationTokenSource pending)
    {
        if (ReferenceEquals(operation, pending)) operation = null;
        if (!closed) SetBusy(false);
        else busy = false;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        refresh.IsEnabled = !value;
        save.IsEnabled = !value && report is not null;
        foreach (CheckBox check in sections.Keys) check.IsEnabled = !value;
        fields.IsEnabled = !value;
        cancel.IsVisible = value;
    }
}
