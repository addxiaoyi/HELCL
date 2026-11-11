using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class AuthenticationFlowTests : IDisposable
{
	private readonly TestHome home = new TestHome();
	public void Dispose() => home.Dispose();

	private static PyDict Account() => PyDict.Of("type", "microsoft", "name", "Player", "uuid", "8667ba71-b85a-4004-af54-457a9734eed7",
		"access_token", "old-test-token", "refresh_token", "test-refresh", "expires_at", 0L);

	private sealed class Replay : IDisposable
	{
		private readonly HttpListener listener = new HttpListener();
		private readonly string? previous = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
		private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
		private readonly Task server;
		public Dictionary<string, int> Calls { get; } = new Dictionary<string, int>();
		public Func<string, int, (int Status, string Body, int? RetryAfter)>? Reply;

		public Replay()
		{
			var port = new TcpListener(IPAddress.Loopback, 0);
			port.Start();
			string address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port + "/";
			port.Stop();
			listener.Prefixes.Add(address);
			listener.Start();
			Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
			Config.CONFIG.data["use_system_proxy"] = false;
			server = Serve();
		}

		private async Task Serve()
		{
			while (!shutdown.IsCancellationRequested)
			{
				HttpListenerContext context;
				// Windows can leave a synchronous accept blocked after HttpListener.Stop().
				try { context = await listener.GetContextAsync().WaitAsync(shutdown.Token).ConfigureAwait(false); }
				catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return; }
				catch (HttpListenerException) when (shutdown.IsCancellationRequested) { return; }
				catch (ObjectDisposedException) when (shutdown.IsCancellationRequested) { return; }
				try
				{
					string? raw = context.Request.Headers["X-HelL-Replay-Url"];
					if (string.IsNullOrEmpty(raw))
					{
						continue;
					}
					string path = new Uri(raw).AbsolutePath;
					Calls.TryGetValue(path, out int count);
					Calls[path] = ++count;
					var reply = Reply?.Invoke(path, count) ?? Success(path);
					byte[] body = Encoding.UTF8.GetBytes(reply.Body);
					context.Response.StatusCode = reply.Status;
					context.Response.ContentType = "application/json";
					context.Response.ContentLength64 = body.Length;
					if (reply.RetryAfter.HasValue)
						context.Response.Headers["Retry-After"] = reply.RetryAfter.Value.ToString();
					context.Response.OutputStream.Write(body);
				}
				finally
				{
					context.Response.Close();
				}
			}
		}

		public static (int Status, string Body, int? RetryAfter) Success(string path) => (200, path switch
		{
			"/oauth20_connect.srf" => "{\"device_code\":\"test-device\",\"user_code\":\"TEST-CODE\",\"verification_uri\":\"https://example.com/verify\",\"expires_in\":900,\"interval\":1}",
			"/oauth20_token.srf" => "{\"access_token\":\"test-ms-token\",\"refresh_token\":\"rotated-test-refresh\"}",
			"/user/authenticate" => "{\"Token\":\"test-xbl\"}",
			"/xsts/authorize" => "{\"Token\":\"test-xsts\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"1234\"}]}}",
			"/authentication/login_with_xbox" => "{\"access_token\":\"test-minecraft\",\"expires_in\":120}",
			"/entitlements/mcstore" => "{\"items\":[{\"name\":\"game_minecraft\"}]}",
			"/minecraft/profile" => "{\"name\":\"Player\",\"id\":\"8667ba71b85a4004af54457a9734eed7\"}",
			_ => throw new InvalidOperationException("Unexpected authentication route: " + path)
		}, null);

		public void Dispose()
		{
			try
			{
				shutdown.Cancel();
				listener.Close();
				Assert.True(server.Wait(TimeSpan.FromSeconds(5)), "Replay server did not stop.");
			}
			finally
			{
				shutdown.Dispose();
				Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previous);
			}
		}
	}

	[Fact]
	public void Login_UsesMinecraftLifetimeAndCompletesEveryStage()
	{
		using var replay = new Replay();
		double started = Py.Time();
		bool receivedCode = false;
		PyDict account = new MicrosoftAuthenticator().Login((code, _, _) => receivedCode = (string)code == "TEST-CODE", open_browser: false);
		Assert.True(receivedCode);
		Assert.Equal("Player", account["name"]);
		Assert.Equal("rotated-test-refresh", account["refresh_token"]);
		Assert.InRange((double)account["expires_at"], started + 120, Py.Time() + 120);
		Assert.Equal(7, replay.Calls.Count);
	}

	[Fact]
	public void Refresh_RotatesTokenWithoutMutatingOriginal()
	{
		using var replay = new Replay();
		PyDict before = Account();
		PyDict refreshed = new MicrosoftAuthenticator().Refresh(before);
		Assert.NotSame(before, refreshed);
		Assert.Equal("old-test-token", before["access_token"]);
		Assert.Equal("test-minecraft", refreshed["access_token"]);
		Assert.Equal("rotated-test-refresh", refreshed["refresh_token"]);
		Assert.InRange((double)refreshed["expires_at"] - Py.Time(), 115, 120);
	}

	[Fact]
	public void Refresh_KeepsRefreshTokenWhenServerDoesNotRotateIt()
	{
		using var replay = new Replay { Reply = (path, _) => path == "/oauth20_token.srf" ? (200, "{\"access_token\":\"test-ms\"}", null) : Replay.Success(path) };
		Assert.Equal("test-refresh", new MicrosoftAuthenticator().Refresh(Account())["refresh_token"]);
	}

	[Fact]
	public void Refresh_RetriesTransientFailuresAndThenSucceeds()
	{
		using var replay = new Replay { Reply = (path, count) => path == "/oauth20_token.srf" && count < 3 ? (count == 1 ? 503 : 429, "{}", 1) : Replay.Success(path) };
		Assert.Equal("test-minecraft", new MicrosoftAuthenticator().Refresh(Account())["access_token"]);
		Assert.Equal(3, replay.Calls["/oauth20_token.srf"]);
	}

	[Fact]
	public void Refresh_ServiceFailureHasBoundedRetriesAndKeepsAccount()
	{
		using var replay = new Replay { Reply = (_, _) => (503, "service unavailable", null) };
		PyDict account = Account();
		AuthError error = Assert.Throws<AuthError>(() => new MicrosoftAuthenticator().Refresh(account));
		Assert.Contains("稍后重试", error.Message);
		Assert.DoesNotContain("重新登录", error.Message);
		Assert.Equal(3, replay.Calls["/oauth20_token.srf"]);
		Assert.Equal("test-refresh", account["refresh_token"]);
	}

	[Fact]
	public void Refresh_LongRetryAfterStopsWithoutEarlyRetry()
	{
		using var replay = new Replay { Reply = (_, _) => (429, "{}", 120) };
		Assert.Throws<AuthError>(() => new MicrosoftAuthenticator().Refresh(Account()));
		Assert.Equal(1, replay.Calls["/oauth20_token.srf"]);
	}

	[Fact]
	public void Refresh_InvalidGrantRequiresLoginWithoutRetryOrLeakingResponse()
	{
		using var replay = new Replay { Reply = (_, _) => (400, "{\"error\":\"invalid_grant\",\"error_description\":\"private-test-value\"}", null) };
		AuthError error = Assert.Throws<AuthError>(() => new MicrosoftAuthenticator().Refresh(Account()));
		Assert.Contains("重新登录", error.Message);
		Assert.DoesNotContain("private-test-value", error.Message);
		Assert.Equal(1, replay.Calls["/oauth20_token.srf"]);
	}

	[Theory]
	[InlineData("/oauth20_token.srf", "[]")]
	[InlineData("/oauth20_token.srf", "not json")]
	[InlineData("/user/authenticate", "{}")]
	[InlineData("/xsts/authorize", "{\"Token\":\"test\",\"DisplayClaims\":{\"xui\":[]}}")]
	[InlineData("/authentication/login_with_xbox", "{\"access_token\":\"test\",\"expires_in\":-1}")]
	[InlineData("/authentication/login_with_xbox", "{\"access_token\":\"test\",\"expires_in\":\"invalid\"}")]
	[InlineData("/minecraft/profile", "{\"name\":\"Player\",\"id\":\"invalid\"}")]
	public void Refresh_MalformedResponseIsActionableAndPreservesAccount(string route, string body)
	{
		using var replay = new Replay { Reply = (path, _) => path == route ? (200, body, null) : Replay.Success(path) };
		PyDict account = Account();
		AuthError error = Assert.Throws<AuthError>(() => new MicrosoftAuthenticator().Refresh(account));
		Assert.Contains("稍后重试", error.Message);
		Assert.Equal("old-test-token", account["access_token"]);
	}

	[Fact]
	public void DevicePolling_WaitsForAuthorization()
	{
		using var replay = new Replay { Reply = (path, count) => count == 1 ? (400, "{\"error\":\"authorization_pending\"}", null) : Replay.Success(path) };
		var status = new List<string>();
		PyDict token = new MicrosoftAuthenticator().PollToken("test-device", 1, 10, status.Add);
		Assert.Single(status);
		Assert.Equal("test-ms-token", token["access_token"]);
		Assert.Equal(2, replay.Calls["/oauth20_token.srf"]);
	}
}
