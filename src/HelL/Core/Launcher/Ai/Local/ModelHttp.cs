using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public static class ModelHttp
{
    public static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri uri, long offset, CancellationToken ct)
    {
        for (int redirects = 0; redirects < 8; redirects++)
        {
            if (uri.Scheme != "https" || uri.UserInfo.Length != 0) throw new IOException("模型来源重定向到不安全地址，已停止请求。");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code != 301 && code != 302 && code != 303 && code != 307 && code != 308) return response;
            // .NET Framework does not follow 308 automatically, which several model mirrors use.
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new IOException("模型来源返回重定向但没有提供目标地址。");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new IOException("模型来源重定向次数过多，请切换下载源。");
    }
}
