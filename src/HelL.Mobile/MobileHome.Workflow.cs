using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using HelL.Mobile.Workflow;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private const double TaskScrollBarClearance = 12;
    private readonly StackPanel localTasks = new() { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left };
    private ScrollViewer? localTasksPage;
    private readonly TextBlock createError = WorkflowText("");
    private readonly TextBlock editError = WorkflowText("");
    private readonly Stack<string> pageHistory = new();
    private bool backNavigationPending;
    private bool resourceTypographyQueued;
    private bool chatTypographyQueued;
    private string currentPage = "home";
    private int navigationRevision;
    private string? savedEditState;
    private string? activeLocalTask;
    private TaskCompletionSource<bool>? confirmation;
    private Border? confirmationOverlay;
    private Control? confirmationFocus;
    private CancellationTokenSource? versionsLoading;
    private sealed record FontSizeSnapshot(double Value);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, FontSizeSnapshot> originalFontSizes = new();
    private readonly Dictionary<Control, bool> confirmationBackground = new();

    private void InitializeWorkflow()
    {
        if (NewName.Parent is StackPanel createFields) createFields.Children.Add(createError);
        if (InstanceName.Parent is StackPanel editFields) editFields.Children.Add(editError);
        if (ReferenceFields.Parent is StackPanel referencePanel)
            referencePanel.Children.Insert(referencePanel.Children.IndexOf(ReferenceFields) + 1,
                WorkflowText("参考版本和加载器仅用于筛选资源；保存此设置不会安装 Minecraft 或加载器内核。"));
        if (NewVersion.Parent is StackPanel createVersion) AddVersionPicker(createVersion, NewVersion);
        if (ReferenceVersion.Parent is StackPanel editVersion) AddVersionPicker(editVersion, ReferenceVersion);
        localTasksPage = new ScrollViewer { IsVisible = false, Content = localTasks,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 18) };
        localTasksPage.PropertyChanged += (_, change) =>
        {
            if (change.Property != ScrollViewer.ViewportProperty) return;
            double width = localTasksPage.Viewport.Width - TaskScrollBarClearance;
            // Overlay scrollbars do not reduce Avalonia's reported viewport width.
            if (width > 0 && double.IsFinite(width)) localTasks.Width = width;
        };
        Pages.Children.Add(localTasksPage);
        Status.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBlock.TextProperty && activeLocalTask is { } task)
                session.Tasks.Update(task, "running", Status.Text ?? "");
        };
        AddHandler(KeyDownEvent, WorkflowKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += AttachMobileInsets;
        Projects.PropertyChanged += QueueResourceTypography;
        Projects.ContainerPrepared += ResourceContainerPrepared;
        AiMessages.PropertyChanged += QueueChatTypography;
        if (MorePage.Content is StackPanel settings)
        {
            double savedScale = HelL.Core.Py.Float(HelL.Core.Launcher.Config.CONFIG.Get("mobile_text_scale", 1d));
            MobileRuntime.TextScale = savedScale is >= 1 and <= 2 ? savedScale : 1;
            settings.Children.Add(WorkflowText("文字大小（同时尊重 Android 系统字体设置）"));
            var scales = new[] { 1d, 1.25, 1.5, 2 };
            int savedIndex = Array.FindIndex(scales, scale => Math.Abs(scale - MobileRuntime.TextScale) < 0.01);
            var textSize = new ComboBox { ItemsSource = new[] { "100%", "125%", "150%", "200%" }, SelectedIndex = Math.Max(0, savedIndex), MinHeight = 44 };
            AutomationProperties.SetName(textSize, "应用文字大小");
            textSize.SelectionChanged += (_, _) =>
            {
                MobileRuntime.TextScale = new[] { 1d, 1.25, 1.5, 2 }[Math.Max(0, textSize.SelectedIndex)];
                try
                {
                    HelL.Core.Launcher.Config.CONFIG.Set("mobile_text_scale", MobileRuntime.TextScale);
                    HelL.Core.Launcher.Config.CONFIG.Save();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Status.Text = "文字大小已在本次会话生效，但偏好保存失败：" + error.Message; }
                ApplyTextScale();
            };
            settings.Children.Add(textSize);
        }
        ApplyTextScale();
    }

    private void QueueResourceTypography(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ItemsControl.ItemsSourceProperty) ScheduleResourceTypography();
    }

    private void ResourceContainerPrepared(object? sender, ContainerPreparedEventArgs args) => ScheduleResourceTypography();

    private void ScheduleResourceTypography()
    {
        if (resourceTypographyQueued) return;
        resourceTypographyQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (disposed) return;
                // Templates created after the initial pass must receive the same user text scale.
                Projects.UpdateLayout();
                ScaleText(this.GetLogicalDescendants().OfType<Control>()
                    .Concat(Projects.GetVisualDescendants().OfType<Control>()).Distinct());
            }
            finally { resourceTypographyQueued = false; }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void QueueChatTypography(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ItemsControl.ItemsSourceProperty) ScheduleChatTypography();
    }

    private void ScheduleChatTypography()
    {
        if (chatTypographyQueued) return;
        chatTypographyQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (disposed) return;
                AiMessages.UpdateLayout();
                ScaleChatText();
            }
            finally { chatTypographyQueued = false; }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void ScaleChatText()
    {
        ScaleText(AiMessages.GetVisualDescendants().OfType<Control>());
        foreach (Controls.ChatContent document in AiMessages.GetVisualDescendants().OfType<Controls.ChatContent>())
            document.TextScale = EffectiveTextScale;
    }

    private void ApplyTextScale()
    {
        ScaleText(this.GetLogicalDescendants().OfType<Control>());
        ScaleChatText();
    }

    private void ScaleText(IEnumerable<Control> content)
    {
        Control[] controls = content.Where(control => !control.GetLogicalAncestors().OfType<Controls.ChatContent>().Any()
            && !control.GetVisualAncestors().OfType<Controls.ChatContent>().Any()).ToArray();
        // Restore parents before sampling new children so inherited sizes do not compound.
        foreach (Control control in controls)
            if (originalFontSizes.TryGetValue(control, out FontSizeSnapshot? saved)) SetFontSize(control, saved.Value);
        var fonts = controls.Select(control => (Control: control, Font: ReadFontSize(control)))
            .Where(entry => entry.Font > 0).ToArray();
        foreach (var entry in fonts)
        {
            double original = originalFontSizes.GetValue(entry.Control, _ => new FontSizeSnapshot(entry.Font)).Value;
            SetFontSize(entry.Control, Math.Max(12, original) * EffectiveTextScale);
            if (entry.Control is TextBlock text)
                text.TextWrapping = text.Classes.Contains("single-line") || text.TextTrimming != TextTrimming.None
                    ? TextWrapping.NoWrap : TextWrapping.Wrap;
        }
        ArrangeTextLayout();
    }

    private static double EffectiveTextScale => Math.Max(MobileRuntime.SystemTextScale, MobileRuntime.TextScale);
    private static double ReadFontSize(Control control) => control is TextBlock text ? text.FontSize
        : control is Avalonia.Controls.Primitives.TemplatedControl field ? field.FontSize : 0;
    private static void SetFontSize(Control control, double size)
    {
        if (control is TextBlock text) text.FontSize = size;
        else if (control is Avalonia.Controls.Primitives.TemplatedControl field) field.FontSize = size;
    }

    private static TextBlock WorkflowText(string text) => new() { Text = text, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

    private void AddVersionPicker(StackPanel field, TextBox target)
    {
        var versions = new ComboBox { PlaceholderText = "选择正式版本，也可手动输入", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        AutomationProperties.SetName(versions, "选择 Minecraft 正式版本");
        versions.SelectionChanged += (_, _) => { if (versions.SelectedItem is string version) target.Text = version; };
        field.Children.Add(versions);
        var refresh = new Button { Content = "加载官方版本列表", Classes = { "secondary" }, MinHeight = 44 };
        refresh.Click += async (_, _) =>
        {
            versionsLoading?.Cancel();
            using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            versionsLoading = pending;
            refresh.IsEnabled = false;
            try
            {
                string[] available = await Task.Run(() => launcher.RemoteVersions(pending.Token), pending.Token);
                if (!disposed) { versions.ItemsSource = available; refresh.Content = $"已加载 {available.Length} 个正式版本"; }
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested)
            { if (!disposed) refresh.Content = "加载超时，点击重试；仍可手动输入"; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!disposed) refresh.Content = "版本列表暂不可用，点击重试"; System.Diagnostics.Trace.TraceWarning("Mobile version picker: " + error.GetType().Name); }
            finally { if (ReferenceEquals(versionsLoading, pending)) versionsLoading = null; if (!disposed) refresh.IsEnabled = true; }
        };
        field.Children.Add(refresh);
    }

    private string EditState() => string.Join("\n", InstanceName.Text, InstanceGroup.Text, ReferenceVersion.Text,
        (ReferenceLoader.SelectedItem as ComboBoxItem)?.Tag);

    private async Task<bool> NavigateCheckedAsync(string page, bool remember = true)
    {
        if (disposed || confirmation is not null) return false;
        if (currentPage == "manage" && page != "manage" && savedEditState != EditState())
        {
            if (!await ConfirmAsync("离开实例编辑？", "这些修改尚未保存。选择继续编辑可保留当前内容。", "放弃修改并离开") || disposed) return false;
            if (instances.FirstOrDefault(item => item.Name == selected) is { } existing) PresentInstance(existing);
        }
        ShowPage(page, remember);
        return true;
    }

    public bool RequestBack()
    {
        if (confirmation is not null) { CompleteConfirmation(false); return true; }
        if (DetailsPanel.IsVisible && ResourcesPage.IsVisible) { BackToSearch(this, new RoutedEventArgs()); return true; }
        if (currentPage == "home") return false;
        if (backNavigationPending) return true;
        _ = NavigateBackAsync();
        return true;
    }

    private async Task NavigateBackAsync()
    {
        backNavigationPending = true;
        string target = pageHistory.Count > 0 ? pageHistory.Peek() : "home";
        try
        {
            if (await NavigateCheckedAsync(target, remember: false) && pageHistory.Count > 0 && pageHistory.Peek() == target)
                pageHistory.Pop();
        }
        finally { backNavigationPending = false; }
    }

    private void WorkflowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !RequestBack()) return;
        e.Handled = true;
        if (OperatingSystem.IsAndroid())
        {
            nativeBackKeyEvents++;
            RecordPlatformAudit();
        }
    }

    private static void ValidateInstanceName(string name, TextBox field, TextBlock notice)
    {
        if (name.Length is >= 1 and <= 48 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(char.IsControl)
            && name is not ("." or "..")) return;
        notice.Text = "实例名称需为 1–48 个字符，不能包含路径符号或控制字符。";
        field.Focus();
        throw new ArgumentException(notice.Text);
    }

    private void PresentLocalTasks(string? notice = null)
    {
        localTasks.Children.Clear();
        localTasks.Children.Add(new TextBlock { Text = "本机任务", FontSize = 22, Margin = new Thickness(0, 0, 0, 8), Classes = { "title" } });
        var storage = WorkflowText("本次启动最多保留 100 条任务。导出诊断包只包含脱敏摘要，不包含原始任务文本。");
        storage.Margin = new Thickness(0, 0, 0, 8);
        localTasks.Children.Add(storage);
        if (!string.IsNullOrWhiteSpace(notice))
        {
            var feedback = WorkflowText(notice);
            feedback.FontSize = 12;
            feedback.Margin = new Thickness(0, 0, 0, 8);
            localTasks.Children.Add(feedback);
        }
        var clear = new Button { Name = "ClearFinishedTasksButton", Content = $"清除已结束任务（{session.Tasks.FinishedCount}）", Classes = { "secondary" },
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), MinHeight = 44,
            IsEnabled = session.Tasks.HasFinished };
        AutomationProperties.SetName(clear, $"清除已结束任务，{session.Tasks.FinishedCount} 条");
        clear.Click += ClearFinishedTasks;
        localTasks.Children.Add(clear);
        if (!session.Tasks.HasFinished)
        {
            string clearHintText = session.Tasks.Entries.Count == 0
                ? "暂无已结束任务可清除。"
                : "运行中的任务结束后可清理历史记录。";
            var clearHint = WorkflowText(clearHintText);
            clearHint.FontSize = 12;
            clearHint.Margin = new Thickness(0, -8, 0, 8);
            localTasks.Children.Add(clearHint);
        }
        if (session.Tasks.Entries.Count == 0) localTasks.Children.Add(WorkflowText("尚无任务。搜索、资源下载、账号和实例操作会显示在这里。"));
        foreach (MobileOperation item in session.Tasks.Entries)
        {
            var row = new StackPanel { Spacing = 5 };
            var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            heading.Children.Add(new TextBlock { Text = item.Title, FontSize = 14, LineHeight = 22, FontWeight = FontWeight.Medium,
                TextWrapping = TextWrapping.Wrap, Classes = { "task-title" } });
            string state = item.State switch { "running" => "进行中", "completed" => "已完成", "cancelled" => "已取消", _ => "失败" };
            var badge = new TextBlock { Text = state, FontSize = 12, LineHeight = 20, TextWrapping = TextWrapping.NoWrap,
                Classes = { "caption", "task-state", "single-line" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 1);
            heading.Children.Add(badge);
            row.Children.Add(heading);
            var caption = WorkflowText(string.Join(" · ", new[] { item.Target, item.Started.ToLocalTime().ToString("HH:mm:ss") }
                .Where(part => !string.IsNullOrWhiteSpace(part))));
            caption.FontSize = 12; caption.LineHeight = 19;
            row.Children.Add(caption);
            if (!string.IsNullOrWhiteSpace(item.Message))
            {
                var message = WorkflowText(item.Message);
                message.FontSize = 13; message.LineHeight = 21;
                row.Children.Add(message);
            }
            if (item.State == "running")
            {
                var cancel = new Button { Content = "取消当前任务", Classes = { "secondary" } };
                cancel.Click += Cancel;
                row.Children.Add(cancel);
            }
            localTasks.Children.Add(new Border { Classes = { "task-reading-row" }, Child = row, Padding = new Thickness(0, 13, 12, 13) });
        }
        ScaleText(localTasks.GetLogicalDescendants().OfType<Control>());
        foreach (TextBlock label in localTasks.GetLogicalDescendants().OfType<TextBlock>())
            label.LineHeight = label.FontSize * 1.5;
    }

    private async void ClearFinishedTasks(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> confirmedIds = session.Tasks.FinishedIds;
        if (confirmedIds.Count == 0) return;
        int requestedCount = confirmedIds.Count;
        bool confirmed = await ConfirmAsync("清除已结束任务？",
            $"将从本次启动的任务历史中移除 {requestedCount} 条已结束任务。运行中的任务不会受影响，此操作无法撤销。",
            "清除历史", "取消");
        if (!confirmed || disposed) return;

        int removed = session.Tasks.ClearFinished(confirmedIds);
        if (removed > 0) PresentLocalTasks($"已清除 {removed} 条已结束任务。");
    }

    private Task<bool> ConfirmAsync(string title, string message, string accept, string cancel = "取消 / 继续编辑")
    {
        if (confirmation is not null) return Task.FromResult(false);
        confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        confirmationFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var content = new StackPanel { Spacing = 12, MaxWidth = 520 };
        content.Children.Add(new TextBlock { Text = title, Classes = { "section" }, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(WorkflowText(message));
        var yes = new Button { Content = accept, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        var no = new Button { Content = cancel, Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        yes.Click += (_, _) => CompleteConfirmation(true);
        no.Click += (_, _) => CompleteConfirmation(false);
        content.Children.Add(yes); content.Children.Add(no);
        var card = new Border { Padding = new Thickness(20), Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("HelL.SurfaceRaised"));
        confirmationOverlay = new Border { Padding = new Thickness(16), Child = card,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        confirmationOverlay.Bind(Border.BackgroundProperty, this.GetResourceObservable("HelL.SurfaceRaised"));
        AutomationProperties.SetName(confirmationOverlay, title);
        foreach (Control background in Pages.Children.Concat(new Control[] { RailNavigation, BottomNavigation })
            .Concat(Shell.Children.OfType<Border>().Where(child => Grid.GetRow(child) == 0)))
        {
            confirmationBackground[background] = background.IsEnabled;
            background.IsEnabled = false;
        }
        Pages.Children.Add(confirmationOverlay);
        no.Focus();
        return confirmation.Task;
    }

    private void CompleteConfirmation(bool accepted)
    {
        TaskCompletionSource<bool>? pending = confirmation;
        Control? previousFocus = confirmationFocus;
        confirmationFocus = null;
        confirmation = null;
        if (confirmationOverlay is not null) Pages.Children.Remove(confirmationOverlay);
        confirmationOverlay = null;
        foreach ((Control background, bool enabled) in confirmationBackground) background.IsEnabled = enabled;
        confirmationBackground.Clear();
        if (!accepted && !disposed && previousFocus is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
            && TopLevel.GetTopLevel(this) is { } top && ReferenceEquals(TopLevel.GetTopLevel(previousFocus), top))
            previousFocus.Focus();
        pending?.TrySetResult(accepted);
    }

    private static string OperationTitle(string method) => method switch
    {
        "SearchResources" or "SearchResourcesAsync" => "搜索兼容资源",
        "LoadReleases" => "加载资源文件",
        "DownloadResource" => "下载并校验资源",
        "SendAiMessage" or "SendAiPromptAsync" => "AI 对话",
        "CreateInstance" => "创建实例", "SaveInstance" => "保存实例设置",
        "LoginMicrosoft" => "微软账号授权", "RefreshAccount" => "刷新账号授权",
        "SaveAiSettings" => "保存 AI 设置", "RestoreAccount" => "恢复账号授权",
        "PinInstance" => "固定实例", "ResetCover" => "恢复默认封面", "PickCover" => "设置实例封面",
        "CheckActivitySession" => "验证界面恢复",
        _ => "应用操作"
    };
}
