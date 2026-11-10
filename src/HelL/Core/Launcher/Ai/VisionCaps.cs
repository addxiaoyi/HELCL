using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HelL.Core.Launcher.Ai;

/// <summary>
/// 自动判断当前模型能不能看图，不靠写死的名单：
/// 1. 玩家在设置里手动选了「支持 / 不支持」就听玩家的；
/// 2. 查过的结果有缓存（按 端点 + 模型）；
/// 3. 查公开模型能力库 models.dev（多家服务商投票），查不到再查 OpenRouter 模型列表；
/// 4. 都查不到就发一张 32×32 的红色小图实测，看模型能不能说出颜色。
/// </summary>
public static class VisionCaps
{
	public const string ModeAuto = "auto";

	public const string ModeOn = "on";

	public const string ModeOff = "off";

	private const string ModelsDevUrl = "https://models.dev/api.json";

	private const string OpenRouterUrl = "https://openrouter.ai/api/v1/models";

	private const string ProbePng = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAKklEQVR4nGO4q6REU8QwasGoBaMWjFowasGoBaMWjFowasGoBaMWDBULANL8hD3ndtChAAAAAElFTkSuQmCC";

	private static readonly TimeSpan CatalogTtl = TimeSpan.FromDays(7);

	private static readonly TimeSpan ResultTtl = TimeSpan.FromDays(30);

	private static readonly object _lock = new object();

	private static string Root => Utils.ResolveRoot();

	private static string PrefPath => Path.Combine(Root, "ai_vision.json");

	private static string CachePath => Path.Combine(Root, "cache", "ai_vision_cache.json");

	private static string CatalogPath(string name) => Path.Combine(Root, "cache", name);

	public sealed class Result
	{
		public bool Supported;

		/// <summary>manual / cache / models.dev / openrouter / probe / rules</summary>
		public string Source = "";

		public string Model = "";
	}

	// ---------------- 手动设置 ----------------

	public static string Mode
	{
		get
		{
			try
			{
				if (!File.Exists(PrefPath)) return ModeAuto;
				using JsonDocument d = JsonDocument.Parse(File.ReadAllText(PrefPath));
				string m = d.RootElement.TryGetProperty("mode", out JsonElement e) ? e.GetString() : ModeAuto;
				return m == ModeOn || m == ModeOff ? m : ModeAuto;
			}
			catch (Exception)
			{
				return ModeAuto;
			}
		}
		set
		{
			try
			{
				Directory.CreateDirectory(Root);
				File.WriteAllText(PrefPath, "{\"mode\": " + JsonSerializer.Serialize(value ?? ModeAuto) + "}");
			}
			catch (Exception)
			{
			}
		}
	}

	// ---------------- 对外入口 ----------------

	/// <summary>当前模型是否支持看图。可能触发联网检测，别在界面线程调用。</summary>
	public static bool Supports(PyDict settings) => Detect(settings, force: false).Supported;

	public static Result Detect(PyDict settings, bool force)
	{
		settings = settings ?? new PyDict();
		string model = Client.CurrentModel(settings);
		string mode = Mode;
		if (mode == ModeOn) return new Result { Supported = true, Source = "manual", Model = model };
		if (mode == ModeOff) return new Result { Supported = false, Source = "manual", Model = model };
		string key = CacheKey(settings, model);
		if (!force && TryCache(key, out Result cached)) return cached;
		Result r = Lookup(model) ?? Probe(settings, model) ?? FromRules(settings, model);
		if (r.Source != "rules") SaveCache(key, r);
		return r;
	}

	public static string Describe(Result r)
	{
		if (r == null) return "";
		string src = r.Source switch
		{
			"manual" => "手动设置",
			"models.dev" => "models.dev 能力库",
			"openrouter" => "OpenRouter 模型列表",
			"probe" => "实测",
			"rules" => "内置规则（联网检测失败）",
			_ => "缓存",
		};
		return (r.Supported ? "支持看图" : "不支持看图") + " · " + r.Model + " · " + src;
	}

	private static string CacheKey(PyDict settings, string model)
	{
		string url = "";
		try { url = Py.S(Client.ResolveEndpoint(settings).Get("url")); } catch (Exception) { }
		return url + "|" + model;
	}

	// ---------------- 缓存 ----------------

	private static bool TryCache(string key, out Result r)
	{
		r = null;
		lock (_lock)
		{
			try
			{
				if (!File.Exists(CachePath)) return false;
				using JsonDocument d = JsonDocument.Parse(File.ReadAllText(CachePath));
				if (!d.RootElement.TryGetProperty(key, out JsonElement e)) return false;
				DateTime at = DateTime.Parse(e.GetProperty("at").GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
				if (DateTime.UtcNow - at > ResultTtl) return false;
				r = new Result { Supported = e.GetProperty("supported").GetBoolean(), Source = e.GetProperty("source").GetString(), Model = e.GetProperty("model").GetString() };
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	private static void SaveCache(string key, Result r)
	{
		lock (_lock)
		{
			try
			{
				var all = new Dictionary<string, object>();
				if (File.Exists(CachePath))
				{
					using JsonDocument d = JsonDocument.Parse(File.ReadAllText(CachePath));
					foreach (JsonProperty p in d.RootElement.EnumerateObject()) all[p.Name] = JsonSerializer.Deserialize<Dictionary<string, object>>(p.Value.GetRawText());
				}
				all[key] = new Dictionary<string, object> { ["supported"] = r.Supported, ["source"] = r.Source, ["model"] = r.Model, ["at"] = DateTime.UtcNow.ToString("o") };
				Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
				File.WriteAllText(CachePath, JsonSerializer.Serialize(all));
			}
			catch (Exception)
			{
			}
		}
	}

	// ---------------- 公开能力库 ----------------

	/// <summary>模型 id 归一化：去掉服务商前缀和 :free 这类后缀，小写。</summary>
	public static string Normalize(string model)
	{
		string m = (model ?? "").Trim().ToLowerInvariant();
		int slash = m.LastIndexOf('/');
		if (slash >= 0) m = m.Substring(slash + 1);
		int colon = m.IndexOf(':');
		if (colon > 0) m = m.Substring(0, colon);
		return m;
	}

	private static Result Lookup(string model)
	{
		string id = Normalize(model);
		if (id.Length == 0) return null;
		bool? v = VoteModelsDev(id);
		if (v.HasValue) return new Result { Supported = v.Value, Source = "models.dev", Model = model };
		v = VoteOpenRouter(id);
		if (v.HasValue) return new Result { Supported = v.Value, Source = "openrouter", Model = model };
		return null;
	}

	/// <summary>models.dev 里同一个模型会被很多服务商收录，按多数票判断。</summary>
	public static bool? VoteModelsDev(string id, string json = null)
	{
		json = json ?? Catalog("models_dev.json", ModelsDevUrl);
		if (string.IsNullOrEmpty(json)) return null;
		int yes = 0, total = 0;
		try
		{
			using JsonDocument d = JsonDocument.Parse(json);
			foreach (JsonProperty provider in d.RootElement.EnumerateObject())
			{
				if (provider.Value.ValueKind != JsonValueKind.Object || !provider.Value.TryGetProperty("models", out JsonElement models) || models.ValueKind != JsonValueKind.Object) continue;
				foreach (JsonProperty m in models.EnumerateObject())
				{
					if (Normalize(m.Name) != id) continue;
					if (!m.Value.TryGetProperty("modalities", out JsonElement mod) || !mod.TryGetProperty("input", out JsonElement input) || input.ValueKind != JsonValueKind.Array) continue;
					total++;
					if (input.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "image")) yes++;
				}
			}
		}
		catch (Exception)
		{
			return null;
		}
		if (total == 0) return null;
		return yes * 2 >= total;
	}

	public static bool? VoteOpenRouter(string id, string json = null)
	{
		json = json ?? Catalog("openrouter_models.json", OpenRouterUrl);
		if (string.IsNullOrEmpty(json)) return null;
		int yes = 0, total = 0;
		try
		{
			using JsonDocument d = JsonDocument.Parse(json);
			if (!d.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array) return null;
			foreach (JsonElement m in data.EnumerateArray())
			{
				if (!m.TryGetProperty("id", out JsonElement mid) || Normalize(mid.GetString()) != id) continue;
				if (!m.TryGetProperty("architecture", out JsonElement arch) || !arch.TryGetProperty("input_modalities", out JsonElement input) || input.ValueKind != JsonValueKind.Array) continue;
				total++;
				if (input.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "image")) yes++;
			}
		}
		catch (Exception)
		{
			return null;
		}
		if (total == 0) return null;
		return yes * 2 >= total;
	}

	/// <summary>能力库按天缓存到 cache/，下载失败时用旧的。</summary>
	private static string Catalog(string file, string url)
	{
		string path = CatalogPath(file);
		try
		{
			if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < CatalogTtl) return File.ReadAllText(path, Encoding.UTF8);
		}
		catch (Exception)
		{
		}
		try
		{
			using AiSession s = new AiSession();
			AiResponse resp = s.Get(url, PyDict.Of("Accept", "application/json", "User-Agent", Defaults.CLIENT_HEADER), 12);
			if (resp.StatusCode < 400 && !string.IsNullOrEmpty(resp.Text))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, resp.Text, new UTF8Encoding(false));
				return resp.Text;
			}
		}
		catch (Exception)
		{
		}
		try
		{
			return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	// ---------------- 实测 ----------------

	private static Result Probe(PyDict settings, string model)
	{
		try
		{
			var content = new List<object>
			{
				PyDict.Of("type", "text", "text", "这张图片是什么颜色？只回答一个颜色词。What color is this image? Answer with one word."),
				PyDict.Of("type", "image_url", "image_url", PyDict.Of("url", "data:image/png;base64," + ProbePng))
			};
			PyDict s2 = settings.Copy();
			s2["ai_max_tokens"] = 512L;
			PyDict r = Client.ChatOnce(s2, new List<object> { PyDict.Of("role", "user", "content", content) }, null, 0.0);
			string text = Py.S(r.Get("content"));
			int end = text.IndexOf("</think>", StringComparison.Ordinal);
			if (end >= 0) text = text.Substring(end + 8);
			text = text.ToLowerInvariant();
			bool ok = text.Contains("红") || text.Contains("紅") || text.Contains("red");
			return new Result { Supported = ok, Source = "probe", Model = model };
		}
		catch (AIClientError e) when (e.Message.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 || e.Message.Contains("图") || e.Message.Contains("vision") || e.Message.Contains("multimodal"))
		{
			// 接口明确拒绝图片
			return new Result { Supported = false, Source = "probe", Model = model };
		}
		catch (Exception)
		{
			// 网络或鉴权问题，判断不了
			return null;
		}
	}

	private static Result FromRules(PyDict settings, string model)
	{
		bool v = false;
		try
		{
			v = Py.Truthy(Py.DictOr(Py.Or(Client.ModelCaps(settings).Get("inputFormat"), new PyDict())).Get("supportsImage"));
		}
		catch (Exception)
		{
		}
		return new Result { Supported = v, Source = "rules", Model = model };
	}
}
