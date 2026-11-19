using System;
using System.Collections.Generic;
using System.Linq;
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
        var notice = Ui.Small("选择诊断项目，检查预览后再手动保存。不会自动上传。").Wrap();
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        var sections = new Dictionary<CheckBox, DiagnosticSections>();
        var choices = Ui.V(6);
        var fieldChoices = Ui.V(4);
        DiagnosticReport report = null;
        DiagnosticReport fullReport = null;
        var refresh = Ui.Btn("刷新预览", BtnKind.Chip);
        var save = Ui.Btn("保存 ZIP 诊断包", BtnKind.Primary);
        AddSection("应用版本", DiagnosticSections.Application, true);
        AddSection("系统与 .NET 环境", DiagnosticSections.Environment, true);
        AddSection("可用磁盘空间", DiagnosticSections.Storage, true);
        AddSection("实例数量", DiagnosticSections.Library, true);
        AddSection("启动预检代码", DiagnosticSections.LaunchChecks, false);
        AddSection("下载源状态（会发送无身份探测请求）", DiagnosticSections.Network, false);
        AddSection("最近失败类别（当前会话）", DiagnosticSections.RecentIssues, false);
        var fieldExpander = new Expander { Header = "选择要保留的字段", Content = fieldChoices };
        save.IsEnabled = false;
        refresh.Click += async (_, _) => await Refresh();
        save.Click += async (_, _) =>
        {
            if (report is null) return;
            var picker = new SaveFileDialog { Title = "保存诊断包", Filter = "ZIP archive|*.zip", AddExtension = true,
                DefaultExt = ".zip", FileName = "hell-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip", OverwritePrompt = true };
            if (picker.ShowDialog() != true) return;
            refresh.IsEnabled = save.IsEnabled = false;
            try { await report.SavePackageAsync(picker.FileName); notice.Text = "诊断包已保存，没有上传任何内容。"; }
            catch (Exception error) { notice.Text = "保存失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
            finally { refresh.IsEnabled = save.IsEnabled = true; }
        };
        var body = Ui.V(10, notice, choices, fieldExpander, preview, Ui.H(8, refresh, save));
        Dlg.Panel("诊断包", Ui.Scroll(body), 740);
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
            refresh.IsEnabled = save.IsEnabled = choices.IsEnabled = false;
            notice.Text = "正在收集所选诊断信息…";
            try
            {
                DiagnosticSections selected = sections.Where(entry => entry.Key.IsChecked == true)
                    .Aggregate(DiagnosticSections.None, (flags, entry) => flags | entry.Value);
                DiagnosticIssue[] issues = TaskStore.Rows.Where(task => task.Finished && !task.Success && !task.Cancelled).Reverse().Take(8)
                    .Select(task => new DiagnosticIssue(task.HasCrash ? "game_crashed" : "task_failed",
                        task.HasCrash ? "A game process crashed." : "A launcher task failed.")).ToArray();
                report = fullReport = await Task.Run(() => DiagnosticCapture.Collect(selected, issues));
                fieldChoices.Children.Clear();
                foreach (DiagnosticField field in report.Fields)
                {
                    var check = Ui.Check(field.Key + " — " + field.Value, true);
                    check.Tag = field.Key;
                    check.Click += (_, _) =>
                    {
                        report = fullReport.SelectFields(fieldChoices.Children.OfType<CheckBox>().Where(item => item.IsChecked == true).Select(item => (string)item.Tag));
                        preview.Text = report.ToJson();
                    };
                    fieldChoices.Children.Add(check);
                }
                preview.Text = report.ToJson();
                notice.Text = "预览已更新；可以剔除字段后保存。";
            }
            catch (Exception error) { report = null; preview.Text = ""; notice.Text = "收集失败：" + DiagnosticReportBuilder.SanitizeMessage(error.Message); }
            finally { refresh.IsEnabled = choices.IsEnabled = true; save.IsEnabled = report is not null; }
        }
    }
}
