using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckMobileDetailsAsync()
    {
        var failures = new List<string>();
        var checks = new List<string>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
        }
        string[] fields = ["mobile_ai_mode", "mobile_ai_url", "mobile_ai_model", "mobile_ai_key_id"];
        var originalSettings = fields.ToDictionary(field => field,
            field => (Exists: Config.CONFIG.data.ContainsKey(field), Value: Config.CONFIG.Get(field)));
        string draft = AiPrompt.Text ?? "";
        string query = Query.Text ?? "";
        int kind = Kind.SelectedIndex;
        try
        {
            ShowPage("ai");
            Config.CONFIG.Commit(PyDict.Of("mobile_ai_mode", "public", "mobile_ai_url", "https://fixture.invalid/v1",
                "mobile_ai_model", "details-fixture", "mobile_ai_key_id", ""));
            int sends = 0;
            aiSendPreview = (_, _) => { sends++; return Task.FromException(new IOException("controlled offline reply")); };
            await SendAiPromptAsync("旧的失败问题");
            Check(failedPrompt == "旧的失败问题", "failed replies expose the original question for recovery");
            AiPrompt.Text = "刚刚写好的新问题";
            aiSendPreview = (_, _) => { sends++; return Task.CompletedTask; };
            RetryAiMessage(this, new RoutedEventArgs());
            await SettleMobileDetailsAsync();
            Check(sends == 1 && AiPrompt.Text == "刚刚写好的新问题", "retry keeps a newer draft and does not submit the old question");

            failedPrompt = "旧的失败问题";
            UpdateAi();
            EditFailedAi(this, new RoutedEventArgs());
            await SettleMobileDetailsAsync();
            bool asksBeforeReplacing = confirmation is not null && AiPrompt.Text == "刚刚写好的新问题";
            Check(asksBeforeReplacing, "editing a failed question asks before replacing a different draft");
            CompleteConfirmation(false);
            await SettleMobileDetailsAsync();
            Check(AiPrompt.Text == "刚刚写好的新问题" && AiPrompt.IsFocused,
                "canceling failed-question editing preserves the current draft and input focus");
            AiPrompt.Text = "刚刚写好的新问题";
            EditFailedAi(this, new RoutedEventArgs());
            CompleteConfirmation(true);
            await SettleMobileDetailsAsync();
            Check(AiPrompt.Text == "旧的失败问题" && AiPrompt.IsFocused, "confirmed failed-question editing focuses the recovered question");

            AiPrompt.Text = "确认期间应保留的草稿";
            EditFailedAi(this, new RoutedEventArgs());
            failedPrompt = "另一个失败问题";
            CompleteConfirmation(true);
            await SettleMobileDetailsAsync();
            Check(AiPrompt.Text == "确认期间应保留的草稿", "a changed recovery target cannot replace the draft after confirmation");
            AiPrompt.Clear();
            int beforeRetry = sends;
            await RetryFailedAiAsync();
            Check(sends == beforeRetry + 1 && failedPrompt.Length == 0 && !AiRetryButton.IsVisible,
                "retry submits the saved question when the draft is empty and clears stale recovery state on success");

            AiPrompt.Focus();
            Task<bool> modal = ConfirmAsync("受控焦点确认", "取消后应返回消息输入。", "确认");
            await SettleMobileDetailsAsync();
            var cancel = confirmationOverlay?.GetLogicalDescendants().OfType<Button>()
                .SingleOrDefault(button => button.Content?.ToString() == "取消 / 继续编辑");
            Check(cancel?.IsFocused == true, "a confirmation starts on its safe cancel action");
            CompleteConfirmation(false);
            await modal;
            await SettleMobileDetailsAsync();
            Check(AiPrompt.IsFocused, "canceling a confirmation restores its original input focus");

            modal = ConfirmAsync("受控隐藏入口", "离开页面后不应拉回旧输入。", "确认");
            ShowPage("home");
            CompleteConfirmation(false);
            await modal;
            Check(!AiPrompt.IsFocused, "canceling does not restore focus to an input on a hidden page");
            ShowPage("ai");
            AiPrompt.Focus();
            modal = ConfirmAsync("受控禁用入口", "禁用的输入不应恢复焦点。", "确认");
            AiPrompt.IsEnabled = false;
            CompleteConfirmation(false);
            await modal;
            Check(!AiPrompt.IsFocused, "canceling does not restore focus to a disabled input");
            AiPrompt.IsEnabled = true;

            var removed = new Button { Content = "受控临时入口" };
            Pages.Children.Add(removed);
            removed.Focus();
            modal = ConfirmAsync("受控移除入口", "入口已移除时不应恢复焦点。", "确认");
            Pages.Children.Remove(removed);
            CompleteConfirmation(false);
            await modal;
            Check(!removed.IsFocused, "canceling does not restore focus to a removed control");

            failedPrompt = "不应在清空后保留的问题";
            if (session.Chat.Entries.Count == 0)
            {
                string fixture = Path.Combine(EvidenceRoot, "details-chat-fixture.json");
                File.WriteAllText(fixture, JsonSerializer.Serialize(new[] { new Ai.ChatEntry("user", "controlled retained question", "complete") }));
                session.Chat.Restore(fixture);
            }
            AiPrompt.Clear();
            UpdateAi();
            ClearAiChat(this, new RoutedEventArgs());
            CompleteConfirmation(true);
            await SettleMobileDetailsAsync();
            Check(session.Chat.Entries.Count == 0 && failedPrompt.Length == 0 && !AiRetryButton.IsVisible && !AiEditRetryButton.IsVisible,
                "clearing chat removes stale failed-question recovery actions");

            Kind.SelectedItem = Kind.Items.OfType<ComboBoxItem>().Single(item => (string?)item.Tag == "resourcepack");
            Query.Text = "details-scroll-fixture";
            resourceSearchPreview = _ => Task.FromResult<IReadOnlyList<CatalogProject>>(Enumerable.Range(0, 30)
                .Select(index => RecheckProject("details-" + index, "details-" + index)).ToArray());
            ShowPage("resources");
            await SearchResourcesAsync(false);
            await SettleMobileDetailsAsync();
            double wanted = Math.Min(500, ResourcesPage.Extent.Height - ResourcesPage.Viewport.Height);
            Check(wanted > 100, "the resource navigation fixture has a real scrollable list");
            ResourcesPage.Offset = new Vector(0, wanted);
            await SettleMobileDetailsAsync();
            double before = ResourcesPage.Offset.Y;
            ShowPage("home");
            ShowPage("resources");
            await SettleMobileDetailsAsync();
            Check(Math.Abs(ResourcesPage.Offset.Y - before) < 1, "leaving and returning restores the resource list reading position");
            await Capture("details-resource-return");
        }
        finally
        {
            CompleteConfirmation(false);
            aiSendPreview = null;
            resourceSearchPreview = null;
            failedPrompt = "";
            AiPrompt.Text = draft;
            Kind.SelectedIndex = kind;
            Query.Text = query;
            lock (Config.CONFIG._lock)
            {
                foreach (var (field, previous) in originalSettings)
                    if (previous.Exists) Config.CONFIG.data[field] = previous.Value;
                    else Config.CONFIG.data.Remove(field);
                Config.CONFIG.Save();
            }
            resourceListOffset = Vector.Zero;
            ResourcesPage.Offset = Vector.Zero;
            ShowPage("home", remember: false);
            pageHistory.Clear();
            UpdateAi();
        }
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-details-preview.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks,
            desktopHosted = !OperatingSystem.IsAndroid(), physicalDeviceVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Mobile interaction details failed: " + string.Join("; ", failures));
    }

    private static async Task SettleMobileDetailsAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(80);
    }
}
