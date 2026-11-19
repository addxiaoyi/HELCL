using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using HelL.Mobile.Workflow;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private const double TaskScrollBarClearance = 12;
    private readonly StackPanel localTasks = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private ScrollViewer? localTasksPage;
    private readonly TextBlock createError = WorkflowText("");
    private readonly TextBlock editError = WorkflowText("");
    private readonly Stack<string> pageHistory = new();
    private string currentPage = "home";
    private int navigationRevision;
    private string? savedEditState;
    private string? activeLocalTask;
    private TaskCompletionSource<bool>? confirmation;
    private Border? confirmationOverlay;
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

    private void ApplyTextScale()
    {
        foreach (Control control in this.GetLogicalDescendants().OfType<Control>())
        {
            double font = control is TextBlock label ? label.FontSize : control is Avalonia.Controls.Primitives.TemplatedControl field ? field.FontSize : 0;
            if (font <= 0) continue;
            double original = originalFontSizes.GetValue(control, _ => new FontSizeSnapshot(font)).Value;
            double scaled = Math.Max(12, original) * Math.Max(MobileRuntime.SystemTextScale, MobileRuntime.TextScale);
            if (control is TextBlock text) { text.FontSize = scaled; text.TextWrapping = TextWrapping.Wrap; }
            else if (control is Avalonia.Controls.Primitives.TemplatedControl target) target.FontSize = scaled;
        }
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

    private async Task NavigateCheckedAsync(string page, bool remember = true)
    {
        if (confirmation is not null) return;
        if (currentPage == "manage" && page != "manage" && savedEditState != EditState())
        {
            if (!await ConfirmAsync("离开实例编辑？", "这些修改尚未保存。选择继续编辑可保留当前内容。", "放弃修改并离开")) return;
            if (instances.FirstOrDefault(item => item.Name == selected) is { } existing) PresentInstance(existing);
        }
        ShowPage(page, remember);
    }

    public bool RequestBack()
    {
        if (confirmation is not null) { CompleteConfirmation(false); return true; }
        if (DetailsPanel.IsVisible && ResourcesPage.IsVisible) { BackToSearch(this, new RoutedEventArgs()); return true; }
        if (currentPage == "home") return false;
        string target = pageHistory.Count > 0 ? pageHistory.Pop() : "home";
        _ = NavigateCheckedAsync(target, remember: false);
        return true;
    }

    private void WorkflowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && RequestBack()) e.Handled = true;
    }

    private static void ValidateInstanceName(string name, TextBox field, TextBlock notice)
    {
        if (name.Length is >= 1 and <= 48 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Any(char.IsControl)
            && name is not ("." or "..")) return;
        notice.Text = "实例名称需为 1–48 个字符，不能包含路径符号或控制字符。";
        field.Focus();
        throw new ArgumentException(notice.Text);
    }

    private void PresentLocalTasks()
    {
        localTasks.Children.Clear();
        localTasks.Children.Add(new TextBlock { Text = "本机任务", Classes = { "title" } });
        localTasks.Children.Add(WorkflowText("本次启动最多保留 100 条任务。导出诊断包只包含脱敏摘要，不包含原始任务文本。"));
        var clear = new Button { Content = "清除已结束任务", Classes = { "secondary" } };
        clear.Click += (_, _) => { session.Tasks.ClearFinished(); PresentLocalTasks(); };
        localTasks.Children.Add(clear);
        if (session.Tasks.Entries.Count == 0) localTasks.Children.Add(WorkflowText("尚无任务。搜索、资源下载、账号和实例操作会显示在这里。"));
        foreach (MobileOperation item in session.Tasks.Entries)
        {
            var row = new StackPanel { Spacing = 6 };
            row.Children.Add(new TextBlock { Text = item.Title, Classes = { "section" }, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(WorkflowText(item.Caption));
            row.Children.Add(WorkflowText(item.Message));
            if (item.State == "running")
            {
                var cancel = new Button { Content = "取消当前任务", Classes = { "secondary" } };
                cancel.Click += Cancel;
                row.Children.Add(cancel);
            }
            localTasks.Children.Add(new Border { Classes = { "glass" }, Child = row });
        }
    }

    private Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        if (confirmation is not null) return Task.FromResult(false);
        confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var content = new StackPanel { Spacing = 12, MaxWidth = 520 };
        content.Children.Add(new TextBlock { Text = title, Classes = { "section" }, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(WorkflowText(message));
        var yes = new Button { Content = accept, Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
        var no = new Button { Content = "取消 / 继续编辑", Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
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
        confirmation = null;
        if (confirmationOverlay is not null) Pages.Children.Remove(confirmationOverlay);
        confirmationOverlay = null;
        foreach ((Control background, bool enabled) in confirmationBackground) background.IsEnabled = enabled;
        confirmationBackground.Clear();
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
