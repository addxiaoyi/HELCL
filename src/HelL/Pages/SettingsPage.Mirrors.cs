using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class SettingsPage
{
    private readonly TextBlock _sourceSummary = Ui.Small(I18n.L("自动比较官方、BMCLAPI 和两个 MC Mirror 节点，失败时切换。")).Wrap();
    private readonly SPanel _sourceResults = Ui.V(4);

    private FrameworkElement SourcePicker()
    {
        var test = Ui.Btn(I18n.L("检测下载源"), BtnKind.Ghost);
        test.Click += (_, _) => Run(async () =>
        {
            test.IsEnabled = false; _sourceSummary.Text = I18n.L("正在检测可用性与响应时间…"); _sourceResults.Children.Clear();
            try
            {
                var status = await Api.CallAsync("download_source_status", new { refresh = true });
                _sourceSummary.Text = I18n.L(status.GetProperty("message").GetString() ?? "");
                foreach (var source in status.GetProperty("samples").EnumerateArray())
                {
                    bool available = source.GetProperty("available").GetBoolean();
                    string latency = available ? I18n.L("{0} ms", source.GetProperty("latency_ms").ToString()) : I18n.L("暂不可用");
                    _sourceResults.Children.Add(Ui.Small(source.GetProperty("name").GetString() + " · " + latency).Wrap());
                }
            }
            catch (Exception ex) { _sourceSummary.Text = I18n.L("下载源检测失败：{0}", ex.Message); }
            finally { test.IsEnabled = true; }
        });
        var row = Ui.H(8, _src, test); row.Wrap = true;
        return Ui.V(8, row, _sourceSummary, _sourceResults);
    }
}
