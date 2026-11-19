using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL.Core.Launcher.Ai.Local;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed class ModelChoice
{
    public string Name { get; set; } = "";
    public bool Local { get; set; }
    public string Endpoint { get; set; } = "";
}

public sealed class ModelPicker : PageBase
{
    private readonly Func<ModelChoice, Task> _choose;
    private readonly TextBox _search = Ui.Input("搜索本地或云端模型");
    private readonly SPanel _local = Ui.V(3);
    private readonly SPanel _cloud = Ui.V(3);
    private readonly TextBlock _error = Ui.Txt("", 12, false, "B.Danger").Wrap();
    private readonly Button _refresh;
    private readonly string _selected;
    private readonly bool _selectedLocal;
    private readonly List<LocalModelInfo> _installed = new List<LocalModelInfo>();
    private readonly List<string> _remote = new List<string>();
    private string _endpoint = "";
    private string _localStatus = "正在检测本地模型…";
    private string _cloudStatus = "正在获取云端模型…";
    private bool _localLoading = true;
    private bool _cloudLoading = true;
    private bool _choosing;
    private bool _refreshing;

    public ModelPicker(string selected, bool selectedLocal, Func<ModelChoice, Task> choose, Action manage, double listHeight = 340)
    {
        _selected = selected;
        _selectedLocal = selectedLocal;
        _choose = choose;
        _refresh = Ui.Btn("刷新", BtnKind.Ghost, (_, _) => _ = RefreshAsync(), "\ue72c");
        var header = Ui.G(null, "*,Auto");
        header.Add(Ui.Txt("选择模型", 16, true).VCenter(), 0);
        header.Add(_refresh, 0, 1);
        var lists = Ui.V(16, _local, _cloud);
        var scroll = Ui.Scroll(lists);
        scroll.MaxHeight = listHeight;
        var manageButton = Ui.Btn("管理模型与连接", BtnKind.Ghost, (_, _) => manage(), "\ue713");
        manageButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _error.Visibility = Visibility.Collapsed;
        Content = Ui.V(12, header, _search, scroll, _error, manageButton).M(16);
        _search.TextChanged += (_, _) => Render();
        Loaded += (_, _) => _search.Focus();
        Render();
    }

    protected override Task LoadAsync() => RefreshAsync();

    public override async Task RefreshAsync()
    {
        if (_refreshing || _choosing) return;
        _refreshing = true;
        _refresh.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed;
        _localLoading = _cloudLoading = true;
        _localStatus = "正在检测本地模型…";
        _cloudStatus = "正在获取云端模型…";
        Render();
        try { await Task.WhenAll(LoadLocal(), LoadCloud()); }
        finally { _refreshing = false; _refresh.IsEnabled = !_choosing; }
    }

    private async Task LoadLocal()
    {
        try
        {
            var status = await Api.CallAsync<LocalModelStatus>("ai_local_model_choices") ?? throw new InvalidOperationException("没有收到本地服务状态。");
            _installed.Clear();
            _endpoint = status.Endpoint;
            if (status.Online) _installed.AddRange(status.Models);
            _localStatus = status.Online ? (_installed.Count == 0 ? "服务已连接，还没有模型。打开模型库下载或导入。" : status.Endpoint)
                : "未连接 Ollama。请在模型与连接中启动或配置本地服务。";
        }
        catch (Exception ex) { _localStatus = "检测失败，可点击刷新重试。"; _local.ToolTip = ex.Message; }
        finally { _localLoading = false; Render(); }
    }

    private async Task LoadCloud()
    {
        try
        {
            var response = await Api.CallAsync<SettingsPage.AiModelList>("ai_cloud_model_choices") ?? throw new InvalidOperationException("没有收到云端模型列表。");
            _remote.Clear();
            _remote.AddRange(response.Models.Where(model => !string.IsNullOrWhiteSpace(model)).Distinct());
            _cloudStatus = response.Ok ? "" : "云端列表暂不可用；可以保留上次模型或手动指定。";
            _cloud.ToolTip = response.Error;
        }
        catch (Exception ex) { _cloudStatus = "云端连接失败；本地模型仍可选择。"; _cloud.ToolTip = ex.Message; }
        finally
        {
            if (!_selectedLocal && _selected.Length > 0 && !_remote.Contains(_selected)) _remote.Insert(0, _selected);
            _cloudLoading = false;
            Render();
        }
    }

    private void Render()
    {
        string query = _search.Text.Trim();
        bool Matches(string name) => query.Length == 0 || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        _local.Children.Clear();
        _cloud.Children.Clear();
        _local.Children.Add(Ui.Txt("本地模型", 12, true, "B.InkMuted").M(4, 0, 0, 4));
        _cloud.Children.Add(Ui.Txt("云端模型", 12, true, "B.InkMuted").M(4, 0, 0, 4));
        if (_localStatus.Length > 0) _local.Children.Add(Ui.Txt(_localStatus, 11, false, "B.InkMuted").Wrap().M(4, 0, 4, 6));
        var localModels = _installed.Where(model => Matches(model.Name)).ToList();
        foreach (var model in localModels.Take(60))
            _local.Children.Add(Choice(new ModelChoice { Name = model.Name, Local = true, Endpoint = _endpoint }, ModelRules.FormatBytes(model.Size) + (model.Running ? " · 已加载" : " · 已安装")));
        if (!_localLoading && localModels.Count == 0 && _installed.Count > 0) _local.Children.Add(Ui.Muted("没有匹配的本地模型"));
        if (_cloudStatus.Length > 0) _cloud.Children.Add(Ui.Txt(_cloudStatus, 11, false, "B.InkMuted").Wrap().M(4, 0, 4, 6));
        foreach (string model in _remote.Where(Matches).Take(60))
            _cloud.Children.Add(Choice(new ModelChoice { Name = model }, "使用已配置的云端连接"));
        if (!_cloudLoading && query.Length > 0 && !_remote.Contains(query))
            _cloud.Children.Add(Choice(new ModelChoice { Name = query }, "手动指定云端模型"));
        if (!_cloudLoading && query.Length == 0 && _remote.Count == 0)
            _cloud.Children.Add(Ui.Muted("输入模型名称手动指定，或在设置中配置云端连接。"));
    }

    private Button Choice(ModelChoice model, string detail)
    {
        bool active = model.Name == _selected && model.Local == _selectedLocal;
        var row = Ui.G(null, "*,Auto");
        row.Add(Ui.V(3, Ui.Txt(model.Name, 14, active).Wrap(), Ui.Txt(detail, 11, false, "B.InkMuted").Wrap()), 0);
        if (active) row.Add(Ui.Glyph("\ue73e", 14, "B.Accent").VCenter(), 0, 1);
        var button = Ui.Btn("", BtnKind.Ghost);
        button.Content = row;
        button.Padding = new Thickness(10, 9, 10, 9);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.IsEnabled = !_choosing;
        AutomationProperties.SetName(button, "使用" + (model.Local ? "本地" : "云端") + "模型 " + model.Name);
        button.Click += async (_, _) =>
        {
            if (_choosing) return;
            _choosing = true;
            _refresh.IsEnabled = false;
            _local.IsEnabled = _cloud.IsEnabled = false;
            _error.Text = "正在切换到 " + model.Name + "…";
            _error.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
            _error.Visibility = Visibility.Visible;
            try { await _choose(model); }
            catch (Exception ex) { _error.Text = ex.Message; _error.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger"); }
            finally { _choosing = false; _refresh.IsEnabled = !_refreshing; _local.IsEnabled = _cloud.IsEnabled = true; Render(); }
        };
        return button;
    }
}
