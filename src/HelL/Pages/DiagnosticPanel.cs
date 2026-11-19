using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Diagnostics;
using HelL.Services;
using Microsoft.Win32;

namespace HelL.Pages;

public static class DiagnosticPanel
{
    public static void Show()
    {
        var notice = Ui.Small("选择诊断项目并检查预览后手动保存，不会自动上传。").Wrap();
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        var sections = new Dictionary<CheckBox, DiagnosticSections>();
        var choices = Ui.V(6);
        var fieldChoices = Ui.V(4);
        DiagnosticReport report = null;
        DiagnosticReport fullReport = null;
        CancellationTokenSource operation = null;
        bool busy = false;
        bool closed = false;
        var refresh = Ui.Btn("刷新预览", BtnKind.Chip);
        var save = Ui.Btn("保存 ZIP 诊断包", BtnKind.Primary);
        var cancel = Ui.Btn("取消当前操作", BtnKind.Chip, (_, _) => operation?.Cancel());
        AddSection("应用版本", DiagnosticSections.Application, true);
        AddSection("系统与 .NET 环境", DiagnosticSections.Environment, true);
        AddSection("可用磁盘空间", DiagnosticSections.Storage, true);
        AddSection("实例数量", DiagnosticSections.Library, true);
        AddSection("启动预检结果", DiagnosticSections.LaunchChecks, false);
        AddSection("下载源状态（会发送匿名探测请求）", DiagnosticSections.Network, false);
        AddSection("最近失败类别（当前会话）", DiagnosticSections.RecentIssues, false);
        var fieldExpander = new Expander { Header = "选择要保留的字段", Content = fieldChoices };
        SetBusy(false);
        refresh.Click += async (_, _) => await Refresh();
        save.Click += async (_, _) =>
        {
            if (report is null || busy || closed) return;
            DiagnosticReport selected = report;
            using var pending = new CancellationTokenSource();
            operation = pending;
            SetBusy(true);
            try
            {
                var picker = new SaveFileDialog { Title = "保存诊断包", Filter = "ZIP archive|*.zip", AddExtension = true,
                    DefaultExt = ".zip", FileName = "hell-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip", OverwritePrompt = true };
                bool accepted = picker.ShowDialog() == true;
                pending.Token.ThrowIfCancellationRequested();
                if (!accepted) { notice.Text = "未选择保存位置，可再次保存。"; return; }
                await selected.SavePackageAsync(picker.FileName, pending.Token);
                pending.Token.ThrowIfCancellationRequested();
                if (!closed) notice.Text = "诊断包已保存，没有上传任何数据。";
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested)
            { if (!closed) notice.Text = "诊断导出已取消，可再次保存。"; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!closed) notice.Text = "保存失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
            finally { Finish(pending); }
        };
        var body = Ui.V(10, notice, choices, fieldExpander, preview, Ui.H(8, refresh, save, cancel));
        Dlg.Panel("诊断包", Ui.Scroll(body), 740, onClose: () => { closed = true; operation?.Cancel(); });
        PageBase.Run(Refresh);

        void AddSection(string label, DiagnosticSections section, bool selected)
        {
            var check = Ui.Check(label, selected);
            sections.Add(check, section);
            choices.Children.Add(check);
            check.Click += async (_, _) => await Refresh();
        }

        async Task Refresh()
        {
            if (busy || closed) return;
            using var pending = new CancellationTokenSource();
            operation = pending;
            SetBusy(true);
            report = fullReport = null;
            preview.Text = "";
            fieldChoices.Children.Clear();
            notice.Text = "正在收集所选诊断信息…";
            try
            {
                DiagnosticSections selected = sections.Where(entry => entry.Key.IsChecked == true)
                    .Aggregate(DiagnosticSections.None, (flags, entry) => flags | entry.Value);
                DiagnosticIssue[] issues = TaskStore.Rows.Where(task => task.Finished && !task.Success && !task.Cancelled).Reverse().Take(8)
                    .Select(task => new DiagnosticIssue(task.HasCrash ? "game_crashed" : "task_failed",
                        task.HasCrash ? "A game process crashed." : "A launcher task failed.")).ToArray();
                DiagnosticReport captured = await Task.Run(() => DiagnosticCapture.Collect(selected, issues, pending.Token), pending.Token);
                pending.Token.ThrowIfCancellationRequested();
                if (closed) return;
                report = fullReport = captured;
                foreach (DiagnosticField field in report.Fields)
                {
                    var check = Ui.Check(field.Key + " · " + field.Value, true);
                    check.Tag = field.Key;
                    check.Click += (_, _) =>
                    {
                        if (busy || closed || fullReport is null) return;
                        report = fullReport.SelectFields(fieldChoices.Children.OfType<CheckBox>().Where(item => item.IsChecked == true).Select(item => (string)item.Tag));
                        preview.Text = report.ToJson();
                    };
                    fieldChoices.Children.Add(check);
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

        void Finish(CancellationTokenSource pending)
        {
            if (ReferenceEquals(operation, pending)) operation = null;
            if (!closed) SetBusy(false);
            else busy = false;
        }

        void SetBusy(bool value)
        {
            busy = value;
            refresh.IsEnabled = choices.IsEnabled = fieldChoices.IsEnabled = !value;
            save.IsEnabled = !value && report is not null;
            cancel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
