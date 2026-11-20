using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class WpfDetailCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        window.Activate();
        var failures = new List<string>();
        var checks = new List<string>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
            Console.WriteLine((passed ? "[PASS] " : "[FAIL] ") + label);
        }

        var password = Ui.Pw();
        Task<bool> passwordForm = Dlg.EditAsync("Controlled password editor", Ui.V(8, password), () =>
        {
            Dlg.Require(password.Password.Length > 0, password, "Enter the controlled password");
            return Task.CompletedTask;
        });
        await Settle(window);
        Check(password.IsKeyboardFocused, "a password-only editor initially focuses its input");
        Click("保存");
        await Settle(window);
        Check(FieldState.GetHasError(password), "password validation is attached to the invalid input");
        password.Password = "fixture-not-a-secret";
        Check(!FieldState.GetHasError(password), "editing a password clears its previous field error");
        Click("取消");
        await passwordForm;
        await Settle(window);

        var input = Ui.Input("Controlled draft");
        var saveGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> editor = Dlg.EditAsync("Controlled recovery editor", Ui.V(8, input), async () =>
        {
            Dlg.Require(input.Text.Length > 0, input, "Enter the controlled draft");
            await saveGate.Task;
        });
        await Settle(window);
        Click("保存");
        await Settle(window);
        Check(FieldState.GetHasError(input), "a raw login-style input receives its validation error");
        input.Text = "retained draft";
        Check(!FieldState.GetHasError(input), "editing a raw login-style input clears its previous field error");
        input.Focus();
        Controls<Button>().Single(button => button.Content?.ToString() == "保存").Focus();
        Click("保存");
        await Settle(window);
        Check(!input.IsEnabled && !editor.IsCompleted, "a pending form blocks editing and duplicate submission");
        saveGate.SetException(new IOException("Controlled save refusal"));
        await Settle(window);
        Check(!editor.IsCompleted && input.Text == "retained draft" && input.IsEnabled,
            "a refused asynchronous save retains the draft and enables recovery");
        Check(input.IsKeyboardFocused, "a refused asynchronous save restores the previous editor focus");
        Click("取消");
        await editor;
        await Settle(window);

        window.Navigate("account", true);
        await Program.WaitForPage(window, "account");
        var account = (AccountPage)window.GetPage("account")!;
        BridgeClient original = AppServices.Client;
        using var requests = new LoginRequests();
        using var client = new BridgeClient(new Uri("http://127.0.0.1:1"), new string('x', 32));
        var transport = new HttpClient(requests) { BaseAddress = client.BaseUri };
        ((HttpClient)typeof(BridgeClient).GetField("_http", Hidden)!.GetValue(client)!).Dispose();
        typeof(BridgeClient).GetField("_http", Hidden)!.SetValue(client, transport);
        try
        {
            AppServices.Client = client;
            Task login = (Task)typeof(AccountPage).GetMethod("Nide8LoginAsync", Hidden)!.Invoke(account, null)!;
            await Settle(window);
            var fields = Controls<TextBox>().ToArray();
            fields[0].Text = "invalid-server-id";
            fields[1].Text = "fixture-user";
            Controls<PasswordBox>().Single().Password = "fixture-password";
            Click("登录");
            await Settle(window);
            Check(!login.IsCompleted && Dlg.AnyOpen, "a malformed unified-pass ID keeps its login form open");
            Check(requests.Submissions == 0, "a malformed unified-pass ID never submits a login task");
            Check(FieldState.GetHasError(fields[0]) && fields[0].IsKeyboardFocused,
                "the invalid server ID gets a focused field error");
            if (!login.IsCompleted)
            {
                Check(fields[1].Text == "fixture-user" && Controls<PasswordBox>().Single().Password == "fixture-password",
                    "invalid server ID retains the entered account fields");
                Program.SaveSnapshot(window, Path.Combine(folder, "nide8-field-validation.png"));
                fields[0].Text = "https://auth.mc-user.com:233/0123456789ABCDEF0123456789ABCDEF";
                Check(!FieldState.GetHasError(fields[0]), "correcting a server ID clears the stale validation marker");
                Click("登录");
                await login;
                await Settle(window);
                Check(requests.Submissions == 1 && requests.ServerId == "0123456789abcdef0123456789abcdef",
                    "a valid unified-pass link submits its canonical server ID once");
            }
            else await login;
        }
        finally
        {
            AppServices.Client = original;
            typeof(AccountPage).GetField("_nide8Task", Hidden)!.SetValue(account, null);
        }

        File.WriteAllText(Path.Combine(folder, "wpf-details.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks, realNetworkLogin = false,
            nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
    }

    private static IEnumerable<T> Controls<T>() => CustomizationPreview.Walk(Dlg.Host!).OfType<T>();
    private static void Click(string label) => Controls<Button>().Single(button => button.Content?.ToString() == label)
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Settle(MainWindow window)
    {
        window.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(80);
        window.UpdateLayout();
    }

    private sealed class LoginRequests : HttpMessageHandler
    {
        internal int Submissions { get; private set; }
        internal string ServerId { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            using var message = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string method = message.RootElement.GetProperty("method").GetString()!;
            if (method != "start_nide8_login") throw new IOException("Unexpected controlled RPC: " + method);
            Submissions++;
            ServerId = message.RootElement.GetProperty("params").GetProperty("server_id").GetString()!;
            string id = message.RootElement.GetProperty("id").GetRawText();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":\"controlled-task\"}", Encoding.UTF8, "application/json")
            };
        }
    }
}
