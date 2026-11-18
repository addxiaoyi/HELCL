using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai;

public static class Compact
{
	public const string _PLACEHOLDER = "[工具结果已清理：内容较旧已从上下文移除，需要时请重新调用该工具获取]";

	public const long DEFAULT_CONTEXT_WINDOW = 131072L;

	public const long MIN_CONTEXT_WINDOW = 8192L;

	public const long MAX_CONTEXT_WINDOW = 2000000L;

	public static string _SUMMARY_PROMPT => "把这段启动器对话压成一份简短摘要。必须保留：用户最终想要什么、已经执行过哪些操作（工具名和结果成败）、提到的游戏版本/实例/模组名、还没做完的事。直接输出摘要正文，不要客套。";

	private static string Role(object m)
	{
		if (!(m is PyDict pyDict))
		{
			return null;
		}
		return Py.S(pyDict.Get("role"));
	}

	public static MicroResult Microcompact(List<object> messages0, MicroConfig cfg, double idleSeconds)
	{
		List<object> list = new List<object>(messages0 ?? new List<object>());
		if (!cfg.enabled)
		{
			return new MicroResult
			{
				messages = list,
				decision_reason = "disabled"
			};
		}
		List<int> list2 = new List<int>();
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] is PyDict pyDict && Py.Eq(pyDict.Get("role"), "tool"))
			{
				list2.Add(i);
			}
		}
		long num = Math.Max(0L, cfg.keep_recent_tool_results);
		if (list2.Count == 0)
		{
			return new MicroResult
			{
				messages = list,
				decision_reason = "no_candidates"
			};
		}
		List<int> list3 = ((num > 0) ? list2.Take(Math.Max(0, list2.Count - (int)num)).ToList() : list2);
		if (list3.Count == 0)
		{
			return new MicroResult
			{
				messages = list,
				decision_reason = "nothing_to_clear"
			};
		}
		long num2 = Tokens.EstimateMessages(list);
		bool num3 = cfg.threshold_tokens.HasValue && num2 >= cfg.threshold_tokens.Value;
		bool flag = idleSeconds > 0.0 && idleSeconds >= (double)(cfg.idle_threshold_minutes * 60);
		if (!num3 && !flag)
		{
			return new MicroResult
			{
				messages = list,
				decision_reason = "not_triggered"
			};
		}
		List<object> list4 = new List<object>(list);
		foreach (int item in list3)
		{
			PyDict pyDict2 = ((PyDict)list4[item]).Copy();
			pyDict2["content"] = "[工具结果已清理：内容较旧已从上下文移除，需要时请重新调用该工具获取]";
			list4[item] = pyDict2;
		}
		long num4 = num2 - Tokens.EstimateMessages(list4);
		if (num4 < cfg.min_token_savings)
		{
			return new MicroResult
			{
				messages = list,
				decision_reason = "below_min_savings",
				tokens_saved = num4
			};
		}
		return new MicroResult
		{
			messages = list4,
			decision_reason = "applied",
			cleared_count = list3.Count,
			tokens_saved = num4,
			changed = true
		};
	}

	public static AutoConfig AutoConfigFromSettings(PyDict settings)
	{
		object o = (settings ?? new PyDict()).Get("ai_context_window");
		long num;
		try
		{
			num = Py.Int(o);
		}
		catch (Exception ex) when (ex is TypeError || ex is ValueError)
		{
			num = 131072L;
		}
		if (num <= 0)
		{
			num = 131072L;
		}
		num = Math.Max(8192L, Math.Min(num, 2000000L));
		return new AutoConfig
		{
			context_window = num
		};
	}

	public static long AutoThreshold(AutoConfig cfg)
	{
		return Math.Max(0L, cfg.context_window - cfg.buffer_tokens);
	}

	public static (bool should, string reason) ShouldAutocompact(List<object> messages, AutoConfig cfg, CompactState cstate, TokenState st)
	{
		if (!cfg.enabled)
		{
			return (should: false, reason: "disabled");
		}
		if (cstate.consecutive_failures >= cfg.max_consecutive_failures)
		{
			return (should: false, reason: "circuit_breaker");
		}
		if ((messages ?? new List<object>()).Count((object m) => m is PyDict pyDict && !Py.Eq(pyDict.Get("role"), "system")) < cfg.keep_recent_messages + 2)
		{
			return (should: false, reason: "not_enough_messages");
		}
		long num = ((st != null) ? Tokens.CurrentInputTokens(messages, st) : 0);
		if (num == 0L)
		{
			num = Tokens.EstimateMessages(messages);
		}
		if (num < AutoThreshold(cfg))
		{
			return (should: false, reason: "below_threshold");
		}
		return (should: true, reason: "above_threshold");
	}

	public static void CheckRapidRefill(CompactState cstate, AutoConfig cfg)
	{
		if (cstate.tool_turns_since_compact < cfg.tool_turn_threshold)
		{
			cstate.rapid_refills++;
			if (cstate.rapid_refills >= cfg.max_consecutive_rapid_refills)
			{
				throw new RapidRefillBlocked("上下文压缩后 " + cstate.tool_turns_since_compact + " 个工具轮又触发压缩，连续 " + cstate.rapid_refills + " 次，已熔断");
			}
		}
	}

	public static void NoteCompactSuccess(CompactState c)
	{
		c.consecutive_failures = 0L;
		c.tool_turns_since_compact = 0L;
	}

	public static bool NoteCompactFailure(CompactState c, AutoConfig cfg)
	{
		c.consecutive_failures++;
		return c.consecutive_failures >= cfg.max_consecutive_failures;
	}

	private static int _align_keep_boundary(List<object> body, int cut)
	{
		cut = Math.Max(0, Math.Min(cut, body.Count));
		while (cut > 0 && cut < body.Count && Role(body[cut]) == "tool")
		{
			cut--;
		}
		return cut;
	}

	public static CompactionResult CompactConversation(List<object> messages0, AutoConfig cfg, Func<List<object>, string> summarize)
	{
		List<object> list = new List<object>(messages0 ?? new List<object>());
		long num = Tokens.EstimateMessages(list);
		int i;
		for (i = 0; i < list.Count && Role(list[i]) == "system"; i++)
		{
		}
		List<object> range = list.GetRange(i, list.Count - i);
		int num2 = (int)Math.Max(2L, cfg.keep_recent_messages);
		if (range.Count <= num2)
		{
			return new CompactionResult
			{
				messages = list,
				pre_token_count = num,
				post_token_count = num,
				kept_message_count = range.Count
			};
		}
		int num3 = _align_keep_boundary(range, range.Count - num2);
		if (num3 <= 0)
		{
			return new CompactionResult
			{
				messages = list,
				pre_token_count = num,
				post_token_count = num,
				kept_message_count = range.Count
			};
		}
		List<object> range2 = range.GetRange(0, num3);
		List<object> range3 = range.GetRange(num3, range.Count - num3);
		string text = summarize(range2);
		if (Py.Strip(text ?? "").Length == 0)
		{
			throw new ValueError("摘要为空，拒绝替换历史");
		}
		string text2 = "";
		for (int j = 0; j < range2.Count; j++)
		{
			object obj = ((range2[j] is PyDict pyDict) ? pyDict.Get("id") : null);
			text2 = Py.Str(Py.Truthy(obj) ? obj : ("idx_" + (i + j)));
		}
		PyDict item = PyDict.Of("role", "user", "content", "[历史摘要]\n" + Py.Strip(text) + "\n（以上是更早对话的摘要，继续当前任务。）", "id", "compact_" + text2);
		List<object> range4 = list.GetRange(0, i);
		range4.Add(item);
		range4.AddRange(range3);
		long post_token_count = Tokens.EstimateMessages(range4);
		return new CompactionResult
		{
			messages = range4,
			pre_token_count = num,
			post_token_count = post_token_count,
			summarized_message_count = range2.Count,
			kept_message_count = range3.Count
		};
	}
}
