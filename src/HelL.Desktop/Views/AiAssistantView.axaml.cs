using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelL.Core.Launcher;
using HelL.Desktop.Services;

namespace HelL.Desktop.Views;

public partial class AiAssistantView : UserControl
{
    private readonly DesktopAiSession session = new();
    private MainWindow? host;
    private DesktopAiProfile? activeProfile;
    private int paintQueued;
    private string failedPrompt = "";
    private bool failedPromptNeedsReview;
    private bool reviewLog;
    internal Func<string, CancellationToken, Task>? SendFixture { get; set; }
    internal Func<DesktopAiProfile, CancellationToken, Task>? SaveConfigFixture { get; set; }

    public AiAssistantView() => InitializeComponent();

    internal void Initialize(MainWindow window)
    {
        host = window;
        try
        {
            activeProfile = DesktopAiProfile.Read(Config.CONFIG, session.Secrets);
            ModePicker.SelectedIndex = activeProfile.Mode switch { "custom" => 1, "public" => 2, _ => 0 };
            EndpointBox.Text = activeProfile.Endpoint;
            ModelBox.Text = activeProfile.Model;
            ProviderSubtitle.Text = $"当前模型：{activeProfile.Model} · {new Uri(activeProfile.Endpoint).Host}";
        }
        catch (Exception error)
        {
            ConfigPanel.IsVisible = true;
            ProviderSubtitle.Text = "AI 配置不可用：" + (error is IOException ? "请检查系统密钥库或保存配置。" : "请检查服务地址、模型与密钥。");
            System.Diagnostics.Trace.TraceWarning("Desktop AI configuration: " + error.GetType().Name);
        }
        SyncModeVisibility();
        RefreshMessages();
    }

    internal void PrefillPrompt(string text, bool log = true)
    {
        reviewLog = log;
        PromptInput.Text = text;
        PromptInput.Focus();
    }

    private void ToggleConfig(object? sender, RoutedEventArgs e) => ConfigPanel.IsVisible = !ConfigPanel.IsVisible;

    private void ModeChanged(object? sender, SelectionChangedEventArgs e) => SyncModeVisibility();

    private void SyncModeVisibility()
    {
        if (ModePicker == null || ApiKeyRow == null || SaveLocalButton == null) return;
        string mode = (ModePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "ollama";
        bool needsKey = mode == "custom";
        ApiKeyRow.IsVisible = needsKey;
        SaveLocalButton.IsVisible = !needsKey;
        if (mode == "ollama" && string.IsNullOrWhiteSpace(EndpointBox.Text))
            EndpointBox.Text = "http://127.0.0.1:11434/v1";
    }

    private async void SaveConfig(object? sender, RoutedEventArgs e) => await SaveConfigurationAsync();

    internal async Task SaveConfigurationAsync()
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            string mode = (ModePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "ollama";
            string key = ApiKeyBox.Text ?? "";
            if (key.Length == 0 && activeProfile?.Mode == mode) key = activeProfile.ApiKey;
            var profile = DesktopAiProfile.Validate(mode, EndpointBox.Text ?? "", ModelBox.Text ?? "", key);
            bool wasEnabled = ConfigPanel.IsEnabled;
            ConfigPanel.IsEnabled = false;
            try
            {
                if (SaveConfigFixture is { } save) await save(profile, token);
                else await Task.Run(() => DesktopAiProfile.Save(Config.CONFIG, session.Secrets, profile), token);
                activeProfile = profile;
                ApiKeyBox.Clear();
                ConfigPanel.IsVisible = false;
                ProviderSubtitle.Text = $"当前模型：{profile.Model} · {new Uri(profile.Endpoint).Host}";
                host.SetStatus("AI 模型连接配置已保存。");
            }
            finally { ConfigPanel.IsEnabled = wasEnabled; }
        });
    }

    private void UseStarterPrompt(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string prompt })
        {
            PromptInput.Text = prompt;
            SendPrompt(sender, e);
        }
    }

    private async void DiagnoseLatestLog(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        string? instName = host.GameLauncher.SelectedInstance;
        if (string.IsNullOrWhiteSpace(instName) || !host.GameLauncher.InstancesList().Any(i => i.Name == instName))
        {
            host.SetStatus("请先创建并选择一个实例后再读取日志。");
            return;
        }
        var inst = new Instance(instName);
        string latestLog = Path.Combine(inst.path, "logs", "latest.log");
        if (!File.Exists(latestLog))
        {
            PromptInput.Text = $"我的实例「{instName}」目前还没有生成 logs/latest.log，请告诉我启动 Minecraft 前需要检查哪些常见环境项？";
            PromptInput.Focus();
            return;
        }
        await LoadLogReviewAsync(instName, latestLog);
    }

    internal async Task<bool> LoadLogReviewAsync(string instance, string path, Func<string, Task<string>>? read = null)
    {
        string draft = PromptInput.Text ?? "";
        try
        {
            string tail = await (read?.Invoke(path) ?? Task.Run(() => LogPreview.ReadTail(path)));
            if (PromptInput.Text != draft) { host?.SetStatus("日志已读取，但输入已改变。为保留新草稿，请重新点击读取。"); return false; }
            PromptInput.Text = $"请分析实例“{instance}”的 latest.log 日志末尾内容：\n\n```\n{tail}\n```";
            reviewLog = true;
            PromptInput.Focus();
            host?.SetStatus("已载入脱敏日志预览。确认内容后点击发送；发送前会再次显示目标服务。");
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            host?.SetStatus("日志读取失败，可能已轮换、移除或被占用。原草稿已保留，请刷新后重试。");
            System.Diagnostics.Trace.TraceWarning("Desktop log preview: " + error.GetType().Name);
            return false;
        }
    }

    private async void SendPrompt(object? sender, RoutedEventArgs e) => await SendCurrentPromptAsync();

    internal async Task SendCurrentPromptAsync()
    {
        if (host == null) return;
        string draft = PromptInput.Text ?? "";
        string prompt = draft.Trim();
        if (prompt.Length == 0)
        {
            PromptInput.Focus();
            return;
        }
        DesktopAiProfile profile;
        try { profile = DesktopAiProfile.Read(Config.CONFIG, session.Secrets); }
        catch (Exception error) { host.SetStatus("AI 配置尚未就绪，请检查服务地址、模型和系统密钥库。" ); System.Diagnostics.Trace.TraceWarning(error.GetType().Name); return; }
        bool requiresReview = reviewLog;
        if (requiresReview)
        {
            prompt = LogPreview.Redact(prompt);
            if (!await host.ConfirmAction("发送日志分析", $"目标服务：{new Uri(profile.Endpoint).Host}\n模式：{profile.Mode}\n请确认以下脱敏内容可以发送：\n\n{prompt}", "确认发送")) return;
            if ((PromptInput.Text ?? "") != draft || reviewLog != requiresReview)
            {
                host.SetStatus("待发送内容已变化，新草稿已保留。请重新核对后再发送。");
                PromptInput.Focus();
                return;
            }
        }
        await host.RunTask(async token =>
        {
            PromptInput.Clear();
            reviewLog = false;
            host.SetStatus($"正在请求 {profile.Model} 生成回复…");
            try
            {
                if (SendFixture is { } fixture) await fixture(prompt, token);
                else await Task.Run(() => session.Send(profile, prompt, QueueRefresh, token), token);
                ClearFailedPrompt();
                host.SetStatus("飞鱼 AI 回复完成。");
            }
            catch
            {
                failedPrompt = prompt;
                failedPromptNeedsReview = requiresReview;
                if (string.IsNullOrWhiteSpace(PromptInput.Text))
                {
                    PromptInput.Text = prompt;
                    reviewLog = requiresReview;
                }
                RetryPromptButton.IsVisible = true;
                throw;
            }
            finally
            {
                RefreshMessages();
            }
        });
    }

    private async void CopyBubbleText(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string content }) await CopyReplyAsync(content);
    }

    internal async Task<bool> CopyReplyAsync(string content, Func<string, Task>? write = null)
    {
        if (string.IsNullOrEmpty(content)) return false;
        try
        {
            if (write is not null) await write(content);
            else
            {
                if (host?.Clipboard is not { } clipboard) throw new IOException("Clipboard unavailable.");
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, content);
            }
            host?.SetStatus("已复制飞鱼 AI 回复内容。");
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            host?.SetStatus("回复复制失败，原内容已保留。请检查系统剪贴板后重试。");
            System.Diagnostics.Trace.TraceWarning("AiAssistantView.CopyReplyAsync: " + error.GetType().Name);
            return false;
        }
    }

    private async void ClearConversation(object? sender, RoutedEventArgs e)
    {
        if (host == null || !await host.ConfirmAction("清空 AI 会话", "将清空当前对话。此操作不会删除已导出的内容。", "清空")) return;
        try
        {
            session.Clear();
            ClearFailedPrompt();
            RefreshMessages();
            host?.SetStatus("已清空本次 AI 会话记录。");
        }
        catch (Exception ex) { host?.SetStatus(ex.Message); }
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref paintQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref paintQueued, 0);
            bool following = ChatScroll.Extent.Height - ChatScroll.Viewport.Height - ChatScroll.Offset.Y < 60;
            RefreshMessages();
            if (following) ChatScroll.ScrollToEnd();
            LatestReplyButton.IsVisible = !following;
        }, DispatcherPriority.Background);
    }

    private void JumpToLatest(object? sender, RoutedEventArgs e) { ChatScroll.ScrollToEnd(); LatestReplyButton.IsVisible = false; }
    private async void RetryPrompt(object? sender, RoutedEventArgs e) => await RetryFailedPromptAsync();
    internal async Task RetryFailedPromptAsync()
    {
        if (failedPrompt.Length == 0) return;
        string draft = PromptInput.Text?.Trim() ?? "";
        if (draft.Length > 0 && draft != failedPrompt)
        {
            host?.SetStatus("新草稿已保留。请先发送或清空草稿，再重试上次问题。");
            PromptInput.Focus();
            return;
        }
        PromptInput.Text = failedPrompt;
        reviewLog = failedPromptNeedsReview;
        await SendCurrentPromptAsync();
    }

    private void ClearFailedPrompt()
    {
        failedPrompt = "";
        failedPromptNeedsReview = false;
        RetryPromptButton.IsVisible = false;
    }

    private void RefreshMessages()
    {
        var entries = session.Entries;
        MessageItems.ItemsSource = entries;
        StarterPanel.IsVisible = entries.Count == 0;
    }
}
