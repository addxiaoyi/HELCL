using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai;

public static class Permission
{
	public static class PermissionMode
	{
		public const string DEFAULT = "default";

		public const string PLAN = "plan";

		public const string EDIT = "edit";

		public const string ACCEPT_EDITS = "acceptEdits";

		public const string AUTO = "auto";

		public const string DONT_ASK = "dontAsk";

		public const string AUTO_EDIT = "autoEdit";

		public const string YOLO = "yolo";

		public const string BYPASS_PERMISSIONS = "bypassPermissions";

		public const string BUILD = "build";

		public static readonly string[] Values = new string[10] { "default", "plan", "edit", "acceptEdits", "auto", "dontAsk", "autoEdit", "yolo", "bypassPermissions", "build" };

		private static readonly HashSet<string> _set = new HashSet<string>(Values);

		public static bool IsValid(string v)
		{
			if (v != null)
			{
				return _set.Contains(v);
			}
			return false;
		}
	}

	public static readonly string[] BEHAVIORS = new string[3] { "allow", "deny", "ask" };

	public const string SCOPE_INSTANCE = "instance";

	public const string SCOPE_GLOBAL = "global";

	public static readonly string[] RULE_CONTENT_KEYS = new string[10] { "command", "url", "file_path", "path", "pattern", "filename", "slug", "name", "version", "major" };

	public static string PERMISSIONS_FILE = null;

	private static readonly object _STORE_LOCK = new object();

	private static readonly Dictionary<string, string> _BEHAVIOR_KEYS = new Dictionary<string, string>
	{
		["allow"] = "允许",
		["deny"] = "禁止",
		["ask"] = "每次问"
	};

	public static string NormalizePermissionMode(object raw_mode, bool confirm_writes = true)
	{
		string text = Py.Strip(Py.S(raw_mode));
		if (text == null || text.Length != 0)
		{
			switch (text)
			{
			case "standard":
				break;
			case "full":
				if (!confirm_writes)
				{
					return "yolo";
				}
				return "acceptEdits";
			case "custom":
				return "custom";
			default:
				if (PermissionMode.IsValid(text))
				{
					return text;
				}
				if (!confirm_writes)
				{
					return "yolo";
				}
				return "default";
			}
		}
		if (!confirm_writes)
		{
			return "yolo";
		}
		return "default";
	}

	public static List<AiRule> DedupeRules(IEnumerable<object> rules)
	{
		List<AiRule> list = new List<AiRule>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (object item in rules ?? Enumerable.Empty<object>())
		{
			AiRule aiRule = (item as AiRule) ?? CoerceRule(item);
			if (aiRule != null && hashSet.Add(aiRule.Key()))
			{
				list.Add(aiRule);
			}
		}
		return list;
	}

	public static List<AiRule> ApplyUpdates(IEnumerable<AiRule> rules, IEnumerable<AiRule> newRules)
	{
		return DedupeRules((rules ?? Enumerable.Empty<AiRule>()).Cast<object>().Concat((newRules ?? Enumerable.Empty<AiRule>()).Cast<object>()));
	}

	private static List<AiRule> _rules_of(PyDict section)
	{
		List<AiRule> list = new List<AiRule>();
		string[] bEHAVIORS = BEHAVIORS;
		foreach (string text in bEHAVIORS)
		{
			foreach (object item in Py.ListOr(Py.Or(section.Get(text), new List<object>())))
			{
				AiRule aiRule = CoerceRule(item);
				if (aiRule != null)
				{
					aiRule.behavior = text;
					list.Add(aiRule);
				}
			}
		}
		return list;
	}

	public static List<AiRule> LoadRules(string instance = null)
	{
		PyDict pyDict = LoadRuleStore();
		List<AiRule> list = _rules_of((PyDict)pyDict["global"]);
		if (!string.IsNullOrEmpty(instance))
		{
			list.AddRange(_rules_of((((PyDict)pyDict["per_instance"]).Get(instance) as PyDict) ?? new PyDict()));
		}
		return DedupeRules(list);
	}

	private static string _norm_content(object text)
	{
		return Utils.NormPathSpelling(text);
	}

	private static bool _matches(AiRule rule, string tname, object content)
	{
		if (rule.tool_name != tname)
		{
			return false;
		}
		if (!Py.Truthy(rule.rule_content))
		{
			return true;
		}
		return _norm_content(rule.rule_content) == _norm_content(content);
	}

	private static string _norm_mode(object mode)
	{
		string text = Py.Str(Py.Or(mode, "default"));
		if (!PermissionMode.IsValid(text))
		{
			return "default";
		}
		return text;
	}

	public static PermissionResult Decide(ToolMetaInfo meta, PyDict args, object mode, IEnumerable<AiRule> rules)
	{
		bool flag = meta?.@readonly ?? false;
		string text = ((meta != null && !string.IsNullOrEmpty(meta.side_effect)) ? meta.side_effect : "write_local");
		string text2 = ((meta != null && !string.IsNullOrEmpty(meta.risk)) ? meta.risk : "medium");
		string tname = meta?.name ?? "";
		object content = RuleContentFromInput(args);
		string text3 = _norm_mode(mode);
		if (text3 == "plan" && !flag)
		{
			return new PermissionResult("deny", "当前是「只看不动」模式，不能执行这类操作");
		}
		List<AiRule> list = (rules ?? Enumerable.Empty<AiRule>()).Where((AiRule r) => r != null && _matches(r, tname, content)).ToList();
		(string, string)[] array = new(string, string)[3]
		{
			("deny", "deny"),
			("allow", "allow"),
			("ask", "ask")
		};
		for (int num = 0; num < array.Length; num++)
		{
			var (text4, text5) = array[num];
			foreach (AiRule item in list)
			{
				if (!(item.behavior != text4) && (!(text5 == "allow") || Py.Truthy(item.rule_content) || !(text == "delete")))
				{
					string ruleId = "rule." + item.tool_name + "." + item.behavior;
					if (text5 == "deny")
					{
						return new PermissionResult("deny", "这条操作被你的规则禁止", ruleId);
					}
					if (text5 == "allow")
					{
						return new PermissionResult("allow", "规则允许", ruleId);
					}
					return new PermissionResult("ask", "规则要求先询问", ruleId);
				}
			}
		}
		if (flag)
		{
			return new PermissionResult("allow", "只读操作");
		}
		switch (text3)
		{
		case "yolo":
		case "bypassPermissions":
			return new PermissionResult("allow", "免确认模式");
		case "auto":
		case "acceptEdits":
		case "autoEdit":
			if (!(text == "delete"))
			{
				return new PermissionResult("allow", "写操作直接执行");
			}
			return new PermissionResult("ask", "删除操作仍需确认");
		case "build":
			if (!(text == "delete") && !(text == "launch"))
			{
				return new PermissionResult("allow", "安装/下载类直接执行");
			}
			return new PermissionResult("ask", "删除/启动操作仍需确认");
		case "edit":
			if (!(text == "write_local"))
			{
				return new PermissionResult("ask", "这类操作需要确认");
			}
			return new PermissionResult("allow", "文件类修改直接执行");
		case "dontAsk":
			return new PermissionResult("deny", "用户开启了「不询问」模式");
		default:
			if (text2 == "high")
			{
				return new PermissionResult("ask", "高风险操作需要确认");
			}
			return new PermissionResult("ask", "写操作需要确认");
		}
	}

	public static string PermissionNote(PyDict settings)
	{
		settings = settings ?? new PyDict();
		switch (NormalizePermissionMode(settings.Get("ai_permission_mode"), Py.Truthy(settings.Get("ai_confirm_writes", true))))
		{
		case "yolo":
		case "bypassPermissions":
			return "[权限设置] 用户选了「全自动」：写操作直接执行，不会弹确认。你仍要先用一句话说明将要做什么。";
		case "plan":
			return "[权限设置] 用户选了「只看不动」：你只能查看和诊断，不能安装/删除/改配置。";
		case "auto":
		case "acceptEdits":
		case "autoEdit":
		case "build":
			return "[权限设置] 写操作会直接执行；删除实例、删除模组前仍会先询问，要等用户点了才执行。";
		case "dontAsk":
			return "[权限设置] 用户选了「不询问」：需要确认的写操作会被直接拒绝，只读查看照常。";
		case "edit":
			return "[权限设置] 文件类修改直接执行；安装、删除、启动前会弹确认，要等用户点了才执行。";
		default:
			return "[权限设置] 安装、删除、禁用、改配置、启动这类写操作执行前都会弹确认，用户点了才会真的执行；查看类操作不弹。被拒绝时不要重复发起同一操作，改为说明原因。";
		}
	}

	public static string BehaviorOf(object value)
	{
		string text = Py.Str(value);
		if (BEHAVIORS.Contains<string>(text))
		{
			return text;
		}
		throw new ValueError(Py.Repr(text) + " is not a valid Behavior");
	}

	public static object RuleContentFromInput(PyDict args)
	{
		string[] rULE_CONTENT_KEYS = RULE_CONTENT_KEYS;
		foreach (string key in rULE_CONTENT_KEYS)
		{
			if ((args ?? new PyDict())[key] is string s && Py.Strip(s).Length > 0)
			{
				return Py.Strip(s);
			}
		}
		return null;
	}

	public static AiRule CoerceRule(object raw)
	{
		if (!(raw is PyDict pyDict))
		{
			return null;
		}
		try
		{
			return new AiRule(Py.Str(Py.Or(pyDict["toolName"], pyDict["tool_name"], "")), Py.Truthy(pyDict["ruleContent"]) ? pyDict["ruleContent"] : null, BehaviorOf(Py.Or(pyDict["behavior"], "allow")));
		}
		catch (ValueError)
		{
			return null;
		}
	}

	private static string _store_path()
	{
		return PERMISSIONS_FILE ?? PPath.Join(Utils.ROOT, "ai_permissions.json");
	}

	private static PyDict Buckets()
	{
		return PyDict.Of("allow", new List<object>(), "deny", new List<object>(), "ask", new List<object>());
	}

	private static PyDict _blank_store()
	{
		return PyDict.Of("version", 1L, "global", Buckets(), "per_instance", new PyDict());
	}

	public static PyDict LoadRuleStore()
	{
		object obj;
		lock (_STORE_LOCK)
		{
			obj = Utils.ReadJson(_store_path());
		}
		if (!(obj is PyDict pyDict))
		{
			return _blank_store();
		}
		PyDict pyDict2 = _blank_store();
		object obj2 = Py.Or(pyDict["global"], new PyDict());
		string[] bEHAVIORS = BEHAVIORS;
		foreach (string key in bEHAVIORS)
		{
			object obj3 = ((obj2 as PyDict) ?? throw new AttributeError("'" + Py.TypeName(obj2) + "' object has no attribute 'get'"))[key];
			if (obj3 is List<object>)
			{
				((PyDict)pyDict2["global"])[key] = obj3;
			}
		}
		if (pyDict["per_instance"] is PyDict pyDict3)
		{
			PyDict pyDict4 = new PyDict();
			foreach (KeyValuePair<string, object> item in pyDict3)
			{
				if (item.Value is PyDict pyDict5)
				{
					PyDict pyDict6 = new PyDict();
					bEHAVIORS = BEHAVIORS;
					foreach (string key2 in bEHAVIORS)
					{
						pyDict6[key2] = ((pyDict5[key2] is List<object> list) ? list : new List<object>());
					}
					pyDict4[item.Key] = pyDict6;
				}
			}
			pyDict2["per_instance"] = pyDict4;
		}
		return pyDict2;
	}

	public static void SaveRuleStore(PyDict store)
	{
		lock (_STORE_LOCK)
		{
			Utils.WriteJson(_store_path(), store);
		}
	}

	public static void AppendRule(AiRule rule, string instance = null)
	{
		lock (_STORE_LOCK)
		{
			PyDict pyDict = LoadRuleStore();
			string behavior = rule.behavior;
			PyDict pyDict2 = PyDict.Of("toolName", rule.tool_name);
			if (Py.Truthy(rule.rule_content))
			{
				pyDict2["ruleContent"] = rule.rule_content;
			}
			PyDict pyDict4;
			if (!string.IsNullOrEmpty(instance))
			{
				PyDict pyDict3 = (PyDict)pyDict["per_instance"];
				if (!pyDict3.ContainsKey(instance))
				{
					pyDict3[instance] = Buckets();
				}
				pyDict4 = (PyDict)pyDict3[instance];
			}
			else
			{
				pyDict4 = (PyDict)pyDict["global"];
			}
			if (!pyDict4.ContainsKey(behavior))
			{
				pyDict4[behavior] = new List<object>();
			}
			List<object> list = (List<object>)pyDict4[behavior];
			if (!new HashSet<string>(list.Select(delegate(object r)
			{
				PyDict pyDict5 = (r as PyDict) ?? throw new AttributeError("'" + Py.TypeName(r) + "' object has no attribute 'get'");
				return Py.Str(pyDict5["toolName"]) + "\0" + (Py.Truthy(pyDict5["ruleContent"]) ? Py.Str(pyDict5["ruleContent"]) : "");
			})).Contains(rule.Key()))
			{
				list.Add(pyDict2);
			}
			SaveRuleStore(pyDict);
		}
	}

	private static PyDict Row(AiRule r, string behavior, string inst, string scope)
	{
		return PyDict.Of("key", r.Key(), "toolName", r.tool_name, "ruleContent", Py.Truthy(r.rule_content) ? r.rule_content : "", "behavior", behavior, "behavior_label", I18nCore.Tr(_BEHAVIOR_KEYS[behavior]), "instance", inst, "scope", scope);
	}

	public static List<object> ListStoredRules()
	{
		PyDict pyDict = LoadRuleStore();
		List<object> list = new List<object>();
		PyDict pyDict2 = (PyDict)pyDict["global"];
		string[] bEHAVIORS = BEHAVIORS;
		foreach (string text in bEHAVIORS)
		{
			foreach (object item in Py.ListOr(Py.Or(pyDict2[text], new List<object>())))
			{
				AiRule aiRule = CoerceRule(item);
				if (aiRule != null)
				{
					aiRule.behavior = text;
					list.Add(Row(aiRule, text, "", I18nCore.Tr("全局")));
				}
			}
		}
		foreach (KeyValuePair<string, object> item2 in (PyDict)pyDict["per_instance"])
		{
			PyDict pyDict3 = (item2.Value as PyDict) ?? new PyDict();
			bEHAVIORS = BEHAVIORS;
			foreach (string text2 in bEHAVIORS)
			{
				foreach (object item3 in Py.ListOr(Py.Or(pyDict3[text2], new List<object>())))
				{
					AiRule aiRule2 = CoerceRule(item3);
					if (aiRule2 != null)
					{
						aiRule2.behavior = text2;
						list.Add(Row(aiRule2, text2, item2.Key, Py.Fmt(I18nCore.Tr("实例 {0}"), item2.Key)));
					}
				}
			}
		}
		return list;
	}

	public static bool RemoveRule(object key, string instance = null)
	{
		lock (_STORE_LOCK)
		{
			PyDict pyDict = LoadRuleStore();
			PyDict pyDict2 = (PyDict)pyDict["per_instance"];
			List<PyDict> obj = ((instance == null) ? new PyDict[1] { (PyDict)pyDict["global"] }.Concat(pyDict2.Select((KeyValuePair<string, object> kv) => (PyDict)kv.Value)).ToList() : ((!(instance == "")) ? new List<PyDict> { (pyDict2[instance] as PyDict) ?? new PyDict() } : new List<PyDict> { (PyDict)pyDict["global"] }));
			bool flag = false;
			foreach (PyDict item in obj)
			{
				string[] bEHAVIORS = BEHAVIORS;
				foreach (string key2 in bEHAVIORS)
				{
					List<object> list = Py.ListOr(Py.Or(item[key2], new List<object>()));
					List<object> list2 = new List<object>();
					foreach (object item2 in list)
					{
						AiRule aiRule = CoerceRule(item2);
						if (aiRule != null && Py.Eq(aiRule.Key(), key))
						{
							flag = true;
						}
						else
						{
							list2.Add(item2);
						}
					}
					item[key2] = list2;
				}
			}
			if (flag)
			{
				SaveRuleStore(pyDict);
			}
			return flag;
		}
	}
}
