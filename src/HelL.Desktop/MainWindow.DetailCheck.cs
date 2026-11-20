using Avalonia.Controls;
using HelL.Desktop.Services;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckDesktopDetailsAsync()
    {
        ShowPage("ai");
        var failures = new List<string>();
        var prompt = AiView.FindControl<TextBox>("PromptInput")!;
        var panel = AiView.FindControl<Border>("ConfigPanel")!;
        var endpoint = AiView.FindControl<TextBox>("EndpointBox")!;
        var model = AiView.FindControl<TextBox>("ModelBox")!;
        var key = AiView.FindControl<TextBox>("ApiKeyBox")!;
        var mode = AiView.FindControl<ComboBox>("ModePicker")!;
        int previousMode = mode.SelectedIndex;
        string? previousEndpoint = endpoint.Text, previousModel = model.Text, previousKey = key.Text, previousPrompt = prompt.Text;
        bool previousPanelVisibility = panel.IsVisible;
        string? previousStatus = Status.Text;

        bool copyEscaped = false;
        bool copied = true;
        try { copied = await AiView.CopyReplyAsync("controlled reply", _ => Task.FromException(new IOException("private clipboard provider path"))); }
        catch (IOException) { copyEscaped = true; }
        bool copyFailureHandled = !copyEscaped && !copied && Status.Text!.Contains("复制失败") && !Status.Text.Contains("private clipboard");
        if (!copyFailureHandled) failures.Add("A clipboard rejection escaped or lacked safe retry feedback.");
        string written = "";
        bool copyRetry = await AiView.CopyReplyAsync("controlled reply", text => { written = text; return Task.CompletedTask; });
        if (!copyRetry || written != "controlled reply") failures.Add("Reply copying could not recover after a provider failure.");

        int sends = 0;
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        AiView.SendFixture = (_, _) => { sends++; return Task.CompletedTask; };
        DesktopDialogs.Confirmation = (_, _) => confirmation.Task;
        try
        {
            AiView.PrefillPrompt("old reviewed log", log: true);
            Task sending = AiView.SendCurrentPromptAsync();
            AiView.PrefillPrompt("new log preview arriving while confirmation is open", log: true);
            confirmation.SetResult(true);
            await sending;
        }
        finally { DesktopDialogs.Confirmation = null; AiView.SendFixture = null; }
        bool changedPromptPreserved = sends == 0 && prompt.Text == "new log preview arriving while confirmation is open";
        if (!changedPromptPreserved) failures.Add("An old log approval sent or cleared a newer prompt.");
        bool approvedPromptSent;
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        AiView.SendFixture = (_, _) => { sends++; return Task.CompletedTask; };
        try
        {
            AiView.PrefillPrompt("unchanged reviewed log", log: true);
            await AiView.SendCurrentPromptAsync();
            approvedPromptSent = sends == 1 && string.IsNullOrEmpty(prompt.Text);
        }
        finally { DesktopDialogs.Confirmation = null; AiView.SendFixture = null; }
        if (!approvedPromptSent) failures.Add("An unchanged approved log could not be sent.");

        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mode.SelectedIndex = 1;
        endpoint.Text = "https://fixture.example/v1";
        model.Text = "unsaved-model";
        key.Text = "isolated-fixture-not-a-real-key";
        panel.IsVisible = true;
        AiView.SaveConfigFixture = (_, _) => saving.Task;
        bool locked;
        try
        {
            Task pending = AiView.SaveConfigurationAsync();
            locked = !panel.IsEnabled && IsWorking;
            saving.SetException(new IOException("controlled config save failure"));
            await pending;
        }
        finally { AiView.SaveConfigFixture = null; }
        bool saveFailurePreserved = panel.IsEnabled && panel.IsVisible && key.Text == "isolated-fixture-not-a-real-key"
            && model.Text == "unsaved-model" && endpoint.Text == "https://fixture.example/v1";
        if (!locked || !saveFailurePreserved) failures.Add("AI configuration saving left an editable pending form or lost its failed draft.");
        mode.SelectedIndex = previousMode;
        endpoint.Text = previousEndpoint;
        model.Text = previousModel;
        key.Text = previousKey;
        panel.IsVisible = previousPanelVisibility;
        AiView.PrefillPrompt(previousPrompt ?? "", log: false);
        SetStatus(previousStatus ?? "");
        File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-details.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, copyFailureHandled, copyRetry,
            changedPromptPreserved, approvedPromptSent, configLockedWhileSaving = locked, saveFailurePreserved,
            nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException(string.Join(" ", failures));
    }
}
