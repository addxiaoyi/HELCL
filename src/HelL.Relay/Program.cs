using HelL.Relay;

if (args is ["--healthcheck"])
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using HttpResponseMessage response = await http.GetAsync("http://127.0.0.1:8080/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

await RelayApp.Build(args).RunAsync();
