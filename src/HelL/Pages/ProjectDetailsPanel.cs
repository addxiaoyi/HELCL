using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class ProjectDetailsPanel : PageBase
{
    private readonly CatalogItem _requested;
    private CatalogItem? _detail;
    private readonly string _preferredGame;
    private readonly SPanel _body = Ui.V(14);
    private readonly SPanel _dependencies = Ui.V(7);
    private readonly TextBlock _status = Ui.Muted("").Wrap();
    private readonly ComboBox _game = Ui.Combo();
    private readonly ComboBox _loader = Ui.Combo();
    private ResourceSummary? _summary;
    private ResourceTitle? _title;
    private ResourceDocumentPanel? _changelog;
    private readonly Action<CatalogItem>? _nameChanged;
    private bool _translating;
    private bool _sync;
    private bool _closed;
    private int _compatibilityGeneration;
    public bool CompatibilityLoading { get; private set; }

    public ProjectDetailsPanel(CatalogItem requested, string preferredGame, Action<CatalogItem>? nameChanged = null)
    {
        _requested = requested; _preferredGame = preferredGame; _nameChanged = nameChanged;
        Content = Ui.V(10, _status, _body);
        _game.Width = _loader.Width = double.NaN;
        _game.SelectionChanged += (_, _) => { if (!_sync) _ = LoadCompatibility(); };
        _loader.SelectionChanged += (_, _) => { if (!_sync) _ = LoadCompatibility(); };
        Unloaded += (_, _) => { _closed = true; ++_compatibilityGeneration; };
    }

    protected override async Task LoadAsync()
    {
        _status.Text = I18n.L("正在加载项目资料…");
        try
        {
            _detail = await Api.CallAsync<CatalogItem>("catalog_details", new { source = _requested.Source, slug = _requested.Slug ?? "", id = _requested.IdValue?.ToString() ?? "" })
                ?? throw new InvalidOperationException(I18n.L("没有收到项目资料。"));
            if (_closed) return;
            _status.Text = "";
            Render();
            await LoadCompatibility();
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _status.Text = I18n.L("项目资料加载失败：{0}", ex.Message);
            _body.Children.Clear(); _body.Children.Add(Ui.Btn(I18n.L("重试"), BtnKind.Chip, (_, _) => Run(LoadAsync)));
        }
    }

    private void Render()
    {
        var detail = _detail!;
        _body.Children.Clear();
        var images = detail.Gallery.Count > 0 ? detail.Gallery : detail.CoverUrl.Length > 0 ? new List<CatalogImage> { new CatalogImage { Url = detail.CoverUrl } } : new List<CatalogImage>();
        _body.Children.Add(new ProjectGallery(images, detail.Name));
        var icon = new CatalogArtwork(detail, 48);
        var heading = Ui.G(null, "Auto,*"); heading.Add(icon, 0);
        _title = new ResourceTitle(detail, 21);
        heading.Add(Ui.V(4, _title, Ui.Muted(I18n.L("作者：{0}", string.IsNullOrWhiteSpace(detail.Author) || detail.Author == "?" ? I18n.L("暂未获取") : detail.Author)).Wrap()).M(12, 0, 0, 0), 0, 1);
        _body.Children.Add(heading);
        _body.Children.Add(Ui.Small(DisplayLabels.Source(detail.Source) + " · " + DisplayLabels.Count(detail.Downloads) + I18n.L(" 次下载") + " · " + detail.Updated));
        _summary = new ResourceSummary(detail, () => Run(Translate), compact: false);
        _body.Children.Add(_summary);
        _body.Children.Add(Ui.Btn(I18n.L("修改中文名与别名"), BtnKind.Ghost, (_, _) => Run(EditName)).Left());
        _sync = true;
        _game.Items.Clear(); _game.Items.Add(I18n.L("全部游戏版本"));
        foreach (string game in detail.GameVersions.AsEnumerable().Reverse().Distinct()) _game.Items.Add(game);
        if (_preferredGame.Length > 0 && !_game.Items.Contains(_preferredGame)) _game.Items.Add(_preferredGame);
        _game.SelectedItem = _preferredGame.Length > 0 ? _preferredGame : _game.Items[0];
        _loader.Items.Clear(); _loader.Items.Add(I18n.L("全部加载器"));
        foreach (string loader in detail.Loaders.Distinct()) _loader.Items.Add(loader);
        _loader.SelectedIndex = 0;
        _sync = false;
        var filters = Ui.G(null, "*,12,*"); filters.Add(Ui.V(4, Ui.Small(I18n.L("游戏版本")), _game), 0); filters.Add(Ui.V(4, Ui.Small(I18n.L("加载器")), _loader), 0, 2);
        _body.Children.Add(Ui.Inset(Ui.V(12, Ui.Txt(I18n.L("兼容性与依赖"), 15, true), filters, _dependencies,
            Ui.Small(I18n.L("以下依赖属于筛选条件匹配的最新文件，不代表所有版本。")).Wrap())));
        _body.Children.Add(new ResourceDocumentPanel(detail, "description"));
        _changelog = new ResourceDocumentPanel(detail, "changelog", () => (_game.SelectedIndex <= 0 ? "" : _game.SelectedItem as string ?? "",
            _loader.SelectedIndex <= 0 ? "" : _loader.SelectedItem as string ?? ""));
        _body.Children.Add(_changelog);
        if (Uri.TryCreate(detail.ProjectUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host == "modrinth.com" || uri.Host == "www.curseforge.com"))
            _body.Children.Add(Ui.Btn(I18n.L("打开项目主页"), BtnKind.Ghost, (_, _) => Ui.OpenUrl(detail.ProjectUrl), "\ue8a7").Left());
    }

    private async Task EditName()
    {
        if (_detail == null) return;
        var updated = await ResourceNameEditor.Show(_detail);
        if (updated == null || _closed) return;
        _detail = updated; _title?.Apply(updated); _summary?.Apply(updated); _nameChanged?.Invoke(updated);
    }

    private async Task Translate()
    {
        if (_translating || _detail == null || _summary == null) return;
        _translating = true; _summary.Begin();
        try
        {
            var response = await Api.CallAsync<SummaryResponse>("catalog_summaries", new { projects = new[] { _detail } });
            if (_closed) return;
            _detail = response?.Rows.FirstOrDefault() ?? _detail;
            _summary.Apply(_detail, response?.Error ?? I18n.L("中文摘要暂不可用。"));
            _title?.Apply(_detail);
        }
        catch (Exception ex) { if (!_closed) _summary.Apply(_detail, ex.Message); }
        finally { _translating = false; }
    }

    private async Task LoadCompatibility()
    {
        if (_detail == null || _closed) return;
        _changelog?.ResetFilters();
        int generation = ++_compatibilityGeneration;
        CompatibilityLoading = true;
        _dependencies.Children.Clear(); _dependencies.Children.Add(Ui.Muted(I18n.L("正在查询适配文件与依赖…")));
        try
        {
            var result = await Api.CallAsync<CatalogCompatibility>("catalog_compatibility", new { source = _detail.Source, slug = _detail.Slug ?? "", id = _detail.IdValue?.ToString() ?? "", game_version = _game.SelectedIndex <= 0 ? "" : _game.SelectedItem as string, loader = _loader.SelectedIndex <= 0 ? "" : _loader.SelectedItem as string })
                ?? throw new InvalidOperationException(I18n.L("兼容性信息不可用。"));
            if (generation != _compatibilityGeneration || _closed) return;
            _dependencies.Children.Clear();
            if (result.Error.Length > 0) { _dependencies.Children.Add(Ui.Muted(result.Error).Wrap()); return; }
            _dependencies.Children.Add(Ui.Txt(I18n.L("适配文件：{0}", result.Version), 13, true).Wrap());
            _dependencies.Children.Add(Ui.Small(string.Join(" · ", result.Loaders) + " · " + string.Join(", ", result.GameVersions)).Wrap());
            if (result.Dependencies.Count == 0) _dependencies.Children.Add(Ui.Muted(I18n.L("上游未为此文件声明额外依赖。")));
            foreach (var dependency in result.Dependencies)
            {
                var row = Ui.G(null, "*,Auto");
                row.Add(Ui.Txt(dependency.DisplayName + (dependency.Version.Length > 0 ? " · " + dependency.Version : ""), 12.5).Wrap(), 0);
                var actions = Ui.H(6, Ui.Tag(I18n.L(ProjectCompatibility.DependencyKind(dependency.Kind))).VCenter()); actions.Wrap = true;
                if (dependency.ProjectId.Length > 0)
                    actions.Children.Add(Ui.Btn(I18n.L("详情"), BtnKind.Ghost, (_, _) => Run(() => OpenDependency(dependency))));
                if (dependency.ProjectType == "mod" && dependency.ProjectId.Length > 0 && dependency.Kind is "required" or "optional")
                    actions.Children.Add(Ui.Btn(I18n.L("安装"), BtnKind.Ghost, (sender, _) => RunOnce((Button)sender, () => DependencyInstallDialog.Show(dependency))));
                row.Add(actions.M(10, 0, 0, 0), 0, 1);
                _dependencies.Children.Add(row);
            }
            if (result.DependencyCount > result.Dependencies.Count)
                _dependencies.Children.Add(Ui.Muted(I18n.L("显示前 {0} 项，共 {1} 项；完整列表请查看项目主页。", result.Dependencies.Count, result.DependencyCount)).Wrap());
        }
        catch (Exception ex)
        {
            if (generation != _compatibilityGeneration || _closed) return;
            _dependencies.Children.Clear(); _dependencies.Children.Add(Ui.Muted(I18n.L("依赖查询失败：{0}", ex.Message)).Wrap());
            _dependencies.Children.Add(Ui.Btn(I18n.L("重试"), BtnKind.Ghost, (_, _) => Run(LoadCompatibility)).Left());
        }
        finally { if (generation == _compatibilityGeneration) CompatibilityLoading = false; }
    }

    private async Task OpenDependency(CatalogDependency dependency)
    {
        var item = new CatalogItem { Name = dependency.Name, ChineseName = dependency.ChineseName, Source = dependency.Source,
            Slug = dependency.Source == "modrinth" ? dependency.ProjectId : "",
            Id = System.Text.Json.JsonSerializer.SerializeToElement(dependency.ProjectId) };
        var panel = new ProjectDetailsPanel(item, _game.SelectedIndex <= 0 ? "" : _game.SelectedItem as string ?? "");
        Dlg.Panel(dependency.DisplayName, panel, 760);
        await panel.EnsureLoadedAsync();
    }

    private sealed class SummaryResponse
    {
        public List<CatalogItem> Rows { get; set; } = new List<CatalogItem>();
        public string Error { get; set; } = "";
    }
}
