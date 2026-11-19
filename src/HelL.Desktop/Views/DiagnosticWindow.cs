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
    private readonly TextBlock notice = new() { Text = "选择诊断项目，检查预览后手动保存；不会自动上传。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button save = new() { Content = "保存 ZIP 诊断包", IsEnabled = false };
    private readonly Button refresh = new() { Content = "刷新预览" };
    private DiagnosticReport? fullReport;
    private DiagnosticReport? report;
    private bool busy;
    private readonly IReadOnlyList<DiagnosticIssue> recentIssues;

    public DiagnosticWindow(IReadOnlyList<DiagnosticIssue> recentIssues)
    {
        this.recentIssues = recentIssues;
        Title = "诊断包";
        Width = 760;
        Height = 700;
        MinWidth = 420;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(20) };
        body.Children.Add(notice);
        AddSection(body, "应用版本", DiagnosticSections.Application, true);
        AddSection(body, "系统与 .NET 环境", DiagnosticSections.Environment, true);
        AddSection(body, "可用磁盘空间", DiagnosticSections.Storage, true);
        AddSection(body, "实例数量", DiagnosticSections.Library, true);
        AddSection(body, "启动预检代码", DiagnosticSections.LaunchChecks, false);
        AddSection(body, "下载源状态（会发送无身份探测请求）", DiagnosticSections.Network, false);
        AddSection(body, "最近失败类别（当前会话）", DiagnosticSections.RecentIssues, false);
        body.Children.Add(new Expander { Header = "选择要保留的字段", Content = fields });
        body.Children.Add(preview);
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        refresh.Click += Refresh;
        save.Click += Save;
        actions.Children.Add(refresh);
        actions.Children.Add(save);
        body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Opened += Refresh;
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
        if (busy) return;
        SetBusy(true);
        notice.Text = "正在收集所选诊断信息…";
        try
        {
            DiagnosticSections selected = sections.Where(entry => entry.Key.IsChecked == true)
                .Aggregate(DiagnosticSections.None, (flags, entry) => flags | entry.Value);
            report = fullReport = await Task.Run(() => DiagnosticCapture.Collect(selected, recentIssues));
            fields.Children.Clear();
            foreach (DiagnosticField field in report.Fields)
            {
                var check = new CheckBox { Content = field.Key + " — " + field.Value, IsChecked = true, Tag = field.Key };
                check.IsCheckedChanged += SelectFields;
                fields.Children.Add(check);
            }
            preview.Text = report.ToJson();
            notice.Text = "预览已更新；可以剔除字段后保存。";
        }
        catch (Exception error) { report = null; preview.Text = ""; notice.Text = "收集失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
        finally { SetBusy(false); }
    }

    private void SelectFields(object? sender, RoutedEventArgs e)
    {
        if (fullReport is null) return;
        report = fullReport.SelectFields(fields.Children.OfType<CheckBox>().Where(check => check.IsChecked == true).Select(check => (string)check.Tag!));
        preview.Text = report.ToJson();
    }

    private async void Save(object? sender, RoutedEventArgs e)
    {
        if (report is null || busy) return;
        DiagnosticReport selected = report;
        SetBusy(true);
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存诊断包", SuggestedFileName = $"hell-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
                DefaultExtension = "zip", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
            });
            if (file is null) return;
            string? path = file.TryGetLocalPath();
            if (path is null) throw new IOException("请选择本机文件夹，以便安全地保存诊断包。");
            await selected.SavePackageAsync(path);
            notice.Text = "诊断包已保存，没有上传任何内容。";
        }
        catch (Exception error) { notice.Text = "保存失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        refresh.IsEnabled = !value;
        save.IsEnabled = !value && report is not null;
        foreach (CheckBox check in sections.Keys) check.IsEnabled = !value;
        fields.IsEnabled = !value;
    }
}
