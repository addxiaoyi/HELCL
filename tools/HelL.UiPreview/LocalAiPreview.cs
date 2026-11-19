using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher.Ai.Local;
using HelL.Pages;
using HelL.Pages.Ai;
using HelL.Services;

internal static class LocalAiPreview
{
    private static int _passed;

    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        using var server = new ModelFixture();
        string cache = Path.Combine(HelL.Core.Launcher.Utils.ROOT, "ai-local", "downloads");
        Directory.CreateDirectory(cache);
        string cacheFile = Path.Combine(cache, new string('a', 64) + ".gguf");
        string partialCache = Path.Combine(cache, new string('b', 64) + ".gguf.partial");
        File.WriteAllText(cacheFile, "isolated cache fixture");
        File.WriteAllText(partialCache, "preserve partial fixture");
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = server.Address, ai_api_key = "isolated-cloud-key", ai_base_url = server.Address + "/v1", ai_model = "cloud-fixture" } });
        window.Navigate("ai", instant: true);
        await Program.WaitForPage(window, "ai");
        var page = Descendants(window).OfType<AiPage>().Single();
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:1" } });
        await AppServices.Client.CallAsync("ai_local_select", new { model = "qwen3:0.6b", endpoint = server.Address });
        var endpointSettings = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(endpointSettings!["ai_local_url"].GetString() == server.Address && endpointSettings["ai_mode"].GetString() == "local", "Choosing an observed local model commits its endpoint and mode together");
        await AppServices.Client.CallAsync("ai_cloud_select", new { model = "cloud-fixture" });
        await page.RefreshAsync();
        Check(Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "选择模型"),
            "Chat exposes one picker for local and cloud models");
        await Layout(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "chat-empty-light.png"));
        Click(Named(page, "选择模型"));
        var modelPopup = Read<Popup>(page, "_modelPopup");
        await Until(() => modelPopup.IsOpen && Descendants(modelPopup.Child).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "使用本地模型 qwen3:0.6b"), "Local models appear while cloud is selected");
        await Layout(window);
        Program.SaveSnapshot((FrameworkElement)modelPopup.Child, Path.Combine(folder, "picker-light.png"));
        Click(Named(modelPopup.Child, "使用本地模型 qwen3:0.6b"));
        await Until(() => !Read<bool>(page, "_switchingModel") && !modelPopup.IsOpen, "Selecting local model commits");
        var picked = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(picked!["ai_mode"].GetString() == "local" && picked["ai_local_model"].GetString() == "qwen3:0.6b", "Choosing a local row changes the actual saved connection");
        Check(Read<TextBlock>(page, "_modelName").Text == "qwen3:0.6b" && Read<TextBlock>(page, "_sourceLabel").Text.StartsWith("本地"), "Header shows the committed local selection");
        await page.RefreshAsync();
        Check(Read<TextBlock>(page, "_modelName").Text == "qwen3:0.6b", "Refresh cannot restore the old cloud model over local selection");
        Click(Named(page, "选择模型"));
        await Until(() => modelPopup.IsOpen && Descendants(modelPopup.Child).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "使用云端模型 cloud-fixture"), "Cloud remains selectable from local mode");
        Click(Named(modelPopup.Child, "使用云端模型 cloud-fixture"));
        await Until(() => !Read<bool>(page, "_switchingModel") && !modelPopup.IsOpen, "Returning to cloud commits");
        picked = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(picked!["ai_mode"].GetString() == "custom" && picked["ai_model"].GetString() == "cloud-fixture", "The same picker restores the cloud provider");
        Click(Named(page, "选择模型"));
        await Until(() => modelPopup.IsOpen && Descendants(modelPopup.Child).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "使用本地模型 qwen3:0.6b"), "Local model becomes selectable again");
        server.RejectSelection = true;
        Click(Named(modelPopup.Child, "使用本地模型 qwen3:0.6b"));
        await Until(() => !Read<bool>(page, "_switchingModel"), "Rejected model choice settles");
        Check(modelPopup.IsOpen && Read<TextBlock>(page, "_modelName").Text == "cloud-fixture", "A failed selection keeps the previous model and the picker open");
        var picker = Read<ModelPicker>(page, "_modelPicker");
        Check(Read<TextBlock>(picker, "_error").Visibility == Visibility.Visible, "Failed model choice has an inline error");
        server.RejectSelection = false;
        Click(Named(modelPopup.Child, "使用本地模型 qwen3:0.6b"));
        await Until(() => !Read<bool>(page, "_switchingModel") && !modelPopup.IsOpen, "The failed local choice can be retried");
        await AppServices.Client.CallAsync("ai_cloud_select", new { model = "cloud-fixture" });
        await page.RefreshAsync();
        Click(Named(page, "选择模型"));
        await Until(() => modelPopup.IsOpen, "Picker reopens for keyboard dismissal");
        modelPopup.Child.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(modelPopup.Child), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(!modelPopup.IsOpen, "Escape closes the model picker without changing the selection");
        Click(Named(page, "选择模型"));
        await Until(() => modelPopup.IsOpen, "Model picker exposes the library entry");
        Click(Named(modelPopup.Child, "管理模型与连接"));
        await Until(() => Descendants(page).OfType<LocalModelsPanel>().Any(panel => panel.IsVisible), "Model library opens");
        var library = Descendants(page).OfType<LocalModelsPanel>().Single();
        await Until(() => !Read<bool>(library, "_loading"), "Model discovery settles");
        Check(Descendants(library).OfType<TextBlock>().Any(text => text.Text == "qwen3:0.6b"), "Existing Ollama models are discovered through the bridge");
        Check(!Descendants(library).OfType<TextBlock>().Any(text => text.Text == "remote-alias"), "Cloud aliases are excluded from installed local models");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (var size in new[] { (1180d, 740d), (900d, 580d) })
            {
                window.Width = size.Item1; window.Height = size.Item2;
                foreach (var tab in new[] { ("已安装", "installed"), ("推荐下载", "catalog"), ("上游仓库", "hub"), ("下载队列", "queue"), ("存储", "storage"), ("设置", "settings") })
                {
                    Click(Named(library, tab.Item1));
                    await Until(() => !Read<bool>(library, "_loading"), "Tab " + tab.Item2 + " settles");
                    Check(Read<TextBlock>(library, "_feedback").Visibility == Visibility.Collapsed,
                        "Tab opens without errors: " + tab.Item2);
                    await Layout(window);
                    Program.SaveSnapshot(window, Path.Combine(folder, tab.Item2 + "-" + (dark ? "dark" : "light") + "-" + size.Item1 + ".png"));
                    CheckVisibleButtons(library, size.Item1 + " " + tab.Item2);
                    var scroll = Descendants(library).OfType<ScrollViewer>().FirstOrDefault(view => view.IsVisible && view.Content is SPanel);
                    Check(scroll != null && scroll.ActualHeight > 100, "Library list keeps usable height " + size.Item1 + " " + tab.Item2);
                }
            }
        }
        // Returning to Hub must reuse its controls without a logical-parent exception.
        Click(Named(library, "已安装"));
        await Until(() => !Read<bool>(library, "_loading"), "Installed tab returns");
        Click(Named(library, "上游仓库"));
        await Until(() => !Read<bool>(library, "_loading"), "Hub can reopen");
        Check(Read<string>(library, "_tab") == "hub", "Repository controls survive tab changes");

        Click(Named(library, "设置"));
        await Until(() => !Read<bool>(library, "_loading"), "Local settings opens");
        var endpointInput = Read<TextBox>(library, "_endpoint");
        endpointInput.Text = "https://example.com";
        Click(Named(library, "保存并检测"));
        await Until(() => !Read<bool>(library, "_loading"), "Invalid endpoint validation settles");
        Check(Read<TextBlock>(library, "_feedback").IsVisible && endpointInput.Text == "https://example.com",
            "Invalid endpoint keeps the input and shows an inline error");
        await Layout(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "settings-invalid-dark-900.png"));
        endpointInput.Text = server.Address;
        Click(Named(library, "保存并检测"));
        await Until(() => !Read<bool>(library, "_loading"), "Corrected endpoint reconnects");
        Check(Read<TextBlock>(library, "_feedback").Text.Contains("已连接"), "Corrected connection gives persistent success feedback");
        var sourceBox = Read<ComboBox>(library, "_settingsSource");
        sourceBox.SelectedIndex = 1;
        Click(Named(library, "保存模型来源"));
        await Until(() => !Read<bool>(library, "_loading"), "Source saves");
        Check(Read<TextBlock>(library, "_feedback").Text.Contains("已保存"), "Model source save gives persistent feedback");
        Check(Read<ComboBox>(library, "_source").SelectedIndex == 1, "Saved source is used by repository search");
        var settingsScroll = Descendants(library).OfType<ScrollViewer>().First(view => view.IsVisible && view.Content is SPanel);
        Button saveSource = Named(library, "保存模型来源");
        saveSource.BringIntoView();
        await Layout(window);
        Point sourcePosition = saveSource.TranslatePoint(new Point(), settingsScroll);
        Check(sourcePosition.Y >= 0 && sourcePosition.Y + saveSource.ActualHeight <= settingsScroll.ActualHeight + 1,
            "Model source save remains reachable by scrolling in a narrow window");
        Program.SaveSnapshot(window, Path.Combine(folder, "settings-source-dark-900.png"));

        Click(Named(library, "存储"));
        await Until(() => !Read<bool>(library, "_loading"), "Storage opens");
        var cacheCheckbox = Descendants(library).OfType<CheckBox>().Single(box => box.IsVisible && (box.Content as string ?? "").StartsWith("完整缓存", StringComparison.Ordinal));
        cacheCheckbox.IsChecked = true;
        Click(Named(library, "清理所选缓存"));
        await Until(() => Dlg.AnyOpen, "Cache confirmation opens");
        Check(File.Exists(cacheFile), "Cache cleanup waits for confirmation");
        Click(Named(Dlg.Host!, "清理"));
        await Until(() => !Read<bool>(library, "_loading"), "Cache cleanup finishes");
        Check(!File.Exists(cacheFile) && File.Exists(partialCache), "Cache cleanup removes only the selected file");

        server.HoldPull = true;
        var queuedFirst = await AppServices.Client.CallAsync<ModelJob>("ai_local_pull", new { model = "qwen3:8b" });
        var queuedSecond = await AppServices.Client.CallAsync<ModelJob>("ai_local_pull", new { model = "qwen3:14b" });
        Click(Named(library, "下载队列"));
        await Until(() => !Read<bool>(library, "_loading"), "Queue opens");
        await Layout(window);
        Program.SaveSnapshot(window, Path.Combine(folder, "queue-active-dark-900.png"));
        Check(Descendants(library).OfType<TextBlock>().Any(text => text.Text.Contains("qwen3:14b")), "Queued models are visible while another download is running");
        await AppServices.Client.CallAsync("ai_local_cancel", new { id = queuedSecond!.Id });
        await AppServices.Client.CallAsync("ai_local_cancel", new { id = queuedFirst!.Id });
        await UntilJob("cancelled");
        server.HoldPull = false;

        await AppServices.Client.CallAsync("ai_local_pull", new { model = "qwen3:4b" });
        await UntilJob("running");
        await AppServices.Client.CallAsync("ai_local_cancel");
        await UntilJob("cancelled");
        Check(true, "Cancelling a streaming download reaches cancelled state");
        await AppServices.Client.CallAsync("ai_local_pull", new { model = "qwen3:4b" });
        await UntilJob("completed");
        Check(true, "The same download can be retried after cancellation");
        await AppServices.Client.CallAsync("ai_local_load", new { model = "qwen3:0.6b" });
        await UntilJob("completed");
        await AppServices.Client.CallAsync("ai_local_load", new { model = "qwen3:0.6b", unload = true });
        await UntilJob("completed");
        Check(true, "Load and unload complete through the native API");

        await AppServices.Client.CallAsync("ai_local_select", new { model = "qwen3:0.6b" });
        await AppServices.Client.CallAsync("ai_local_tool_mode", new { enabled = true });
        var toolSettings = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(toolSettings!["ai_local_tools"].GetBoolean(), "A capable model can explicitly enable local tool mode");
        await AppServices.Client.CallAsync("ai_local_tool_mode", new { enabled = false });
        var settings = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(settings!["ai_mode"].GetString() == "local" && settings["ai_model"].GetString() == "cloud-fixture", "Local selection preserves the cloud model setting");
        window.Navigate("settings", instant: true);
        await Program.WaitForPage(window, "settings");
        var settingsPage = Descendants(window).OfType<SettingsPage>().Single();
        await settingsPage.RefreshAsync();
        var modeBox = Read<ComboBox>(settingsPage, "_aiMode");
        var modelBox = Read<ComboBox>(settingsPage, "_aiModel");
        Check(modelBox.Text == "qwen3:0.6b" && !modelBox.IsEnabled, "Settings displays the selected local model without making it a cloud setting");
        typeof(SettingsPage).GetMethod("SelectKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { modeBox, "custom" });
        Check(modelBox.Text == "cloud-fixture", "Switching Settings to cloud restores its own model name");
        typeof(SettingsPage).GetMethod("SelectKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { modeBox, "local" });
        Check(modelBox.Text == "qwen3:0.6b", "Switching Settings back preserves the local model name");
        window.Navigate("ai", instant: true);
        await Program.WaitForPage(window, "ai");
        await page.RefreshAsync();
        Click(Named(library, "返回对话"));
        await Layout(window);
        await AppServices.Client.CallAsync("ai_send", new { text = "你好，介绍一下你自己", chat_id = (string?)null });
        await UntilAsync(async () => { var store = await AppServices.Client.CallAsync("ai_list_chats"); return !store.GetProperty("busy").GetBoolean(); }, "Native local chat finishes");
        await page.RefreshAsync();
        Check(server.ChatCount == 1 && server.AuthHeaders == 0, "Chat uses the local native endpoint without cloud credentials");
        var storeAfter = await AppServices.Client.CallAsync("ai_list_chats");
        Check(storeAfter.GetRawText().Contains("本地测试回复"), "Streamed local response is persisted in chat history");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (double width in new[] { 1180d, 900d })
            {
                window.Width = width; window.Height = width == 900 ? 580 : 740;
                await Layout(window);
                Program.SaveSnapshot(window, Path.Combine(folder, "chat-" + (dark ? "dark" : "light") + "-" + width + ".png"));
            }
        }
        await AppServices.Client.CallAsync("ai_local_cloud");
        settings = await AppServices.Client.CallAsync<Dictionary<string, JsonElement>>("get_settings");
        Check(settings!["ai_mode"].GetString() == "custom" && settings["ai_model"].GetString() == "cloud-fixture", "Switching back restores the prior cloud connection");
        await AppServices.Client.CallAsync("ai_local_select", new { model = "qwen3:0.6b" });
        await AppServices.Client.CallAsync("ai_local_tool_mode", new { enabled = true });
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_permission_mode = "yolo" } });
        foreach (bool allow in new[] { false, true })
        {
            string instance = allow ? "local-tools-approved" : "local-tools-denied";
            server.ToolInstance = instance;
            await AppServices.Client.CallAsync("ai_new_chat");
            await AppServices.Client.CallAsync("ai_send", new { text = "请创建测试实例 " + instance });
            await UntilAsync(async () =>
            {
                var store = await AppServices.Client.CallAsync("ai_list_chats");
                return store.TryGetProperty("pending_card", out var card) && card.ValueKind == JsonValueKind.Object && card.TryGetProperty("kind", out var kind) && kind.GetString() == "confirm";
            }, "Local tool waits for permission");
            var beforeInstances = await AppServices.Client.CallAsync("get_instances");
            Check(!beforeInstances.GetRawText().Contains(instance), "No local tool executes before confirmation");
            await page.RefreshAsync();
            await Layout(window);
            Program.SaveSnapshot(window, Path.Combine(folder, allow ? "tools-approve.png" : "tools-deny.png"));
            if (allow)
            {
                App.ApplyTheme(false);
                await Layout(window);
                Program.SaveSnapshot(window, Path.Combine(folder, "tools-approve-light.png"));
                App.ApplyTheme(true);
                await Layout(window);
            }
            await AppServices.Client.CallAsync("ai_confirm", new { ok = allow });
            await UntilAsync(async () => !(await AppServices.Client.CallAsync("ai_list_chats")).GetProperty("busy").GetBoolean(), "Tool round finishes");
            var afterInstances = await AppServices.Client.CallAsync("get_instances");
            Check(afterInstances.GetRawText().Contains(instance) == allow, allow ? "Approved local write executes" : "Rejected local write does not execute");
        }
        await AppServices.Client.CallAsync("ai_local_delete", new { model = "qwen3:0.6b" });
        Check(server.DeleteCount == 1, "Model deletion uses Ollama DELETE instead of deleting arbitrary files");
        Console.WriteLine("[AI_PREVIEW] " + _passed + " checks passed. Fixture HTTP responses, not real model inference.");
    }

    private static async Task UntilJob(string state) => await UntilAsync(async () => (await AppServices.Client.CallAsync<ModelJob>("ai_local_job"))?.State == state, "Job reaches " + state);
    private static async Task Until(Func<bool> predicate, string message) => await UntilAsync(() => Task.FromResult(predicate()), message);
    private static async Task UntilAsync(Func<Task<bool>> predicate, string message)
    {
        for (int n = 0; n < 100; n++) { if (await predicate()) return; await Task.Delay(100); }
        throw new InvalidOperationException(message + " timed out");
    }
    private static async Task Layout(Window window) { await Task.Delay(180); window.UpdateLayout(); }
    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Named(DependencyObject root, string name) => Descendants(root).OfType<Button>().First(button => button.IsVisible && (AutomationProperties.GetName(button) == name || button.Content as string == name));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[PASS] " + message); }
    private static void CheckVisibleButtons(FrameworkElement root, string label)
    {
        foreach (var button in Descendants(root).OfType<Button>().Where(button => button.IsVisible && button.ActualWidth > 0))
        {
            var point = button.TranslatePoint(new Point(), root);
            Check(point.X >= -1 && point.X + button.ActualWidth <= root.ActualWidth + 1, "Button fits horizontally: " + label + " " + AutomationProperties.GetName(button));
        }
    }

    private sealed class ModelFixture : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        public string Address { get; }
        public int ChatCount;
        public int AuthHeaders;
        public int DeleteCount;
        public string ToolInstance = "";
        public volatile bool HoldPull;
        public volatile bool RejectSelection;
        public ModelFixture()
        {
            _listener.Start(); int number = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Address = "http://127.0.0.1:" + number;
            _ = Task.Run(Serve);
        }
        private async Task Serve()
        {
            while (!_cancel.IsCancellationRequested)
            {
                TcpClient connection;
                try { connection = await _listener.AcceptTcpClientAsync(); }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { return; }
                _ = Task.Run(() => Respond(connection));
            }
        }
        private async Task Respond(TcpClient connection)
        {
            try
            {
                using var stream = connection.GetStream();
                var header = new List<byte>();
                var single = new byte[1];
                while (header.Count < 16384)
                {
                    if (await stream.ReadAsync(single, 0, 1) == 0) return;
                    header.Add(single[0]);
                    int n = header.Count;
                    if (n >= 4 && header[n-4] == 13 && header[n-3] == 10 && header[n-2] == 13 && header[n-1] == 10) break;
                }
                string[] lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                bool authorized = lines.Any(line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
                int length = 0;
                string? lengthLine = lines.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                if (lengthLine != null) length = int.Parse(lengthLine.Substring(15).Trim());
                if (length > 1000000) throw new IOException("Fixture request is too large");
                if (lines.Any(line => line.StartsWith("Expect: 100-continue", StringComparison.OrdinalIgnoreCase)))
                { byte[] interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); await stream.WriteAsync(interim, 0, interim.Length); }
                var bytes = new byte[length];
                int offset = 0;
                while (offset < bytes.Length) { int n = await stream.ReadAsync(bytes, offset, bytes.Length - offset); if (n == 0) return; offset += n; }
                string body = Encoding.UTF8.GetString(bytes);
                string path = lines[0].Split(' ')[1];
                if (authorized && path.StartsWith("/api/", StringComparison.Ordinal)) Interlocked.Increment(ref AuthHeaders);
                if (path == "/api/delete") { if (!lines[0].StartsWith("DELETE ", StringComparison.Ordinal)) throw new IOException("Wrong delete method"); Interlocked.Increment(ref DeleteCount); }
                byte[] responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(responseHeader, 0, responseHeader.Length);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                if (path == "/api/pull")
                {
                    while (HoldPull)
                    {
                        await writer.WriteLineAsync("{\"status\":\"pulling fixture layer\",\"total\":1200,\"completed\":100}");
                        await writer.FlushAsync(); await Task.Delay(100);
                    }
                    for (int n = 0; n < 12; n++) { await writer.WriteLineAsync("{\"status\":\"pulling test layer\",\"total\":1200,\"completed\":" + n * 100 + "}"); await writer.FlushAsync(); await Task.Delay(100); }
                    await writer.WriteLineAsync("{\"status\":\"success\"}");
                }
                else if (path == "/api/chat")
                {
                    Interlocked.Increment(ref ChatCount);
                    if (body.Contains("isolated-cloud-key")) throw new InvalidOperationException("Cloud key leaked to chat");
                    using var payload = JsonDocument.Parse(body);
                    bool tools = payload.RootElement.TryGetProperty("tools", out var schemas) && schemas.GetArrayLength() > 0 && ToolInstance.Length > 0;
                    bool hasTool = payload.RootElement.GetProperty("messages").EnumerateArray().Any(message => message.GetProperty("role").GetString() == "tool");
                    if (tools && !hasTool)
                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { message = new { role = "assistant", content = "", tool_calls = new[] { new { function = new { name = "create_instance", arguments = new { name = ToolInstance } } } } }, done = false }));
                    else await writer.WriteLineAsync("{\"message\":{\"role\":\"assistant\",\"content\":\"本地测试回复：这是一条隔离服务返回的流式消息。\"},\"done\":false}");
                    await writer.WriteLineAsync("{\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":30,\"eval_count\":20}");
                }
                else
                {
                    string json = path switch {
                        "/api/version" => "{\"version\":\"fixture\"}",
                        "/api/tags" => "{\"models\":[{\"name\":\"qwen3:0.6b\",\"size\":523000000,\"details\":{\"parameter_size\":\"0.6B\",\"quantization_level\":\"Q4_K_M\"}},{\"name\":\"remote-alias\",\"remote_model\":\"cloud\"}]}",
                        "/api/ps" => "{\"models\":[]}",
                        "/api/show" => RejectSelection ? "{\"remote_model\":\"cloud-only\"}" : "{\"capabilities\":[\"completion\",\"tools\"]}",
                        "/api/generate" => "{\"done\":true}",
                        "/api/delete" => "{}",
                        "/v1/models" => "{\"data\":[{\"id\":\"cloud-fixture\"}]}",
                        _ => "{\"error\":\"unknown fixture endpoint\"}" };
                    await writer.WriteLineAsync(json);
                }
                await writer.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException) { Console.WriteLine("[FIXTURE] Client disconnected: " + ex.GetType().Name); }
            finally { connection.Close(); }
        }
        public void Dispose() { _cancel.Cancel(); _listener.Stop(); _cancel.Dispose(); }
    }
}
