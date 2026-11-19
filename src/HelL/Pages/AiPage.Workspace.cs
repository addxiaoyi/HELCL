using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class AiPage
{
    private readonly Button _modelButton = Ui.Btn("", BtnKind.Ghost);
    private readonly TextBlock _modelName = Ui.Txt("选择模型", 15, true);
    private readonly TextBlock _sourceLabel = Ui.Txt("本地 / 云端", 11, false, "B.InkMuted");
    private readonly Popup _modelPopup = new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom };
    private ModelPicker? _modelPicker;
    private string _selectedModel = "";
    private int _modelSyncGeneration;
    private bool _switchingModel;
    private Grid? _conversationGrid;
    private FrameworkElement? _composerArea;
    private readonly ContentControl _library = new ContentControl { Visibility = Visibility.Collapsed };
    private Grid? _chatWorkspace;
    private LocalModelsPanel? _modelsPanel;
    private readonly ContentControl _extensionsHost = new ContentControl { Visibility = Visibility.Collapsed };
    private bool _localMode;
    private bool _localTools;
    private FrameworkElement[] _cloudControls = Array.Empty<FrameworkElement>();

    private static (string, string, string, string)[] LocalQuickStarts => new[] {
        ("入门生存", "第一天应该准备什么", QuickStarts[0].Icon, "给我一个 Minecraft 生存第一天的简短计划"),
        ("模组建议", "解释模组用途与搭配", QuickStarts[1].Icon, "介绍 Fabric 中常见的性能优化模组，以及它们分别解决什么问题"),
        ("理解日志", "粘贴日志，一起分析", QuickStarts[2].Icon, "我想分析 Minecraft 崩溃日志，我应该复制其中哪些部分给你？"),
        ("聊聊配置", "了解 Java 与内存设置", QuickStarts[3].Icon, "解释 Minecraft 的 Java 版本与内存分配应该怎么选")
    };

    private FrameworkElement BuildModelHeader()
    {
        _modelName.TextTrimming = TextTrimming.CharacterEllipsis;
        _sourceLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        var selector = Ui.G(null, "*,Auto");
        selector.Add(Ui.V(3, _modelName, _sourceLabel), 0);
        selector.Add(Ui.Glyph("\ue70d", 11, "B.InkMuted").M(14, 0, 2, 0).VCenter(), 0, 1);
        _modelButton.Content = selector;
        _modelButton.MaxWidth = 360;
        _modelButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _modelButton.HorizontalAlignment = HorizontalAlignment.Left;
        _modelButton.Padding = new Thickness(10, 5, 10, 5);
        _modelButton.ToolTip = "选择本地或云端模型";
        AutomationProperties.SetName(_modelButton, "选择模型");
        _modelButton.Click += (_, _) => OpenModelPicker();
        _modelPopup.PlacementTarget = _modelButton;
        _modelPopup.Closed += (_, _) => _modelButton.Focus();
        Unloaded += (_, _) => _modelPopup.IsOpen = false;
        _titleText.FontSize = 12;
        _titleText.FontWeight = FontWeights.Normal;
        _titleText.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
        return _modelButton;
    }

    private void OpenModelPicker()
    {
        if (_busy || _switchingModel) return;
        if (_modelPopup.IsOpen) { _modelPopup.IsOpen = false; return; }
        _modelPicker = new ModelPicker(_selectedModel, _localMode, SelectModelAsync, () => { _modelPopup.IsOpen = false; OpenModels(); }, Math.Max(120, Math.Min(340, ActualHeight - 240)));
        var shell = new Border { Child = _modelPicker, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Width = Math.Min(460, Math.Max(300, ActualWidth - 48)) };
        shell.SetResourceReference(Border.BackgroundProperty, "B.Popup");
        shell.SetResourceReference(Border.BorderBrushProperty, "B.PopupBorder");
        shell.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _modelPopup.IsOpen = false; e.Handled = true; } };
        KeyboardNavigation.SetTabNavigation(shell, KeyboardNavigationMode.Cycle);
        _modelPopup.Child = shell;
        _modelPopup.IsOpen = true;
        Run(_modelPicker.EnsureLoadedAsync);
    }

    private async Task SelectModelAsync(ModelChoice choice)
    {
        if (_busy) throw new InvalidOperationException("请等当前回复结束后再切换模型。");
        _switchingModel = true;
        _modelButton.IsEnabled = false;
        _send.IsEnabled = false;
        ++_modelSyncGeneration;
        try
        {
            if (choice.Local) await Api.CallAsync("ai_local_select", new { model = choice.Name, endpoint = choice.Endpoint });
            else await Api.CallAsync("ai_cloud_select", new { model = choice.Name });
            await SyncModelAsync();
            _modelPopup.IsOpen = false;
            if (ActiveChat?.Messages.Count == 0) RenderMessages();
            _input.Focus();
        }
        finally
        {
            _switchingModel = false;
            _modelButton.IsEnabled = !_busy;
            _send.IsEnabled = !_busy;
        }
    }

    private async Task SyncModelAsync()
    {
        int generation = ++_modelSyncGeneration;
        var settings = await Api.CallAsync<Dictionary<string, JsonElement>>("get_settings") ?? throw new InvalidOperationException("无法读取当前模型设置。");
        if (generation != _modelSyncGeneration) return;
        _localMode = settings.TryGetValue("ai_mode", out var mode) && mode.GetString() == "local";
        _localTools = settings.TryGetValue("ai_local_tools", out var tools) && tools.ValueKind == JsonValueKind.True;
        string key = _localMode ? "ai_local_model" : "ai_model";
        _selectedModel = settings.TryGetValue(key, out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() ?? "" : "";
        _modelName.Text = _selectedModel.Length > 0 ? _selectedModel : "选择模型";
        _sourceLabel.Text = _selectedModel.Length == 0 ? "本地 / 云端" : _localMode ? (_localTools ? "本地 · 工具助手" : "本地 · 文字聊天") : "云端连接";
        SyncConnection();
    }

    private void AttachWorkspace(Grid chat)
    {
        _chatWorkspace = chat;
        Content = new Grid { Children = { chat, _library, _extensionsHost } };
    }

    private void ConfigureConversationLayout(Grid grid, FrameworkElement composer)
    {
        _conversationGrid = grid;
        _composerArea = composer;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        SizeChanged += (_, _) => SyncConversationLayout();
    }

    private void SyncConversationLayout()
    {
        if (_conversationGrid == null || _composerArea == null) return;
        bool empty = _emptyView != null && !_busy;
        _conversationGrid.RowDefinitions[2].Height = GridLength.Auto;
        _conversationGrid.RowDefinitions[4].Height = empty ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(_scroll, empty ? 2 : 1);
        Grid.SetRow(_composerArea, empty ? 3 : 2);
        _scroll.MaxHeight = empty ? Math.Max(150, ActualHeight - 250) : double.PositiveInfinity;
        _messages.Margin = empty ? new Thickness(28, 0, 28, 8) : new Thickness(28, 18, 28, 12);
        SyncDockHeight();
    }

    private void SyncDockHeight()
    {
        // Keep confirmation actions and the composer reachable in short windows.
        double height = Math.Max(150, ActualHeight - _composer.ActualHeight - 180);
        foreach (FrameworkElement card in _dock.Children) card.MaxHeight = height;
    }

    private void OpenModels()
    {
        bool firstOpen = _modelsPanel == null;
        if (_modelsPanel == null)
        {
            _modelsPanel = new LocalModelsPanel(CloseModels, async () =>
            {
                await SyncModelAsync();
                RenderMessages();
                CloseModels();
            });
            _library.Content = _modelsPanel;
        }
        if (_chatWorkspace != null) _chatWorkspace.Visibility = Visibility.Collapsed;
        _modelPopup.IsOpen = false;
        _library.Visibility = Visibility.Visible;
        Run(firstOpen ? _modelsPanel.EnsureLoadedAsync : _modelsPanel.RefreshAsync);
    }

    private void OpenExtensions()
    {
        if (_chatWorkspace != null) _chatWorkspace.Visibility = Visibility.Collapsed;
        _library.Visibility = Visibility.Collapsed; _modelPopup.IsOpen = false;
        var panel = new ExtensionsPanel(CloseExtensions);
        _extensionsHost.Content = panel; _extensionsHost.Visibility = Visibility.Visible;
        Run(panel.EnsureLoadedAsync);
    }
    private void CloseExtensions()
    {
        _extensionsHost.Visibility = Visibility.Collapsed; _extensionsHost.Content = null;
        if (_chatWorkspace != null) _chatWorkspace.Visibility = Visibility.Visible;
        Run(SyncModelAsync); _input.Focus();
    }

    private void CloseModels()
    {
        _library.Visibility = Visibility.Collapsed;
        if (_chatWorkspace != null) _chatWorkspace.Visibility = Visibility.Visible;
        Run(SyncModelAsync);
        _input.Focus();
    }

    private void SyncConnection()
    {
        _perm.IsEnabled = !_localMode;
        foreach (var control in _cloudControls) control.Visibility = _localMode ? Visibility.Collapsed : Visibility.Visible;
        _instPill.Visibility = _localMode ? Visibility.Collapsed : Visibility.Visible;
        _permTip.Text = _localMode ? (_localTools ? "本地工具助手 · 操作前确认" : "本地文字聊天 · 使用最近上下文 · 不执行工具操作") : _permTip.Text;
        if (!_localMode) SyncPermTip();
        SyncPlaceholder();
    }
}
