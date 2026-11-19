using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Input;

namespace HelL.Services;

public static class ImeGuard
{
	public static bool ShouldSend(Key key, Key imeProcessedKey, bool shift, string? composition)
	{
		if (shift)
		{
			return false;
		}
		if (key == Key.ImeProcessed || imeProcessedKey != Key.None)
		{
			return false;
		}
		if (key != Key.Return)
		{
			return false;
		}
		return string.IsNullOrEmpty(composition);
	}

	public static int SelfTest(TextWriter output)
	{
		(string, Key, Key, bool, string, bool)[] array = new(string, Key, Key, bool, string, bool)[9]
		{
			("空输入框直接回车 → 发送", Key.Return, Key.None, false, "", true),
			("Return 键同样算发送", Key.Return, Key.None, false, null, true),
			("Shift+Enter → 换行不发", Key.Return, Key.None, true, "", false),
			("拼音串未上屏时回车 → 不发", Key.Return, Key.None, false, "nihao", false),
			("输入法吃掉这一下 → 不发", Key.ImeProcessed, Key.Return, false, "", false),
			("ImeProcessedKey 有值 → 不发", Key.Return, Key.Return, false, "", false),
			("组合串已上屏（清空）后回车 → 发送", Key.Return, Key.None, false, "", true),
			("普通字符键 → 不发", Key.A, Key.None, false, "", false),
			("拼音串 + Shift → 不发", Key.Return, Key.None, true, "zhongwen", false)
		};
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		int num = 0;
		(string, Key, Key, bool, string, bool)[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			(string, Key, Key, bool, string, bool) tuple = array2[i];
			bool flag = ShouldSend(tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5);
			bool flag2 = flag == tuple.Item6;
			if (!flag2)
			{
				num++;
			}
			Dictionary<string, object> dictionary = new Dictionary<string, object> { ["case"] = tuple.Item1 };
			Key item = tuple.Item2;
			dictionary["key"] = item.ToString();
			item = tuple.Item3;
			dictionary["ime_processed_key"] = item.ToString();
			dictionary["shift"] = tuple.Item4;
			dictionary["composition"] = tuple.Item5;
			dictionary["want"] = tuple.Item6;
			dictionary["got"] = flag;
			dictionary["ok"] = flag2;
			list.Add(dictionary);
		}
		output.Write(JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["total"] = array.Length,
			["failed"] = num,
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
