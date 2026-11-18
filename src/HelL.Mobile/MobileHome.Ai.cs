using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Mobile.Ai;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private bool aiInitialized;
    private int aiPaintPending;
    private MobileAiProfile? aiProfile;

    private void InitializeAi()
    {
        if (aiInitialized) return;
        aiInitialized = true;
        try
        {
            aiProfile = MobileAiProfile.Read(Config.CONFIG, session.Secrets);
            AiMode.SelectedIndex = aiProfile.Mode == "public" ? 1 : 0;
            AiAddress.Text = aiProfile.Endpoint;
            AiModel.Text = aiProfile.Model;
            AiProviderNotice.Text = aiProfile.Model + " · " + new Uri(aiProfile.Endpoint).Host;
        }
        catch (Exception error)
        {
            AiSettingsPanel.IsVisible = true;
            AiAddress.Text = Py.S(Config.CONFIG.Get("ai_base_url"));
            AiModel.Text = Py.S(Config.CONFIG.Get("ai_model"));
            AiProviderNotice.Text = "尚未配置可用的云端模型";
            AiChatNotice.Text = error is ArgumentException or InvalidOperationException ? error.Message : "无法读取模型连接，请重新填写并保存。";
        }
        UpdateAi();
    }

    private void AiModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AiKeyField == null || AiAddressLabel == null) return;
        bool custom = (AiMode.SelectedItem as ComboBoxItem)?.Tag as string != "public";
        AiKeyField.IsVisible = custom;
        AiAddressLabel.Text = custom ? "HTTPS 服务地址（到 /v1）" : "HTTPS 网关地址";
    }

    private void ToggleAiSettings(object? sender, RoutedEventArgs e) => AiSettingsPanel.IsVisible = !AiSettingsPanel.IsVisible;

    private async void SaveAiSettings(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        string mode = (AiMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "custom";
        string key = AiKey.Text ?? "";
        if (key.Length == 0 && aiProfile?.Mode == mode) key = aiProfile.ApiKey;
        var profile = MobileAiProfile.Validate(mode, AiAddress.Text ?? "", AiModel.Text ?? "", key);
        await Task.Run(() => MobileAiProfile.Save(Config.CONFIG, session.Secrets, profile), token);
        aiProfile = profile;
        AiKey.Clear();
        AiProviderNotice.Text = profile.Model + " · " + new Uri(profile.Endpoint).Host;
        AiSettingsPanel.IsVisible = false;
        AiChatNotice.Text = "连接已保存，可以开始对话。";
        Status.Text = "AI 连接已安全保存。";
    }, allowNavigation: true);

    private async void SendAiMessage(object? sender, RoutedEventArgs e) => await Run(async token =>
    {
        var profile = MobileAiProfile.Read(Config.CONFIG, session.Secrets);
        string prompt = AiPrompt.Text?.Trim() ?? "";
        if (prompt.Length == 0) throw new ArgumentException("请先输入消息。");
        AiPrompt.Clear();
        AiChatNotice.Text = "正在等待模型回复…";
        Status.Text = "AI 正在回复，可在底部停止。";
        try
        {
            await Task.Run(() => session.Chat.Send(profile, prompt, QueueAiPaint, token), token);
            AiChatNotice.Text = "回复完成";
            Status.Text = "AI 回复完成。";
        }
        catch (Exception) when (token.IsCancellationRequested) { AiChatNotice.Text = "已停止，收到的文字已保留。"; throw; }
        catch (Exception error)
        {
            string message = error is AIClientError client ? client.Category switch
            {
                "auth" => "模型服务拒绝授权，请检查 API 密钥。",
                "rate_limited" => "模型服务暂时限流，请稍后重试。",
                "provider_timeout" => "模型服务响应超时，请稍后重试。",
                _ => "无法连接模型服务，请检查地址、网络和模型名称。"
            } : error is ArgumentException ? error.Message : "回复未完成，请检查模型设置或稍后重试。";
            AiChatNotice.Text = message;
            throw new InvalidOperationException(message);
        }
        finally { UpdateAi(); }
    }, allowNavigation: true);

    private void ClearAiChat(object? sender, RoutedEventArgs e)
    {
        if (operation != null) return;
        session.Chat.Clear();
        AiChatNotice.Text = "本次对话已清空。";
        UpdateAi();
    }

    private void QueueAiPaint()
    {
        if (Interlocked.Exchange(ref aiPaintPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref aiPaintPending, 0);
            if (disposed) return;
            UpdateAi();
            AiMessagesScroll.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void UpdateAi()
    {
        var entries = session.Chat.Entries;
        AiMessages.ItemsSource = entries;
        AiEmpty.IsVisible = entries.Count == 0;
        bool idle = operation == null;
        AiSendButton.IsEnabled = AiSaveButton.IsEnabled = AiClearButton.IsEnabled = idle;
        AiMode.IsEnabled = AiAddress.IsEnabled = AiModel.IsEnabled = AiKey.IsEnabled = idle;
    }
}
