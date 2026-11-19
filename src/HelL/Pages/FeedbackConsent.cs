using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using HelL.Services;

namespace HelL.Pages;

public static class FeedbackConsent
{
	public readonly record struct Raw(bool Readable, bool? Value);

	public const int DelayMs = 400;

	public const string Key = "feedback_consent";

	public static Raw ReadRaw(string? configPath)
	{
		if (string.IsNullOrWhiteSpace(configPath))
		{
			return new Raw(Readable: false, null);
		}
		try
		{
			if (!File.Exists(configPath))
			{
				return new Raw(Readable: true, null);
			}
			return ParseRaw(File.ReadAllText(configPath));
		}
		catch
		{
			return new Raw(Readable: false, null);
		}
	}

	public static Raw ParseRaw(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				return new Raw(Readable: false, null);
			}
			if (!jsonDocument.RootElement.TryGetProperty("feedback_consent", out var value))
			{
				return new Raw(Readable: true, null);
			}
			Raw result;
			switch (value.ValueKind)
			{
			case JsonValueKind.Undefined:
			case JsonValueKind.Null:
				result = new Raw(Readable: true, null);
				break;
			case JsonValueKind.True:
				result = new Raw(Readable: true, true);
				break;
			default:
				result = new Raw(Readable: true, false);
				break;
			}
			return result;
		}
		catch (JsonException)
		{
			return new Raw(Readable: false, null);
		}
	}

	public static bool ShouldAsk(Raw raw, bool bridgeConsent)
	{
		if (raw.Readable)
		{
			return !raw.Value.HasValue;
		}
		return !bridgeConsent;
	}

	public static async Task MaybeAskAsync()
	{
		BridgeClient api = AppServices.Client;
		Dictionary<string, JsonElement> obj = (await api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		JsonElement value;
		string text = ((obj.TryGetValue("root", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
		JsonElement value2;
		bool bridgeConsent = obj.TryGetValue("feedback_consent", out value2) && value2.ValueKind == JsonValueKind.True;
		if (ShouldAsk(ReadRaw((text.Length > 0) ? Path.Combine(text, "config.json") : null), bridgeConsent))
		{
			await Task.Delay(400);
			// 关掉弹窗（Esc / 点遮罩）不等于拒绝：不写 settings，下次启动再问一次
			bool? consent = await Dlg.ConfirmOrDismiss(I18n.L("是否上传诊断数据"), I18n.L("第一次打开需要你亲自选择。\n\n同意后才会向开发者上传：\n· 你提交的反馈内容\n· 本机配置（CPU / 内存 / 显卡 / Java / 已装版本）\n\n暂不同意则不会上传，以后可在设置里更改。"), I18n.L("同意"), I18n.L("暂不同意"));
			if (consent.HasValue)
			{
				await api.TryCallAsync<object>("save_settings", new
				{
					data = new
					{
						feedback_consent = consent.Value
					}
				});
			}
		}
	}

	public static int SelfTest(TextWriter output)
	{
		(string, string, Raw)[] array = new(string, string, Raw)[7]
		{
			("empty object -> never asked", "{}", new Raw(Readable: true, null)),
			("explicit null -> never asked", "{\"feedback_consent\": null}", new Raw(Readable: true, null)),
			("true -> asked, consented", "{\"feedback_consent\": true, \"first_run\": false}", new Raw(Readable: true, true)),
			("false -> asked, declined", "{\"feedback_consent\": false}", new Raw(Readable: true, false)),
			("non-bool value counts as asked (python: is not None)", "{\"feedback_consent\": \"yes\"}", new Raw(Readable: true, false)),
			("broken json -> unreadable", "{not json", new Raw(Readable: false, null)),
			("json array -> unreadable", "[1,2]", new Raw(Readable: false, null))
		};
		(string, Raw, bool, bool)[] array2 = new(string, Raw, bool, bool)[6]
		{
			("config readable, key null -> ask", new Raw(Readable: true, null), false, true),
			("config readable, key null even if bridge says true (file wins) -> ask", new Raw(Readable: true, null), true, true),
			("config readable, true -> no", new Raw(Readable: true, true), true, false),
			("config readable, false (declined earlier, maybe in Qt) -> no", new Raw(Readable: true, false), false, false),
			("config unreadable, bridge consented -> no", new Raw(Readable: false, null), true, false),
			("config unreadable, bridge false -> ask", new Raw(Readable: false, null), false, true)
		};
		(string, bool, bool)[] array3 = new(string, bool, bool)[2]
		{
			("after agreeing, next launch does not ask", true, false),
			("after declining, next launch does not ask", false, false)
		};
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		int num = 0;
		(string, string, Raw)[] array4 = array;
		for (int i = 0; i < array4.Length; i++)
		{
			(string, string, Raw) tuple = array4[i];
			Raw raw = ParseRaw(tuple.Item2);
			bool flag = raw == tuple.Item3;
			if (!flag)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple.Item1,
				["kind"] = "parse",
				["json"] = tuple.Item2,
				["want"] = string.Format("{0}/{1}", tuple.Item3.Readable, tuple.Item3.Value?.ToString() ?? "null"),
				["got"] = string.Format("{0}/{1}", raw.Readable, raw.Value?.ToString() ?? "null"),
				["ok"] = flag
			});
		}
		(string, Raw, bool, bool)[] array5 = array2;
		for (int j = 0; j < array5.Length; j++)
		{
			(string, Raw, bool, bool) tuple2 = array5[j];
			bool flag2 = ShouldAsk(tuple2.Item2, tuple2.Item3);
			bool flag3 = flag2 == tuple2.Item4;
			if (!flag3)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple2.Item1,
				["kind"] = "decide",
				["readable"] = tuple2.Item2.Readable,
				["raw"] = tuple2.Item2.Value,
				["bridge_consent"] = tuple2.Item3,
				["want"] = tuple2.Item4,
				["got"] = flag2,
				["ok"] = flag3
			});
		}
		(string, bool, bool)[] array6 = array3;
		for (int k = 0; k < array6.Length; k++)
		{
			(string, bool, bool) tuple3 = array6[k];
			bool flag4 = ShouldAsk(ParseRaw("{\"feedback_consent\": null}"), bridgeConsent: false);
			bool flag5 = ShouldAsk(ParseRaw(JsonSerializer.Serialize(new Dictionary<string, object> { ["feedback_consent"] = tuple3.Item2 })), tuple3.Item2);
			bool flag6 = flag4 && flag5 == tuple3.Item3;
			if (!flag6)
			{
				num++;
			}
			list.Add(new Dictionary<string, object>
			{
				["case"] = tuple3.Item1,
				["kind"] = "transition",
				["choice"] = tuple3.Item2,
				["first_launch_asks"] = flag4,
				["second_launch_asks"] = flag5,
				["want_second"] = tuple3.Item3,
				["ok"] = flag6
			});
		}
		output.Write(JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["total"] = array.Length + array2.Length + array3.Length,
			["failed"] = num,
			["delay_ms"] = 400,
			["cases"] = list
		}, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		}));
		output.Flush();
		return (num != 0) ? 1 : 0;
	}

	public static int SelfTest()
	{
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		using StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), encoding);
		return SelfTest(output);
	}
}
