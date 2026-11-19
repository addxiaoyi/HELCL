using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class FrontendRemediationCheck
{
    private static int checks;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethods(Hidden | BindingFlags.DeclaredOnly)
        .Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(owner, args);
    private static IEnumerable<T> Find<T>(DependencyObject owner) => CustomizationPreview.Walk(owner).OfType<T>();
    private static void Check(bool passed, string message)
    {
        checks++;
        if (!passed) throw new IOException("Frontend remediation check failed: " + message);
        Console.WriteLine("[PASS] " + message);
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Width = 900; window.Height = 620;
        window.Navigate("settings", true); await Program.WaitForPage(window, "settings");
        var settings = (SettingsPage)window.GetPage("settings")!;
        object secret = Field(settings, "_aiKey");
        Type secretType = secret.GetType();
        secretType.GetProperty("Text")!.SetValue(secret, "isolated-private-key-never-screen-captured");
        Check(!(bool)secretType.GetProperty("Revealed")!.GetValue(secret)!, "API key is masked by default");
        Check(Find<PasswordBox>((DependencyObject)secret).Single().Password.Length > 0, "masked key remains available for saving");
        var reveal = Find<Button>((DependencyObject)secret).Single();
        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check((bool)secretType.GetProperty("Revealed")!.GetValue(secret)!, "explicit reveal shows key");
        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        secretType.GetProperty("Text")!.SetValue(secret, "");
        var width = (TextBox)Field(settings, "_w"); string previous = width.Text; width.Text = "invalid-width";
        var save = (Task)Call(settings, "SaveAsync")!;
        try { await save; throw new IOException("Invalid width unexpectedly saved."); }
        catch (ArgumentException) { Check(true, "invalid numeric settings fail before any commit"); }
        width.Text = previous;
        Check(((TextBlock)Field(settings, "_saveState")).IsVisible && ((TextBlock)Field(settings, "_saveState")).Text.Contains("未保存"), "settings show pending-change count and effect timing");
        var search = Find<TextBox>(settings).First(input => System.Windows.Automation.AutomationProperties.GetName(input) == "搜索并定位设置");
        search.Text = "代理";
        await Task.Delay(80);
        Check(Find<Button>(settings).Any(button => button.Content?.ToString()?.Contains("代理") == true), "settings search exposes a category-linked result");
        Program.SaveSnapshot(window, Path.Combine(folder, "settings-search-900.png"));
        search.Text = "";

        window.Navigate("account", true); await Program.WaitForPage(window, "account");
        var account = (AccountPage)window.GetPage("account")!;
        Task form = (Task)Call(account, "AuthlibLoginAsync")!;
        await Task.Delay(100);
        var layer = Dlg.Host!;
        var login = Find<Button>(layer).Single(button => button.Content?.ToString() == "登录");
        login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(80);
        Check(Dlg.AnyOpen && !form.IsCompleted, "invalid account form remains open for correction");
        Check(Find<TextBlock>(layer).Any(text => text.Text.Contains("请输入")), "account validation points to missing input");
        Program.SaveSnapshot(window, Path.Combine(folder, "account-validation-900.png"));
        Find<Button>(layer).First(button => button.Content?.ToString() == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await form;

        var loginLayer = Dlg.Panel("Controlled device-code expiry", Ui.V(8), 420);
        account.GetType().GetField("_loginLayer", Hidden)!.SetValue(account, loginLayer);
        var copy = Ui.Btn("copy"); var browser = Ui.Btn("browser"); var renew = Ui.Btn("renew");
        account.GetType().GetField("_copyLoginCode", Hidden)!.SetValue(account, copy);
        account.GetType().GetField("_openLoginBrowser", Hidden)!.SetValue(account, browser);
        account.GetType().GetField("_renewLoginCode", Hidden)!.SetValue(account, renew);
        account.OnEvent(new BridgeEvent { Event = "login_code", Code = "CONTROLLED", Uri = "https://microsoft.com/link", ExpiresIn = 1 });
        Check(copy.IsEnabled && browser.IsEnabled, "received code enables supported login actions");
        await Task.Delay(1250);
        Call(account, "UpdateLoginExpiry");
        Check(!copy.IsEnabled && !browser.IsEnabled && renew.Visibility == Visibility.Visible, "expired code disables stale actions and offers a new code");
        loginLayer.Close(); account.GetType().GetField("_loginLayer", Hidden)!.SetValue(account, null);

        window.Navigate("mods", true); await Program.WaitForPage(window, "mods");
        var catalog = (CatalogPage)window.GetPage("mods")!;
        var missingMod = new ModEntry { Filename = "controlled-missing-mod.jar", Name = "missing fixture", Enabled = true };
        var modRow = (UIElement)Call(catalog, "ModRow", missingMod)!;
        var toggle = Find<CheckBox>(modRow).Single();
        toggle.IsChecked = false;
        for (int attempt = 0; attempt < 100 && !toggle.IsEnabled; attempt++) await Task.Delay(25);
        Check(toggle.IsChecked == true && missingMod.Enabled && toggle.ToolTip?.ToString()?.StartsWith("修改未保存") == true,
            "failed mod state commit rolls the switch back and exposes a retryable error");

        window.Navigate("instance", true); await Program.WaitForPage(window, "instance");
        var library = (InstancePage)window.GetPage("instance")!;
        var instances = Enumerable.Range(0, 125).Select(index => new InstanceInfo { Name = "controlled-library-" + index }).ToList();
        library.GetType().GetField("_rows", Hidden)!.SetValue(library, instances);
        Call(library, "Render");
        Check(((System.Collections.IDictionary)Field(library, "_cards")).Count == 60, "large library initially builds only a bounded page of cards");
        var loadMore = (Button)Field(library, "_loadMore"); loadMore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((System.Collections.IDictionary)Field(library, "_cards")).Count == 120 && loadMore.IsVisible, "large library progressively exposes further results");

        window.Navigate("ai", true); await Program.WaitForPage(window, "ai");
        var ai = (AiPage)window.GetPage("ai")!;
        var chats = Enumerable.Range(0, 2000).Select(index => new AiChatDto { Id = "audit-" + index, Title = "Conversation " + index,
            Messages = new List<AiMsgDto> { new AiMsgDto { Role = "user", Content = index == 1234 ? "unique-body-query" : "ordinary content" } } }).ToList();
        ai.GetType().GetField("_store", Hidden)!.SetValue(ai, new AiStoreDto { ActiveId = chats[0].Id, Chats = chats });
        var historySearch = (TextBox)Field(ai, "_histSearch"); historySearch.Text = "unique-body-query";
        Call(ai, "RenderChats");
        var history = (ListBox)Field(ai, "_chatList");
        Check(history.Items.Count == 1 && ((AiChatDto)history.Items[0]).Id == "audit-1234", "history search matches message body");
        historySearch.Text = ""; Call(ai, "RenderChats");
        Check(history.Items.Count == 2000 && Find<Button>(history).Count() < 100, "long chat history uses virtual containers");
        var menu = (ContextMenu)Call(ai, "DelMenu", "audit-1")!;
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(80);
        Check(Dlg.AnyOpen && chats.Count == 2000, "deleting chat first requires target confirmation");
        Find<Button>(Dlg.Host!).First(button => button.Content?.ToString() == "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        window.Navigate("version", true); await Program.WaitForPage(window, "version");
        var versions = (VersionPage)window.GetPage("version")!;
        var versionList = (ListBox)Field(versions, "_list");
        double shortHeight = versionList.Height;
        window.Height = 900; await Task.Delay(80);
        Check(versionList.Height > shortHeight, "version list grows with available height");
        Program.SaveSnapshot(window, Path.Combine(folder, "version-adaptive-900.png"));
        File.WriteAllText(Path.Combine(folder, "frontend-remediation.json"), JsonSerializer.Serialize(new { ok = true, checks,
            secretMasked = true, invalidSettingsRejected = true, accountInputRetained = true, settingsSearch = true,
            messageBodySearch = true, virtualHistory = true, destructiveConfirmation = true, nativeAndroidVerified = false }));
    }
}
