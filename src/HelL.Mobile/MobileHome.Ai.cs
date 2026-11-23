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
    private Func<string, CancellationToken, Task>? aiSendPreview;
    private readonly Button aiRecoverHistory = new() { Content = "备份损坏记录并重置", Classes = { "secondary" }, MinHeight = 44 };
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
        AiStorageNotice.Text = "仅存本机 · 最近 48 条 · 清空可撤销";
        aiRecoverHistory.Click += RecoverChatHistory;
        if (AiStorageNotice.Parent is StackPanel storage) storage.Children.Insert(storage.Children.IndexOf(AiStorageNotice) + 1, aiRecoverHistory);
        if (session.Chat.NeedsHistoryRecovery) AiStorageNotice.Text = "原聊天记录无法读取，已阻止覆盖。可先备份原件并新建聊天记录。";
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

    private async void SaveAiSettings(object? sender, RoutedEventArgs e)
    {
        if (operation != null) { Status.Text = "当前任务尚未结束，请稍后保存连接。"; return; }
        AiSettingsError.IsVisible = false;
        string mode = (AiMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "custom";
        string key = AiKey.Text ?? "";
        string endpoint = (AiAddress.Text ?? "").Trim().TrimEnd('/');
        bool sameProvider = aiProfile?.Mode == mode && aiProfile.Endpoint == endpoint;
        if (key.Length == 0 && sameProvider) key = aiProfile!.ApiKey;
        MobileAiProfile profile;
        try
        {
            if (mode == "custom" && key.Length == 0 && aiProfile?.Mode == "custom" && !sameProvider)
                throw new ArgumentException("服务地址已改变，请填写新服务的 API 密钥。旧服务密钥不会自动沿用。");
            profile = MobileAiProfile.Validate(mode, endpoint, AiModel.Text ?? "", key);
        }
        catch (ArgumentException error)
        {
            AiSettingsError.Text = error.Message;
            AiSettingsError.IsVisible = AiSettingsPanel.IsVisible = true;
            AiChatNotice.Text = "连接未保存，请检查设置。";
            Status.Text = error.Message;
            Dispatcher.UIThread.Post(() => AiSaveButton.BringIntoView(), DispatcherPriority.Loaded);
            return;
        }
        await Run(async token =>
        {
            try
            {
                await Task.Run(() => MobileAiProfile.Save(Config.CONFIG, session.Secrets, profile), token);
                aiProfile = profile;
                AiKey.Clear();
                AiProviderNotice.Text = profile.Model + " · " + new Uri(profile.Endpoint).Host;
                AiSettingsPanel.IsVisible = false;
                AiChatNotice.Text = "连接已保存，可以开始对话。";
                Status.Text = "AI 连接已安全保存。";
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                AiSettingsError.Text = "连接未保存。请检查系统凭据库或存储权限后重试，填写内容已保留。";
                AiSettingsError.IsVisible = AiSettingsPanel.IsVisible = true;
                throw;
            }
        }, allowNavigation: true);
    }

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
            if (aiSendPreview is { } preview) await preview(prompt, token);
            else await Task.Run(() => session.Chat.Send(profile, prompt, QueueAiPaint, token), token);
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
        if (disposed) return;
        if (operation is not null) { AiChatNotice.Text = "有任务开始执行，请等待或停止任务后再清空聊天。"; return; }
        try { session.Chat.Clear(); }
        catch (InvalidOperationException error) { AiChatNotice.Text = error.Message; return; }
        failedPrompt = "";
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
    private async void RecoverChatHistory(object? sender, RoutedEventArgs e)
    {
        if (operation is not null || !session.Chat.NeedsHistoryRecovery) return;
        bool accepted = await ConfirmAsync("备份并重置聊天记录？", "将无法读取的原文件原样移到随机命名的 .corrupt 备份，保留原权限；然后新建聊天记录。当前未保存的聊天会被清空。", "备份原件并新建");
        if (!accepted || disposed || operation is not null) return;
        try
        {
            string backup = session.Chat.RecoverHistory(ChatPath);
            session.Chat.Save(ChatPath);
            AiStorageNotice.Text = "已保留原件：" + Path.GetFileName(backup) + "。新的聊天记录可以正常保存。";
            UpdateAi();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { AiStorageNotice.Text = "重置未完成，原件没有被删除：" + error.Message; }
    }
    private async void RetryAiMessage(object? sender, RoutedEventArgs e) => await RetryFailedAiAsync();
    private async Task RetryFailedAiAsync()
    {
        if (failedPrompt.Length == 0 || operation is not null) return;
        string draft = AiPrompt.Text?.Trim() ?? "";
        if (draft.Length > 0 && draft != failedPrompt)
        {
            AiChatNotice.Text = "新草稿已保留。请先发送或清空草稿，再重试上次问题。";
            AiPrompt.Focus();
            return;
        }
        await SendAiPromptAsync(failedPrompt);
    }
    private async void EditFailedAi(object? sender, RoutedEventArgs e) => await EditFailedAiAsync();
    private async Task EditFailedAiAsync()
    {
        if (failedPrompt.Length == 0 || operation is not null) return;
        string previous = failedPrompt;
        string draft = AiPrompt.Text ?? "";
        if (!string.IsNullOrWhiteSpace(draft) && draft.Trim() != previous
            && !await ConfirmAsync("替换当前草稿？", "上次失败的问题将替换当前输入。取消可继续编辑新草稿。", "替换为失败问题")) return;
        if (disposed || operation is not null || failedPrompt != previous || AiPrompt.Text != draft) return;
        AiPrompt.Text = previous;
        AiPrompt.Focus();
    }
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
        AiRetryButton.IsEnabled = AiEditRetryButton.IsEnabled = idle;
        aiRecoverHistory.IsVisible = session.Chat.NeedsHistoryRecovery;
        aiRecoverHistory.IsEnabled = idle;
        AiSendButton.IsEnabled = AiSaveButton.IsEnabled = AiClearButton.IsEnabled = idle;
        AiMode.IsEnabled = AiAddress.IsEnabled = AiModel.IsEnabled = AiKey.IsEnabled = idle;
    }
}
