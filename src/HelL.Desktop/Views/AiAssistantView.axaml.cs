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
        catch
        {
            ConfigPanel.IsVisible = true;
        }
        SyncModeVisibility();
        RefreshMessages();
    }

    internal void PrefillPrompt(string text)
    {
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

    private async void SaveConfig(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        await host.RunTask(async token =>
        {
            string mode = (ModePicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "ollama";
            string key = ApiKeyBox.Text ?? "";
            if (key.Length == 0 && activeProfile?.Mode == mode) key = activeProfile.ApiKey;
            var profile = DesktopAiProfile.Validate(mode, EndpointBox.Text ?? "", ModelBox.Text ?? "", key);
            await Task.Run(() => DesktopAiProfile.Save(Config.CONFIG, session.Secrets, profile), token);
            activeProfile = profile;
            ApiKeyBox.Clear();
            ConfigPanel.IsVisible = false;
            ProviderSubtitle.Text = $"当前模型：{profile.Model} · {new Uri(profile.Endpoint).Host}";
            host.SetStatus("AI 模型连接配置已保存。");
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

    private void DiagnoseLatestLog(object? sender, RoutedEventArgs e)
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
        string[] lines = File.ReadAllLines(latestLog);
        string tail = string.Join(Environment.NewLine, lines.TakeLast(120));
        PromptInput.Text = $"请帮我诊断实例「{instName}」的 latest.log 日志末尾输出：\n\n```\n{tail}\n```";
        SendPrompt(sender, e);
    }

    private async void SendPrompt(object? sender, RoutedEventArgs e)
    {
        if (host == null) return;
        string prompt = PromptInput.Text?.Trim() ?? "";
        if (prompt.Length == 0)
        {
            PromptInput.Focus();
            return;
        }
        await host.RunTask(async token =>
        {
            var profile = DesktopAiProfile.Read(Config.CONFIG, session.Secrets);
            PromptInput.Clear();
            host.SetStatus($"正在请求 {profile.Model} 生成回复…");
            try
            {
                await Task.Run(() => session.Send(profile, prompt, QueueRefresh, token), token);
                host.SetStatus("飞鱼 AI 回复完成。");
            }
            finally
            {
                RefreshMessages();
            }
        });
    }

    private async void CopyBubbleText(object? sender, RoutedEventArgs e)
    {
        if (host?.Clipboard == null || sender is not Button { Tag: string content } || string.IsNullOrEmpty(content)) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(host.Clipboard, content);
        host.SetStatus("已复制飞鱼 AI 回复内容。");
    }

    private void ClearConversation(object? sender, RoutedEventArgs e)
    {
        try
        {
            session.Clear();
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
            RefreshMessages();
            ChatScroll.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void RefreshMessages()
    {
        var entries = session.Entries;
        MessageItems.ItemsSource = entries;
        StarterPanel.IsVisible = entries.Count == 0;
    }
}
