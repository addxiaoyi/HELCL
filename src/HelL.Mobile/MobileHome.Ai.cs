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
    private bool aiNearBottom = true;
    private string failedPrompt = "";
    private string ChatPath => Path.Combine(root, "mobile-chat.json");

    private void InitializeAi()
    {
        if (aiInitialized) return;
        aiInitialized = true;
        AiMessagesScroll.ScrollChanged += (_, change) =>
        {
            if (Math.Abs(change.OffsetDelta.Y) > 0.1) aiNearBottom = AiMessagesScroll.Extent.Height - AiMessagesScroll.Viewport.Height - AiMessagesScroll.Offset.Y < 72;
        };
        try { session.Chat.Restore(ChatPath); }
        catch (Exception error) when (error is not OutOfMemoryException) { AiChatNotice.Text = "无法恢复聊天记录：" + error.Message; }
        AiStorageNotice.Text = "对话仅保存在本机 mobile-chat.json，最多保留最近 48 条；不包含 API 密钥。可清空并撤销。";
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

    private async void SendAiMessage(object? sender, RoutedEventArgs e) => await SendAiPromptAsync(AiPrompt.Text?.Trim() ?? "");

    private Task SendAiPromptAsync(string prompt) => Run(async token =>
    {
        var profile = MobileAiProfile.Read(Config.CONFIG, session.Secrets);
        if (prompt.Length == 0) throw new ArgumentException("请先输入消息。");
        failedPrompt = "";
        AiPrompt.Clear();
        aiNearBottom = true;
        AiChatNotice.Text = "正在等待模型回复…";
        Status.Text = "AI 正在回复，可在底部停止。";
        try
        {
            await Task.Run(() => session.Chat.Send(profile, prompt, QueueAiPaint, token), token);
            session.Chat.Save(ChatPath);
            AiChatNotice.Text = "回复完成";
            Status.Text = "AI 回复完成。";
        }
        catch (Exception) when (token.IsCancellationRequested) { AiChatNotice.Text = "已停止，收到的文字已保留。"; throw; }
        catch (Exception error)
        {
            failedPrompt = prompt;
            if (string.IsNullOrWhiteSpace(AiPrompt.Text)) AiPrompt.Text = prompt;
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
        finally
        {
            try { session.Chat.Save(ChatPath); }
            catch (Exception error) when (error is not OutOfMemoryException) { AiStorageNotice.Text = "聊天记录保存失败，当前对话仍保留在内存：" + error.Message; }
            UpdateAi();
        }
    }, allowNavigation: true);

    private async void ClearAiChat(object? sender, RoutedEventArgs e)
    {
        if (operation != null || session.Chat.Entries.Count == 0) return;
        if (!await ConfirmAsync("清空当前聊天？", "清空本机聊天记录后可以撤销；此操作不会修改服务端数据。", "清空聊天")) return;
        session.Chat.Clear();
        try { session.Chat.Save(ChatPath); }
        catch (Exception error) when (error is not OutOfMemoryException) { AiStorageNotice.Text = "保存聊天变更失败，当前内容仍在内存：" + error.Message; }
        AiUndoClearButton.IsVisible = true;
        AiChatNotice.Text = "本机对话已清空，可以撤销。";
        UpdateAi();
    }

    private void UndoClearAi(object? sender, RoutedEventArgs e)
    {
        if (!session.Chat.UndoClear()) return;
        try { session.Chat.Save(ChatPath); }
        catch (Exception error) when (error is not OutOfMemoryException) { AiStorageNotice.Text = "保存聊天变更失败，当前内容仍在内存：" + error.Message; }
        AiUndoClearButton.IsVisible = false;
        AiChatNotice.Text = "已恢复清空前的对话。";
        UpdateAi();
    }
    private async void RetryAiMessage(object? sender, RoutedEventArgs e) { if (failedPrompt.Length > 0) await SendAiPromptAsync(failedPrompt); }
    private void EditFailedAi(object? sender, RoutedEventArgs e) { AiPrompt.Text = failedPrompt; AiPrompt.Focus(); }
    private void JumpToLatestAi(object? sender, RoutedEventArgs e) { aiNearBottom = true; AiMessagesScroll.ScrollToEnd(); AiLatestButton.IsVisible = false; }

    private void QueueAiPaint()
    {
        if (Interlocked.Exchange(ref aiPaintPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref aiPaintPending, 0);
            if (disposed) return;
            UpdateAi();
            if (aiNearBottom) AiMessagesScroll.ScrollToEnd();
            else AiLatestButton.IsVisible = true;
        }, DispatcherPriority.Background);
    }

    private void UpdateAi()
    {
        var entries = session.Chat.Entries;
        AiMessages.ItemsSource = entries;
        AiEmpty.IsVisible = entries.Count == 0;
        AiRetryButton.IsVisible = AiEditRetryButton.IsVisible = failedPrompt.Length > 0;
        bool idle = operation == null;
        AiSendButton.IsEnabled = AiSaveButton.IsEnabled = AiClearButton.IsEnabled = idle;
        AiMode.IsEnabled = AiAddress.IsEnabled = AiModel.IsEnabled = AiKey.IsEnabled = idle;
    }
}
