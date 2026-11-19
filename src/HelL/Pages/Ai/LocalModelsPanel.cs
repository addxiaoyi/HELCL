using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Core.Launcher.Ai.Local;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed partial class LocalModelsPanel : PageBase
{
    private readonly Func<Task> _selected;
    private readonly TextBlock _viewTitle = Ui.Txt("已安装", 20, true);
    private readonly TextBlock _hardware = Ui.Txt("正在检测这台电脑…", 13, false, "B.InkMuted").Wrap();
    private readonly TextBlock _runtime = Ui.Txt("正在寻找本地服务…", 13).Wrap();
    private readonly TextBlock _feedback = Ui.Txt("", 12, false, "B.InkMuted").Wrap();
    private readonly TextBlock _jobText = Ui.Txt("", 12, false, "B.InkMuted").Wrap();
    private readonly ProgressBar _progress = new ProgressBar { Height = 3, Minimum = 0, Maximum = 100 };
    private readonly SPanel _rows = Ui.V(0);
    private readonly SmoothScroll _listScroll;
    private readonly ContentControl _filters = new ContentControl();
    private readonly Button _runtimeAction;
    private readonly Button _stopRuntime;
    private readonly Button _connectDetected;
    private readonly Button _cancel;
    private readonly Button _retry;
    private readonly TextBox _endpoint = Ui.Input("http://127.0.0.1:11434");
    private readonly ComboBox _source = Ui.Combo(new[] { "Hugging Face 官方", "HF Mirror 镜像", "自定义镜像" });
    private readonly TextBox _mirror = Ui.Input("https://hf-mirror.com");
    private readonly ComboBox _settingsSource = Ui.Combo(new[] { "Hugging Face 官方", "HF Mirror 镜像", "自定义镜像" });
    private readonly TextBox _settingsMirror = Ui.Input("https://hf-mirror.com");
    private readonly TextBox _query = Ui.Input("搜索模型，或输入 作者/仓库");
    private readonly FrameworkElement _hubFilters;
    private readonly FrameworkElement _settingsView;
    private readonly CheckBox _toolMode = Ui.Check("允许当前本地模型调用启动器工具");
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Button> _tabs = new List<Button>();
    private LocalModelStatus _state = new LocalModelStatus();
    private string _tab = "installed";
    private bool _loading;
    private bool _polling;
    private bool _jobRunning;
    private string _lastJob = "";
    private bool _refreshPending;
    private int _feedbackRevision;
    private string _filesSource = "";

    public LocalModelsPanel(Action back, Func<Task> selected)
    {
        _selected = selected;
        var heading = Ui.G(null, "*,Auto");
        heading.Add(Ui.Txt("模型与连接", 16, true).VCenter(), 0);
        heading.Add(Ui.Btn("返回对话", BtnKind.Ghost, (_, _) => back(), "\ue72b").VCenter(), 0, 1);
        var header = new Border { Height = 64, Padding = new Thickness(22, 0, 16, 0), BorderThickness = new Thickness(0, 0, 0, 1), Child = heading };
        header.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
        header.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
        var tabs = Ui.V(4);
        tabs.Children.Add(Ui.Txt("模型", 11, false, "B.InkMuted").M(10, 4, 0, 8));
        foreach (var item in new[] { ("installed", "已安装"), ("catalog", "推荐下载"), ("hub", "上游仓库"), ("queue", "下载队列"), ("storage", "存储"), ("settings", "设置") })
        {
            string key = item.Item1;
            if (key == "queue") tabs.Children.Add(Ui.Txt("管理", 11, false, "B.InkMuted").M(10, 20, 0, 8));
            var button = Ui.Btn(item.Item2, BtnKind.Ghost, (_, _) => RunUi(() => SelectTab(key)));
            button.Tag = key;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 9, 10, 9);
            _tabs.Add(button);
            tabs.Children.Add(button);
        }
        var navigation = new Border { Padding = new Thickness(10, 16, 10, 12), BorderThickness = new Thickness(0, 0, 1, 0), Child = Ui.Scroll(tabs) };
        navigation.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
        navigation.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
        _runtimeAction = Ui.Btn("启动服务", BtnKind.Chip, (_, _) => RunUi(RuntimeAction));
        _stopRuntime = Ui.Btn("停止服务", BtnKind.Ghost, (_, _) => RunUi(StopRuntime));
        _connectDetected = Ui.Btn("连接发现的服务", BtnKind.Chip, (_, _) => RunUi(ConnectDetected))
            .M(0, 0, 0, 0);
        _connectDetected.Visibility = Visibility.Collapsed;
        var runtimeActions = Ui.H(4, _runtimeAction, _stopRuntime, Ui.Btn("检测", BtnKind.Ghost, (_, _) => RunUi(RefreshRuntime), "\ue72c"));
        runtimeActions.Wrap = true;
        var runtimeRow = Ui.G(null, "*,Auto");
        runtimeRow.Add(_runtime.VCenter().M(0, 0, 12, 0), 0);
        runtimeRow.Add(runtimeActions.VCenter(), 0, 1);
        _hardware.FontSize = 11;
        var top = Ui.V(10, _viewTitle, runtimeRow, _hardware, _connectDetected.Left(), _filters, _feedback);
        _feedback.Visibility = Visibility.Collapsed;
        _cancel = Ui.Btn("取消", BtnKind.Ghost, (_, _) => RunUi(CancelJob));
        _retry = Ui.Btn("重试", BtnKind.Chip, (_, _) => RunUi(RetryJob));
        var jobLine = Ui.G(null, "*,Auto,Auto");
        jobLine.Add(_jobText, 0);
        jobLine.Add(_cancel, 0, 1);
        jobLine.Add(_retry, 0, 2);
        var body = Ui.G("Auto,*,Auto");
        body.Add(top, 0);
        _listScroll = Ui.Scroll(_rows);
        body.Add(_listScroll.M(0, 12, 0, 12), 1);
        body.Add(Ui.V(8, _progress, jobLine), 2);
        body.Margin = new Thickness(24, 22, 24, 16);
        body.MaxWidth = 1040;
        var workspace = Ui.G(null, "136,*");
        workspace.Add(navigation, 0);
        workspace.Add(body, 0, 1);
        var root = Ui.G("Auto,*");
        root.Add(header, 0);
        root.Add(workspace, 1);
        Content = root;
        _timer.Tick += async (_, _) => await PollJob();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        _source.SelectionChanged += (_, _) => _mirror.Visibility = _source.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _settingsSource.SelectionChanged += (_, _) => _settingsMirror.Visibility = _settingsSource.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _source.SelectedIndex = 0;
        _mirror.Visibility = Visibility.Collapsed;
        _query.Text = "Qwen3 GGUF";
        var search = Ui.G(null, "*,Auto");
        search.Add(_query, 0);
        search.Add(Ui.Btn("搜索", BtnKind.Chip, (_, _) => RunUi(SearchHub), "\ue721").M(8, 0, 0, 0), 0, 1);
        _source.Width = double.NaN;
        var sourceSearch = Ui.G(null, "Auto,*");
        _source.Width = 145;
        sourceSearch.Add(_source.M(0, 0, 8, 0), 0);
        sourceSearch.Add(search, 0, 1);
        _hubFilters = Ui.V(8, sourceSearch, _mirror);
        _settingsView = BuildSettings();
        _endpoint.Text = ModelRules.DefaultEndpoint;
        RenderJob(new ModelJob());
    }

    protected override async Task LoadAsync()
    {
        await Execute(async () =>
        {
            var settings = await Api.CallAsync<Dictionary<string, JsonElement>>("get_settings") ?? new Dictionary<string, JsonElement>();
            _toolMode.IsChecked = settings.TryGetValue("ai_local_tools", out var tools) && tools.ValueKind == JsonValueKind.True;
            if (settings.TryGetValue("ai_local_url", out var localUrl) && localUrl.ValueKind == JsonValueKind.String)
            {
                string configuredEndpoint = localUrl.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(configuredEndpoint))
                    _endpoint.Text = ModelRules.Endpoint(configuredEndpoint);
            }
            _endpoint.Tag = _endpoint.Text;
            if (settings.TryGetValue("ai_model_source", out var source))
            {
                string url = source.GetString() ?? "https://huggingface.co";
                _source.SelectedIndex = url == "https://huggingface.co" ? 0 : url == "https://hf-mirror.com" ? 1 : 2;
                _mirror.Text = url;
                _settingsSource.SelectedIndex = _source.SelectedIndex;
                _settingsMirror.Text = url;
            }
            await RefreshRuntime();
        });
    }

    private void RunUi(Func<Task> work) => _ = Execute(work);
    private async Task Execute(Func<Task> work)
    {
        if (_loading) { Feedback("已有操作正在进行，请稍后再试。"); return; }
        _loading = true;
        Feedback("正在处理…");
        int progressRevision = _feedbackRevision;
        try { await work(); if (_feedbackRevision == progressRevision) Feedback(""); }
        catch (Exception ex) { Feedback(ex.Message, true); }
        finally { _loading = false; }
    }

    private void Feedback(string text, bool error = false)
    {
        _feedbackRevision++;
        _feedback.Text = text;
        _feedback.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _feedback.SetResourceReference(TextBlock.ForegroundProperty, error ? "B.Danger" : "B.InkMuted");
    }

    private Task RefreshRuntime() => RefreshRuntime(true);

    private async Task RefreshRuntime(bool detect)
    {
        _state = await Api.CallAsync<LocalModelStatus>("ai_local_status", new { detect }) ?? throw new InvalidOperationException("未收到本地服务状态，请重试。");
        _connectDetected.Visibility = _state.DiscoveredEndpoint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _connectDetected.Content = "连接发现的服务 · " + _state.DiscoveredEndpoint;
        var hardware = _state.Hardware;
        _hardware.Text = "可用内存 " + ModelRules.FormatBytes(hardware.AvailableMemory) + " · 下载盘剩余 " + ModelRules.FormatBytes(hardware.FreeDisk);
        _hardware.ToolTip = hardware.Gpu + "\n" + hardware.Note + "\n" + hardware.DiskPath;
        _runtime.Text = _state.Online ? "Ollama " + _state.Version + " · 已连接本机" : "本地服务未连接";
        _runtime.ToolTip = _state.Online ? _state.Endpoint : _state.Error;
        _runtimeAction.Content = _state.Online ? "服务已就绪" : _state.RuntimePath.Length > 0 ? "启动服务" : "安装运行环境";
        _runtimeAction.Visibility = _state.Online ? Visibility.Collapsed : Visibility.Visible;
        _runtimeAction.IsEnabled = !_state.Online && !_jobRunning;
        _stopRuntime.Visibility = _state.Managed ? Visibility.Visible : Visibility.Collapsed;
        RenderJob(_state.Job);
        await SelectTab(_tab);
    }

    private async Task SaveEndpoint()
    {
        string endpoint = ModelRules.Endpoint(_endpoint.Text);
        await Api.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ai_local_url"] = endpoint } });
        _endpoint.Tag = endpoint;
        Feedback("本地地址已保存，正在检测连接…");
        await RefreshRuntime(false);
        Feedback(_state.Online ? "本地地址已保存，已连接 " + endpoint : "地址已保存，但服务未连接。请确认 Ollama 已启动，再点击检测。", !_state.Online);
    }

    private async Task ConnectDetected()
    {
        if (_state.DiscoveredEndpoint.Length == 0) return;
        _endpoint.Text = ModelRules.Endpoint(_state.DiscoveredEndpoint);
        await SaveEndpoint();
    }

    private void OpenConnection() => RunUi(() => SelectTab("settings"));

    private async Task RuntimeAction()
    {
        if (_state.RuntimePath.Length > 0)
        {
            await BeginJob(() => Api.CallAsync("ai_local_start"));
            return;
        }
        var package = await Api.CallAsync<RuntimePackage>("ai_local_package") ?? throw new InvalidOperationException("无法查询运行环境安装包。");
        int choice = await Dlg.Choose("安装 Ollama " + package.Version,
            Ui.Muted("从 Ollama 官方 GitHub 下载约 " + ModelRules.FormatBytes(package.Size) + "。校验后安装到启动器的 ai-local 目录，不需要管理员权限。解压还需要额外空间。"), new[] { "取消", "下载并安装" });
        if (choice == 1) await BeginJob(() => Api.CallAsync("ai_local_install"));
    }

    private async Task StopRuntime() { await Api.CallAsync("ai_local_stop"); await RefreshRuntime(); }

    private async Task SelectTab(string tab)
    {
        bool changed = _tab != tab;
        _tab = tab;
        foreach (var button in _tabs)
        {
            bool active = (string)button.Tag == tab;
            button.SetResourceReference(Control.BackgroundProperty, active ? "B.Hover" : "B.Canvas");
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            if (active) _viewTitle.Text = button.Content as string ?? System.Windows.Automation.AutomationProperties.GetName(button);
        }
        _hardware.Visibility = tab is "catalog" or "storage" or "settings" ? Visibility.Visible : Visibility.Collapsed;
        _filters.Content = null;
        _rows.Children.Clear();
        if (tab == "installed") RenderInstalled();
        else if (tab == "catalog") await RenderCatalog();
        else if (tab == "hub") RenderHub();
        else if (tab == "queue") await RenderQueue();
        else if (tab == "storage") await RenderStorage();
        else RenderSettings();
        _source.Visibility = tab == "hub" ? Visibility.Visible : Visibility.Collapsed;
        _mirror.Visibility = (_source.SelectedIndex == 2 && tab == "hub") ? Visibility.Visible : Visibility.Collapsed;
        _settingsSource.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _settingsMirror.Visibility = (_settingsSource.SelectedIndex == 2 && tab == "settings") ? Visibility.Visible : Visibility.Collapsed;
        if (changed) _listScroll.ScrollToTop();
    }

    private FrameworkElement BuildSettings()
    {
        _settingsSource.Width = double.NaN;
        return Ui.V(10,
            Ui.Txt("本地运行环境", 18, true),
            Ui.Muted("设置本机 Ollama 服务地址。保存后会自动检测连接；通常使用默认地址，无需填写云端 API Key。"),
            _endpoint,
            Ui.Btn("保存并检测", BtnKind.Chip, (_, _) => RunUi(SaveEndpoint)).Left(),
            Ui.Txt("模型来源", 14, true),
            _settingsSource,
            _settingsMirror,
            Ui.Btn("保存模型来源", BtnKind.Ghost, (_, _) => RunUi(SaveModelSource)).Left(),
            Ui.Txt("本地工具助手", 14, true), _toolMode,
            Ui.Muted("需要模型声明工具能力。启用后使用较大的上下文，操作仍经过确认卡；模型切换后默认关闭。"),
            Ui.Btn("保存工具模式", BtnKind.Chip, (_, _) => RunUi(SaveToolMode)).Left(),
            Ui.Muted("支持完整的分片 GGUF；私有及受限仓库暂不支持。"))
            .M(0, 16, 0, 16);
    }

    private void RenderSettings()
    {
        _filters.Content = null;
        _rows.Children.Add(_settingsView);
        _settingsSource.Visibility = Visibility.Visible;
        _settingsMirror.Visibility = _settingsSource.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task SaveModelSource()
    {
        string source = SettingsSourceUrl();
        await Api.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ai_model_source"] = source } });
        _source.SelectedIndex = _settingsSource.SelectedIndex;
        _mirror.Text = source;
        Feedback("模型来源已保存：" + source, false);
    }

    private void RenderInstalled()
    {
        _filters.Content = Ui.H(6, Ui.Btn("扫描 GGUF 文件夹", BtnKind.Ghost, (_, _) => RunUi(ScanFolder), "\ue8b7"));
        if (_state.Models.Count == 0)
            _rows.Children.Add(Ui.V(8, Ui.Txt("还没有本地模型", 18, true), Ui.Muted("连接 Ollama 后会自动发现已安装模型。也可以前往推荐下载，或扫描已有 GGUF 文件夹。"),
                Ui.Btn("浏览推荐模型", BtnKind.Chip, (_, _) => RunUi(() => SelectTab("catalog"))).Left()).M(0, 24, 0, 24));
        foreach (var model in _state.Models)
        {
            var use = Ui.Btn("使用", BtnKind.Chip, (_, _) => RunUi(() => UseModel(model.Name)));
            var load = Ui.Btn(model.Running ? "释放内存" : "加载", BtnKind.Ghost, (_, _) => RunUi(() => BeginJob(() => Api.CallAsync("ai_local_load", new { model = model.Name, unload = model.Running }))));
            var delete = Ui.Btn("删除", BtnKind.Ghost, (_, _) => RunUi(() => DeleteModel(model)));
            _rows.Children.Add(Row(model.Name, string.Join(" · ", new[] { ModelRules.FormatBytes(model.Size), model.Parameters, model.Quantization, model.Running ? "正在内存中运行" : "已安装" }.Where(s => s.Length > 0)), Ui.H(5, load, use, delete)));
        }
    }

    private async Task RenderCatalog()
    {
        _rows.Children.Add(Ui.Muted("下载量为约数。内存估算包含基础上下文开销，实际占用随模型和对话长度变化。").M(0, 0, 0, 10));
        var entries = await Api.CallAsync<List<LibraryModel>>("ai_local_catalog") ?? throw new InvalidOperationException("无法读取模型推荐。");
        foreach (var model in entries)
        {
            var download = Ui.Btn("下载", BtnKind.Chip, (_, _) => RunUi(() => ConfirmPull(model)));
            download.IsEnabled = ModelRules.CanDownload(_state.Online, true, model.Fit);
            string detail = model.Parameters + " · 约 " + ModelRules.FormatBytes(model.Size) + " · " + model.Fit.Label + "\n" + model.Description;
            var row = Row(model.Name, detail, download);
            row.ToolTip = model.Fit.Reason;
            _rows.Children.Add(row);
        }
        var custom = Ui.Input("其他 Ollama 模型标签，例如 gemma3:4b");
        var customRow = Ui.G(null, "*,Auto");
        customRow.Add(custom, 0);
        customRow.Add(Ui.Btn("下载标签", BtnKind.Ghost, (_, _) => RunUi(() => ConfirmPull(new LibraryModel { Name = custom.Text, Description = "自定义模型标签：大小由上游返回。" }))).M(8, 0, 0, 0), 0, 1);
        _rows.Children.Add(customRow.M(0, 18, 0, 8));
    }

    private async Task ConfirmPull(LibraryModel model)
    {
        RequireOnline();
        ModelRules.ModelName(model.Name);
        string estimate = model.Size > 0 ? "下载约 " + ModelRules.FormatBytes(model.Size) + "。" : "上游返回大小后显示下载进度。";
        int choice = await Dlg.Choose("下载 " + model.Name, Ui.Muted(estimate + "\n" + model.Fit.Reason + "\n下载来源：Ollama 模型库。下载完成后点击“使用”开始本地聊天。"), new[] { "取消", "下载模型" });
        if (choice == 1) await BeginJob(() => Api.CallAsync("ai_local_pull", new { model = model.Name }));
    }

    private void RenderHub()
    {
        _filters.Content = _hubFilters;
        _rows.Children.Add(Ui.Muted("搜索公开 GGUF 模型，也可输入完整仓库名称。镜像会收到模型搜索与下载请求，不会收到对话内容或云端 API Key。完整分片会合并为一项下载；私有及受限仓库暂不支持。").M(0, 8, 0, 8));
    }

    private string SettingsSourceUrl() => ModelRepository.ValidateSource(_settingsSource.SelectedIndex == 0 ? "https://huggingface.co" : _settingsSource.SelectedIndex == 1 ? "https://hf-mirror.com" : _settingsMirror.Text);
    private string SourceUrl() => ModelRepository.ValidateSource(_source.SelectedIndex == 0 ? "https://huggingface.co" : _source.SelectedIndex == 1 ? "https://hf-mirror.com" : _mirror.Text);

    private async Task SearchHub()
    {
        string source = SourceUrl();
        await Api.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ai_model_source"] = source } });
        _settingsSource.SelectedIndex = _source.SelectedIndex;
        _settingsMirror.Text = source;
        string query = _query.Text.Trim();
        if (query.Contains("/") && !query.Contains(" ")) { await ShowFiles(query); return; }
        var repositories = await Api.CallAsync<List<string>>("ai_local_search", new { source, query }) ?? throw new InvalidOperationException("仓库没有返回搜索结果。");
        _rows.Children.Clear();
        if (repositories.Count == 0) _rows.Children.Add(Ui.Muted("没有找到 GGUF 模型。试试更短的名称，或直接输入 作者/仓库。"));
        foreach (string repository in repositories)
            _rows.Children.Add(Row(repository, "公开 GGUF 仓库", Ui.Btn("查看文件", BtnKind.Ghost, (_, _) => RunUi(() => ShowFiles(repository)))));
    }

    private async Task ShowFiles(string repository)
    {
        string source = SourceUrl();
        var files = await Api.CallAsync<List<ModelFile>>("ai_local_files", new { source, repository }) ?? throw new InvalidOperationException("无法读取仓库文件。");
        _filesSource = source;
        _rows.Children.Clear();
        _rows.Children.Add(Ui.Txt(repository, 15, true).Wrap().M(0, 8, 0, 12));
        if (files.Count == 0) _rows.Children.Add(Ui.Muted("该仓库没有 GGUF 文件。请选择 GGUF 量化仓库。"));
        foreach (var file in files)
        {
            var button = Ui.Btn("下载并导入", BtnKind.Chip, (_, _) => RunUi(() => DownloadFile(file)));
            button.IsEnabled = ModelRules.CanDownload(_state.Online, file.Supported, file.Fit);
            _rows.Children.Add(Row(file.Name, ModelRules.FormatBytes(file.Size) + " · " + file.Fit.Label + "\n" + file.Note, button));
        }
    }

    private async Task DownloadFile(ModelFile file)
    {
        RequireOnline();
        string name = await Dlg.Prompt("本地模型名称", "下载 " + file.Name + "（" + ModelRules.FormatBytes(file.Size) + "）\n" + file.Fit.Reason, SuggestName(file.Name)) ?? "";
        if (name.Length == 0) return;
        ModelRules.ModelName(name);
        string source = _filesSource;
        await BeginJob(() => Api.CallAsync("ai_local_download", new { source, repository = file.Repository, file = file.Name, model = name }));
    }

    private async Task ScanFolder()
    {
        string? folder = Dlg.PickFolder("选择 GGUF 模型文件夹");
        if (folder == null) return;
        var files = await Api.CallAsync<List<LocalModelInfo>>("ai_local_scan", new { folder }) ?? throw new InvalidOperationException("无法扫描模型文件夹。");
        _rows.Children.Clear();
        _rows.Children.Add(Ui.Muted("扫描至三层子目录，最多显示 300 个文件；导入会复制模型到 Ollama 存储。").M(0, 8, 0, 12));
        if (files.Count == 0) _rows.Children.Add(Ui.Muted("这个文件夹中没有发现 GGUF 模型。"));
        foreach (var file in files)
            _rows.Children.Add(Row(file.Name, ModelRules.FormatBytes(file.Size) + " · " + file.Path, Ui.Btn("导入", BtnKind.Chip, (_, _) => RunUi(() => ImportFile(file)))));
    }

    private async Task ImportFile(LocalModelInfo file)
    {
        RequireOnline();
        string name = await Dlg.Prompt("导入模型", "为这个 GGUF 文件设置本地名称", SuggestName(file.Name)) ?? "";
        if (name.Length == 0) return;
        ModelRules.ModelName(name);
        await BeginJob(() => Api.CallAsync("ai_local_import", new { path = file.Path, model = name }));
    }

    private static string SuggestName(string file) => "local/" + System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), "[^a-z0-9._-]", "-");
    private void RequireOnline() { if (!_state.Online) throw new InvalidOperationException("请先安装并启动上方的本地运行环境，再下载或导入模型。"); }
    private async Task UseModel(string model) { await Api.CallAsync("ai_local_select", new { model }); _toolMode.IsChecked = false; await _selected(); }

    private async Task BeginJob(Func<Task<JsonElement>> work)
    {
        var json = await work();
        var job = JsonSerializer.Deserialize<ModelJob>(json.GetRawText(), BridgeClient.JsonOpt) ?? throw new InvalidOperationException("任务没有创建成功。");
        RenderJob(job);
    }
    private async Task CancelJob() { RenderJob(await Api.CallAsync<ModelJob>("ai_local_cancel") ?? new ModelJob()); }
    private async Task RetryJob()
    {
        var job = await Api.CallAsync<ModelJob>("ai_local_job") ?? throw new InvalidOperationException("找不到上次任务。");
        RenderJob(await Api.CallAsync<ModelJob>("ai_local_retry", new { id = job.Id }) ?? new ModelJob());
    }

    private async Task PollJob()
    {
        if (_polling || !IsVisible) return;
        _polling = true;
        try
        {
            var job = await Api.CallAsync<ModelJob>("ai_local_job") ?? throw new InvalidOperationException("无法读取下载进度。");
            string stamp = job.Id + ":" + job.State;
            _refreshPending |= stamp != _lastJob && (job.State == "completed" || job.State == "failed" || job.State == "cancelled");
            _lastJob = stamp;
            RenderJob(job);
            if (_tab == "queue" && !_loading && !_rows.IsKeyboardFocusWithin && !_rows.IsMouseOver) await RenderQueue();
            if (_refreshPending && !_loading)
            {
                _refreshPending = false;
                await Execute(() => RefreshRuntime(false));
            }
        }
        catch (Exception ex) { Feedback("进度连接中断：" + ex.Message, true); }
        finally { _polling = false; }
    }

    private void RenderJob(ModelJob job)
    {
        _jobRunning = ModelQueue.IsPending(job.State);
        _progress.Visibility = _jobRunning ? Visibility.Visible : Visibility.Collapsed;
        _progress.IsIndeterminate = job.Total <= 0;
        _progress.Value = job.Total > 0 ? Math.Max(0, Math.Min(100, 100d * job.Completed / job.Total)) : 0;
        _jobText.Text = job.State == "idle" ? "模型留在本机 · 下载完成后可离线对话" : job.Title + " · " + job.Message + (job.Total > 0 ? "  " + ModelRules.FormatBytes(job.Completed) + " / " + ModelRules.FormatBytes(job.Total) : "");
        _cancel.Visibility = _jobRunning ? Visibility.Visible : Visibility.Collapsed;
        _cancel.IsEnabled = job.State != "cancelling";
        _retry.Visibility = job.State is "failed" or "cancelled" ? Visibility.Visible : Visibility.Collapsed;
        _runtimeAction.IsEnabled = !_state.Online && (!_jobRunning || job.State == "waiting");
        _stopRuntime.IsEnabled = !_jobRunning || job.State == "waiting";
        _rows.IsEnabled = true;
    }

    private static FrameworkElement Row(string title, string detail, FrameworkElement action)
    {
        var grid = Ui.G(null, "*,Auto");
        grid.Add(Ui.V(5, Ui.Txt(title, 14, true).Wrap(), Ui.Txt(detail, 12, false, "B.InkMuted").Wrap()).M(0, 0, 16, 0), 0);
        grid.Add(action.VCenter(), 0, 1);
        var border = new Border { Padding = new Thickness(0, 16, 0, 16), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        border.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
        return border;
    }
}
