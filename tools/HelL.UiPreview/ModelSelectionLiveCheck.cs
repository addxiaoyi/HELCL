using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher.Ai.Local;
using HelL.Pages;
using HelL.Services;

internal static class ModelSelectionLiveCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_mode = "custom", ai_model = "unavailable-cloud", ai_base_url = "http://127.0.0.1:1/v1", ai_local_url = ModelRules.DefaultEndpoint } });
        window.Navigate("ai", instant: true);
        await Program.WaitForPage(window, "ai");
        var page = Walk(window).OfType<AiPage>().Single();
        var choices = await AppServices.Client.CallAsync<LocalModelStatus>("ai_local_model_choices") ?? throw new IOException("No local discovery response");
        var model = choices.Models.FirstOrDefault() ?? throw new IOException("No installed local model to verify");
        var pickerButton = Walk(page).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "选择模型");
        pickerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var popup = Read<Popup>(page, "_modelPopup");
        await Until(() => popup.IsOpen && Walk(popup.Child).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "使用本地模型 " + model.Name));
        App.ApplyTheme(true);
        window.UpdateLayout();
        Program.SaveSnapshot((FrameworkElement)popup.Child, Path.Combine(folder, "actual-local-picker.png"));
        Walk(popup.Child).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "使用本地模型 " + model.Name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !Read<bool>(page, "_switchingModel") && !popup.IsOpen);
        if (Read<TextBlock>(page, "_modelName").Text != model.Name || !Read<TextBlock>(page, "_sourceLabel").Text.StartsWith("本地")) throw new IOException("The header did not commit the selected local model");
        var input = Read<TextBox>(page, "_input");
        input.Text = "请只回答：本地模型已连接。";
        Read<Button>(page, "_send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Read<bool>(page, "_busy"));
        await Until(() => !Read<bool>(page, "_busy"), 3000);
        var store = await AppServices.Client.CallAsync("ai_list_chats");
        var messages = store.GetProperty("chats").EnumerateArray().SelectMany(chat => chat.GetProperty("messages").EnumerateArray()).ToList();
        if (messages.Any(message => message.GetProperty("role").GetString() == "error")) throw new IOException("Local chat returned an error: " + messages.Last().GetProperty("content").GetString());
        string answer = messages.LastOrDefault(message => message.GetProperty("role").GetString() == "assistant").GetProperty("content").GetString() ?? "";
        if (answer.Length == 0) throw new IOException("No response from the selected model");
        File.WriteAllText(Path.Combine(folder, "reply.txt"), answer);
        window.UpdateLayout();
        Program.SaveSnapshot(window, Path.Combine(folder, "actual-local-chat.png"));
        Console.WriteLine("[MODEL_PICKER_LIVE] UI selection and Send succeeded; model=" + model.Name + "; endpoint=" + choices.Endpoint + "; response_chars=" + answer.Length);
    }

    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static async Task Until(Func<bool> predicate, int attempts = 150)
    {
        for (int n = 0; n < attempts; n++) { if (predicate()) return; await Task.Delay(100); }
        throw new TimeoutException("Model selection or chat did not finish in time");
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        { var child = VisualTreeHelper.GetChild(root, n); yield return child; foreach (var next in Walk(child)) yield return next; }
    }
}
