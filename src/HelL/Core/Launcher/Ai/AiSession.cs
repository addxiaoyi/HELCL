using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher.Ai;

public sealed class AiSession : IDisposable
{
	private readonly HttpClient _client;
	private readonly bool _localOnly;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	public AiSession(bool localOnly = false)
	{
		_localOnly = localOnly;
		bool flag = !Py.Truthy(Config.CONFIG.Get("use_system_proxy", true));
		HttpClientHandler handler = new HttpClientHandler
		{
			AllowAutoRedirect = !localOnly,
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
			UseProxy = !localOnly && !flag
		};
		_client = new HttpClient(handler)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public AiResponse Get(string url, PyDict headers, int timeoutSec)
	{
		return Send(HttpMethod.Get, url, headers, null, timeoutSec, timeoutSec, stream: false);
	}

	public AiResponse Post(string url, PyDict headers, object jsonBody, int connectSec, int readSec, bool stream)
	{
		return Send(HttpMethod.Post, url, headers, jsonBody, connectSec, readSec, stream);
	}

	private AiResponse Send(HttpMethod m, string url, PyDict headers, object jsonBody, int connectSec, int readSec, bool stream)
	{
		string text = _localOnly ? "" : (Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY") ?? "").Trim();
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(m, (text.Length > 0) ? text.TrimEnd('/') : url);
		if (text.Length > 0)
		{
			httpRequestMessage.Headers.TryAddWithoutValidation("X-HelL-Replay-Url", url);
		}
		string text2 = null;
		if (headers != null)
		{
			foreach (KeyValuePair<string, object> header in headers)
			{
				if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
				{
					text2 = Py.Str(header.Value);
				}
				else
				{
					httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, Py.Str(header.Value));
				}
			}
		}
		if (jsonBody != null)
		{
			ByteArrayContent byteArrayContent = new ByteArrayContent(Encoding.UTF8.GetBytes(PyJson.Dumps(jsonBody)));
			byteArrayContent.Headers.TryAddWithoutValidation("Content-Type", text2 ?? "application/json");
			httpRequestMessage.Content = byteArrayContent;
		}
		using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(stream ? connectSec : Math.Max(connectSec, readSec)));
		using CancellationTokenSource cancellationTokenSource2 = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, _cts.Token);
		try
		{
			HttpResponseMessage result = _client.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource2.Token).GetAwaiter().GetResult();
			_cts.Token.Register(DisposeState, result);
			AiResponse aiResponse = new AiResponse
			{
				StatusCode = (int)result.StatusCode,
				Raw = result,
				ReadTimeoutMs = readSec * 1000
			};
			aiResponse.Body = result.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
			if (!stream)
			{
				_ = aiResponse.Content;
			}
			return aiResponse;
		}
		catch (OperationCanceledException inner)
		{
			if (_cts.IsCancellationRequested)
			{
				throw new AiRequestException("Connection aborted.", inner);
			}
			throw new AiRequestException("Read timed out. (read timeout=" + connectSec + ")", inner);
		}
		catch (HttpRequestException ex)
		{
			throw new AiRequestException(ex.InnerException?.Message ?? ex.Message, ex);
		}
	}

	public void Close()
	{
		try
		{
			_cts.Cancel();
		}
		catch (Exception)
		{
		}
	}

	public void Dispose()
	{
		Close();
		try
		{
			_client.Dispose();
		}
		catch (Exception)
		{
		}
	}

	private static void DisposeState(object o)
	{
		try
		{
			(o as IDisposable).Dispose();
		}
		catch (Exception)
		{
		}
	}
}
