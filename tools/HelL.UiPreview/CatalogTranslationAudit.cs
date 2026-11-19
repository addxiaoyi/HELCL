using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class CatalogTranslationAudit
{
    internal static async Task Live(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = "http://127.0.0.1:11434" } });
        var project = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "3d-crops" })
            ?? throw new InvalidOperationException("Live translation project is unavailable");
        var reply = await AppServices.Client.CallAsync<System.Text.Json.JsonElement>("catalog_summaries", new { projects = new[] { project } });
        File.WriteAllText(Path.Combine(folder, "translation.json"), reply.GetRawText());
        if (reply.GetProperty("error").GetString() is string error && error.Length > 0) throw new InvalidOperationException(error);
        var translated = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<CatalogItem>>(reply.GetProperty("rows").GetRawText(), BridgeClient.JsonOpt)!.Single();
        if (translated.Name != project.Name || !HelL.Core.Launcher.ResourceNames.ContainsChinese(translated.Description) ||
            !HelL.Core.Launcher.ResourceNames.ContainsChinese(translated.ChineseName)) throw new InvalidOperationException("Local model did not return a usable Chinese title and summary");
        var searches = await AppServices.Client.CallAsync<System.Collections.Generic.List<CatalogItem>>("search_resourcepacks", new { query = translated.ChineseName, source = "Modrinth" });
        if (searches == null || !searches.Any(row => row.Slug == project.Slug)) throw new InvalidOperationException("Learned Chinese name did not find the original project");
        window.Navigate("resourcepack", instant: true); await Program.WaitForPage(window, "resourcepack");
        var page = RichUiAudit.Find<CatalogPage>(window).Single();
        RichUiAudit.Field<ComboBox>(page, "_source").SelectedItem = "Modrinth";
        RichUiAudit.Field<TextBox>(page, "_search").Text = translated.ChineseName;
        RichUiAudit.Field<System.Windows.Threading.DispatcherTimer>(page, "_debounce").Stop();
        await (Task)RichUiAudit.Invoke(page, "SearchAsync")!;
        window.Width = 800;
        await MaterialControlsPreview.Layout(window);
        await MaterialControlsPreview.Until(() => RichUiAudit.Find<ProjectCover>(page).Any(cover => cover.Ready), "translated project artwork loaded", 25);
        RichUiAudit.Shot(window, folder, "live-translated-search");
        Console.WriteLine("[LOCAL TRANSLATION PASS] " + translated.Name + " => " + translated.ChineseName + "; Chinese summary cached and Chinese query found " + project.Slug);
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        var page = RichUiAudit.Find<CatalogPage>(window).Single();
        await MaterialControlsPreview.Until(() => !RichUiAudit.Field<bool>(page, "_summaryBusy"), "previous translation settled", 25);
        using var server = new EmptyModels();
        await AppServices.Client.CallAsync("save_settings", new { data = new { ai_local_url = server.Endpoint } });
        var projects = Enumerable.Range(1, 12).Select(index => new CatalogItem {
            Name = "Translation fixture " + index, Kind = "world", OriginalDescription = "Original description remains readable.",
            Localization = "英文简介待翻译"
        }).ToList();
        RichUiAudit.Invoke(page, "ShowRows", projects);
        await MaterialControlsPreview.Until(() => server.Requests > 0 && RichUiAudit.Field<TextBlock>(page, "_summaryProgress").Text.Contains("已暂停") &&
            !RichUiAudit.Field<bool>(page, "_summaryBusy"), "missing model pauses the queue", 12);
        if (server.Requests != 1) throw new InvalidOperationException("Missing model retried automatically: " + server.Requests);
        if (RichUiAudit.Find<ResourceSummary>(page).SelectMany(RichUiAudit.Find<ProgressBar>).Any(bar => bar.IsVisible))
            throw new InvalidOperationException("Translation failure left a spinning row");
        var summary = RichUiAudit.Find<ResourceSummary>(page).First();
        var retry = RichUiAudit.Find<Button>(summary).Single(button => button.Content as string == "重试翻译");
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await MaterialControlsPreview.Until(() => server.Requests == 2 && !RichUiAudit.Field<bool>(page, "_summaryBusy"), "manual translation retry completes", 12);
        RichUiAudit.Find<Button>(summary).Single(button => button.Content as string == "原文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!summary.ShowingOriginal || !RichUiAudit.Find<TextBlock>(summary).Any(text => text.Text == projects[0].OriginalDescription))
            throw new InvalidOperationException("Original description disappeared after translation failure");
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, "translation-failure-fixture");
        await (Task)RichUiAudit.Invoke(page, "ShowLanguageIndex")!;
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, "language-index");
        CatalogChinesePreview.CloseDialog(window);
        Console.WriteLine("[TRANSLATION UI PASS] Missing model: one request, no stuck spinner; manual retry and original text verified");
    }

    private sealed class EmptyModels : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _server;
        private bool _closed;
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public string Endpoint { get; }
        internal EmptyModels()
        {
            _listener.Start(); Endpoint = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _server = Serve();
        }
        private async Task Serve()
        {
            while (!_closed)
            {
                TcpClient peer;
                try { peer = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (SocketException) when (_closed) { return; }
                catch (ObjectDisposedException) { return; }
                using (peer)
                using (var stream = peer.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    while (await reader.ReadLineAsync().ConfigureAwait(false) is string line && line.Length > 0) { }
                    Interlocked.Increment(ref _requests);
                    byte[] bytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: 13\r\n\r\n{\"models\":[]}");
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
        }
        public void Dispose() { _closed = true; _listener.Stop(); _server.GetAwaiter().GetResult(); }
    }
}
