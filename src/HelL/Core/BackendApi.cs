using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;

namespace HelL.Core;

public partial class BackendApi
{
	internal AccountManager accounts;

	internal volatile bool _ai_busy;

	internal PyDict _ai_pending_card;

	internal volatile bool _ai_cancel;

	internal readonly object _ai_lock = new object();

	internal List<object> _ai_steer = new List<object>();

	internal object _ai_confirm_ok;

	internal readonly ManualResetEventSlim _ai_confirm_ev = new ManualResetEventSlim(initialState: false);

	internal (string name, PyDict args)? _ai_confirm_ctx;

	internal object _ai_ask_result;

	internal readonly ManualResetEventSlim _ai_ask_ev = new ManualResetEventSlim(initialState: false);

	internal Action _ai_http_abort;

	internal readonly EventBus _bus;

	private long _counter;

	internal readonly Dictionary<string, BackendWorker> _workers = new Dictionary<string, BackendWorker>();

	internal readonly Dictionary<string, object> _titles = new Dictionary<string, object>();

	internal readonly object _lock = new object();

	internal Process _game_proc;

	internal readonly object _game_lock = new object();

	internal volatile string _launch_task_id;

	internal readonly PyDict _crashes = new PyDict();

	private readonly List<KeyValuePair<string, (bool ok, string msg)>> _task_results = new List<KeyValuePair<string, (bool, string)>>();

	private readonly object _resultsLock = new object();

	internal List<object> _pack_cache = new List<object>();

	internal List<object> _mod_cache = new List<object>();

	internal PyDict _ui_launch = new PyDict();

	internal volatile List<object> _inst_cache;

	internal double _inst_cache_at;

	internal static readonly string[] _INTERACTIVE_TITLES;

	internal static readonly string[] _NAV_SECTIONS;

	internal static readonly string[] _SECRET_SETTING_KEYS;

	internal static readonly HashSet<string> _PERM_MODES;

	internal static readonly string[] _WINDOW_ASPECTS;

	private static readonly string[][] LOADER_TAGS;

	public static Func<List<object>> AllJavasHook;

	private static readonly Regex _B64_OK;

	private static readonly Regex _UPLOAD_BAD;

	public EventBus Bus => _bus;

	private static LauncherConfig CONFIG => Config.CONFIG;

	public object get_accounts()
	{
		List<object> list = new List<object> { I18nCore.Tr("离线模式") };
		foreach (PyDict account in accounts.accounts)
		{
			object obj = account["name"];
			if (Py.Truthy(obj) && !Py.In(obj, list))
			{
				list.Add(obj);
			}
		}
		return list;
	}

	public object get_account_rows()
	{
		List<object> list = new List<object>();
		foreach (PyDict account in accounts.accounts)
		{
			list.Add(PyDict.Of("name", Py.Or(account["name"], ""), "type", Py.Or(account["type"], "offline"), "uuid", Py.Or(account["uuid"], ""), "api", Py.Or(account["api"], ""), "avatar", Skin.AvatarUrl(account), "body", Skin.BodyUrl(account), "active", Py.Eq(account["name"], accounts.active), "skin_file", Py.Or(account["skin_file"], ""), "skin_model", Skin.SkinModel(account)));
		}
		return list;
	}

	public object remove_account(object name)
	{
		accounts.RemoveAccount(name);
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object set_active_account(object name)
	{
		accounts.SetActive(name);
		Emit("ui_changed", new PyDict());
		return accounts.active;
	}

	public object add_offline_account(object username, [Def("")] object skin = null)
	{
		PyDict pyDict = accounts.OfflineAccount(username, Py.Or(skin, CONFIG.Get("offline_skin"), "default"));
		PyDict account = new PyDict(pyDict) { ["type"] = "offline" };
		accounts.AddAccount(account);
		Emit("ui_changed", new PyDict());
		return pyDict["name"];
	}

	public object start_microsoft_login()
	{
		AuthHttp.Warmup("https://login.live.com/");
		return StartTask("微软登录", _microsoft_login_impl);
	}

	private object _microsoft_login_impl(TaskContext ctx)
	{
		MicrosoftAuthenticator microsoftAuthenticator = new MicrosoftAuthenticator(Config.MsClientId());
		TaskContext worker = TaskContext.Current ?? ctx;
		PyDict pyDict = microsoftAuthenticator.Login(on_code, on_status);
		accounts.AddAccount(pyDict);
		ctx.Log("登录成功：" + Py.Str(pyDict.Get("name")));
		return Py.Fmt(I18nCore.Tr("已登录 {0}"), pyDict.Get("name"));
		void on_code(object code, object uri, object exp)
		{
			worker?.LoginCode(code, uri);
			long num = Py.Int(exp);
			long num2 = ((num >= 0) ? (num / 60) : (-((-num + 59) / 60)));
			ctx.Log($"请在微软登录弹窗中查看授权代码（{num2} 分钟内有效）。");
		}
		void on_status(string st)
		{
			worker?.LoginStatus(st);
			ctx.Log(st);
			ctx.Progress(0L, 0L, st);
		}
	}

	public object start_authlib_login(object api, object username, object password)
	{
		return StartTask("皮肤站登录", delegate(TaskContext ctx)
		{
			Authlib.EnsureInjector(_dm(ctx), delegate(string t)
			{
				ctx.Log(t);
			});
			PyDict pyDict = Authlib.Login(api, username, password);
			accounts.AddAccount(pyDict);
			ctx.Log("皮肤站登录成功：" + Py.Str(pyDict.Get("name")));
			return Py.Fmt(I18nCore.Tr("已登录 {0}"), pyDict.Get("name"));
		});
	}

	public object start_nide8_login(object server_id, object username, object password)
	{
		return StartTask("统一通行证登录", delegate(TaskContext ctx)
		{
			Nide8.EnsureJar(_dm(ctx), delegate(string t)
			{
				ctx.Log(t);
			});
			PyDict pyDict = Nide8.Login(server_id, username, password);
			accounts.AddAccount(pyDict);
			ctx.Log("统一通行证登录成功：" + Py.Str(pyDict.Get("name")));
			return Py.Fmt(I18nCore.Tr("已登录 {0}"), pyDict.Get("name"));
		});
	}

	private static byte[] B64Strict(string raw)
	{
		for (int i = 0; i < raw.Length; i++)
		{
			if (raw[i] > '\u007f')
			{
				throw new ValueError("string argument should contain only ASCII characters");
			}
		}
		int num = 0;
		int num2 = raw.Length;
		while (num2 > 0 && raw[num2 - 1] == '=')
		{
			num++;
			num2--;
		}
		for (int j = 0; j < num2; j++)
		{
			char c = raw[j];
			if ((c < 'A' || c > 'Z') && (c < 'a' || c > 'z') && (c < '0' || c > '9') && c != '+' && c != '/')
			{
				throw new ValueError("Only base64 data is allowed");
			}
		}
		if (num > 2 || raw.Length % 4 != 0)
		{
			throw new ValueError("Incorrect padding");
		}
		try
		{
			return Convert.FromBase64String(raw);
		}
		catch (FormatException)
		{
			throw new ValueError("Incorrect padding");
		}
	}

	public object set_account_skin(object name, [Def("")] object path = null, [Def("classic")] object model = null, [Def("")] object data = null)
	{
		PyDict account = accounts.GetAccount(name);
		if (!Py.Truthy(account))
		{
			throw new ValueError(Py.Fmt(I18nCore.Tr("没有这个账号：{0}"), name));
		}
		if (!Py.Eq(account.Get("type"), "offline"))
		{
			throw new ValueError(I18nCore.Tr("自定义皮肤只对离线账号有效；正版和皮肤站账号的皮肤在各自的网站上改"));
		}
		string text = Py.Strip(Py.AsStr(Py.Or(data, ""), "strip"));
		if (text.Length > 0)
		{
			string raw = (text.StartsWith("data:", StringComparison.Ordinal) ? Py.Split(text, ",", 1)[Py.Split(text, ",", 1).Count - 1] : text);
			byte[] data2;
			try
			{
				data2 = B64Strict(raw);
			}
			catch (ValueError)
			{
				throw new ValueError(I18nCore.Tr("皮肤数据不是有效的 base64"));
			}
			Skin.ValidateSkin(data2);
			account["skin_file"] = Skin.SaveSkinBytes(name, data2);
			account["skin_model"] = ((Py.Lower(Py.Str(model)) == "slim") ? "slim" : "classic");
		}
		else if (Py.Strip(Py.AsStr(Py.Or(path, ""), "strip")).Length == 0)
		{
			Skin.RemoveSkin(account);
			account.Remove("skin_file");
			account.Remove("skin_model");
		}
		else
		{
			account["skin_file"] = Skin.ImportSkin(name, Py.Str(path));
			account["skin_model"] = ((Py.Lower(Py.Str(model)) == "slim") ? "slim" : "classic");
		}
		accounts.Save();
		Emit("ui_changed", new PyDict());
		return PyDict.Of("name", name, "skin_file", Py.Or(account.Get("skin_file"), ""), "skin_model", Skin.SkinModel(account));
	}

	public object get_account_skin(object name)
	{
		PyDict pyDict = accounts.GetAccount(name) ?? new PyDict();
		byte[] array = Skin.LoadSkinPng(pyDict);
		return PyDict.Of("name", name, "skin_file", Py.Or(pyDict.Get("skin_file"), ""), "skin_model", Skin.SkinModel(pyDict), "data_url", (array != null && array.Length != 0) ? ("data:image/png;base64," + Convert.ToBase64String(array)) : "");
	}

	public object ai_rewind([Def("")] object chat_id = null)
	{
		if (_ai_busy)
		{
			return PyDict.Of("ok", false, "truncated", false, "restored_files", 0L, "restored_bytes", 0L, "rollbackable", false, "disk_changed", false, "message", I18nCore.Tr("回合还在跑，先停止再撤回"));
		}
		string text = Py.Strip(Py.Str(Py.Truthy(chat_id) ? chat_id : ""));
		if (text.Length == 0)
		{
			text = Py.Str(Py.Or(Store.Load()["active_id"], ""));
		}
		return Rewind.RewindLastRound(text);
	}

	public object ai_stop()
	{
        HelL.Core.Launcher.Ai.Minecraft.MinecraftSession.Stop();
		bool ai_busy = _ai_busy;
		_ai_cancel = true;
		Action ai_http_abort = _ai_http_abort;
		if (ai_http_abort != null)
		{
			try
			{
				ai_http_abort();
			}
			catch (Exception)
			{
			}
		}
		ai_confirm(false);
		ai_answer();
		return PyDict.Of("ok", true, "busy", ai_busy);
	}

	public object ai_confirm([Def(false)] object ok = null, [Def(false)] object always = null, [Def("instance")] object scope = null)
	{
		object ai_confirm_ok = Py.Truthy(ok);
		(string, PyDict)? ai_confirm_ctx = _ai_confirm_ctx;
		if (Py.Truthy(ok) && Py.Truthy(always) && ai_confirm_ctx.HasValue)
		{
			(string, PyDict) value = ai_confirm_ctx.Value;
			string item = value.Item1;
			PyDict item2 = value.Item2;
			ai_confirm_ok = new AiRule(item, Permission.RuleContentFromInput(item2 ?? new PyDict()), "allow", Py.Eq(scope, "global") ? "global" : "instance");
		}
		_ai_confirm_ok = ai_confirm_ok;
		_ai_confirm_ev.Set();
		return PyDict.Of("ok", true);
	}

	public object ai_permission_rules()
	{
		return Permission.ListStoredRules();
	}

	public object ai_permission_rule_add(object tool, [Def("allow")] object behavior = null, [Def("")] object content = null, [Def("")] object instance = null)
	{
		if (!(tool is string text) || ToolMeta.Get(text) == null)
		{
			if (tool is List<object> || tool is PyDict)
			{
				throw new TypeError("unhashable type: '" + Py.TypeName(tool) + "'");
			}
			throw new ValueError(Py.Fmt(I18nCore.Tr("未知工具：{0}"), tool));
		}
		string behavior2;
		try
		{
			behavior2 = Permission.BehaviorOf(Py.Str(Py.Or(behavior, "allow")));
		}
		catch (ValueError)
		{
			throw new ValueError(Py.Fmt(I18nCore.Tr("未知行为：{0}"), behavior));
		}
		string text2 = Py.Strip(Py.AsStr(Py.Truthy(content) ? content : "", "strip"));
		Permission.AppendRule(new AiRule(text, (text2.Length > 0) ? text2 : null, behavior2), Py.Truthy(instance) ? Py.Str(instance) : null);
		return Permission.ListStoredRules();
	}

	public object ai_permission_rule_remove(object key, [Def("")] object instance = null)
	{
		Permission.RemoveRule(key, Py.Truthy(instance) ? Py.Str(instance) : "");
		return Permission.ListStoredRules();
	}

	public object ai_answer(object result = null)
	{
		_ai_ask_result = result;
		_ai_ask_ev.Set();
		return PyDict.Of("ok", true);
	}

	public object ai_steer(object text)
	{
		string text2 = Py.Strip(Py.Str(Py.Truthy(text) ? text : ""));
		if (text2.Length == 0)
		{
			return PyDict.Of("ok", false, "message", I18nCore.Tr("内容为空"));
		}
		lock (_ai_lock)
		{
			if (!_ai_busy)
			{
				return PyDict.Of("ok", false, "message", I18nCore.Tr("当前没有在跑的回合"));
			}
			_ai_steer.Add(text2);
		}
		return PyDict.Of("ok", true, "queued", (long)_ai_steer.Count);
	}

	public object test_ai_connection(object settings = null)
	{
		return Client.TestConnection(_with_stored_secrets(settings));
	}

	public object ai_vision_status(object settings = null, object force = null)
	{
		VisionCaps.Result r = VisionCaps.Detect(_with_stored_secrets(settings), Py.Truthy(force));
		return PyDict.Of("supported", r.Supported, "source", r.Source, "model", r.Model, "mode", VisionCaps.Mode, "text", VisionCaps.Describe(r));
	}

	public object ai_vision_set_mode(object mode = null)
	{
		VisionCaps.Mode = Py.S(mode);
		return PyDict.Of("ok", true, "mode", VisionCaps.Mode);
	}

	public object ai_list_models(object settings = null)
	{
		PyDict settings2 = _with_stored_secrets(settings);
		List<object> list;
		try
		{
			list = Client.ListModels(settings2);
		}
		catch (AIClientError aIClientError)
		{
			return PyDict.Of("ok", false, "models", new List<object>(), "error", aIClientError.Message);
		}
		catch (Exception e)
		{
			return PyDict.Of("ok", false, "models", new List<object>(), "error", "拉取模型列表失败：" + Py.ExcStr(e));
		}
		return PyDict.Of("ok", true, "models", list, "error", "");
	}

	public object ai_list_chats()
	{
		PyDict pyDict = _localize_chats(Store.Load());
		pyDict["busy"] = _ai_busy;
		pyDict["pending_card"] = ai_pending_card();
		return pyDict;
	}

	public object ai_pending_card()
	{
		PyDict pyDict = _ai_pending_card;
		if (pyDict == null || pyDict.Count == 0 || !_ai_busy)
		{
			return null;
		}
		return pyDict.Copy();
	}

	public object ai_new_chat()
	{
		PyDict data = Store.Load();
		Store.NewChat(data);
		return _localize_chats(data);
	}

	public object ai_delete_chat(object chat_id)
	{
		PyDict data = Store.Load();
		Store.DeleteChat(data, chat_id);
		return _localize_chats(data);
	}

	public object ai_set_active(object chat_id)
	{
		PyDict data = Store.Load();
		Store.SetActive(data, chat_id);
		return _localize_chats(data);
	}

	internal static PyDict _localize_chats(PyDict data)
	{
		if (data == null)
		{
			return data;
		}
		List<object> list = new List<object>();
		foreach (object item2 in Py.ListOr(Py.Or(data.Get("chats"), new List<object>())))
		{
			object item;
			if ((item = item2) is PyDict pyDict && Py.In(pyDict.Get("title"), Py.L("", "新对话")))
			{
				PyDict pyDict2 = pyDict.Copy();
				pyDict2["title"] = I18nCore.Tr("新对话");
				item = pyDict2;
			}
			list.Add(item);
		}
		PyDict pyDict3 = data.Copy();
		pyDict3["chats"] = list;
		return pyDict3;
	}

	internal static string _stop_note(AgentResult result)
	{
		string text = result?.stop_reason;
		if (text == null || text == "completed")
		{
			return "";
		}
		string text2 = Py.Strip(result.detail ?? "");
		string text3 = text switch
		{
			"no_tool_call" => I18nCore.Tr("它没有真的开始执行：模型只回了文字，没有调用任何工具。"),
			"max_rounds" => I18nCore.Tr("步骤太多，先停在这里。你可以让我继续。"),
			"pending_task" => I18nCore.Tr("下载/安装还在后台跑，可以在「下载任务」里看进度。"),
			"stream_failed" => I18nCore.Tr("接口这轮没有返回内容，已停止。"),
			"empty_response" => I18nCore.Tr("接口返回了空回复。"),
			_ => null,
		};
		if (string.IsNullOrEmpty(text3))
		{
			return "";
		}
		if (text2.Length > 0 && !text3.Contains(text2))
		{
			text3 = text3 + I18nCore.Tr("（") + text2 + I18nCore.Tr("）");
		}
		return I18nCore.Tr("（提示：") + text3 + I18nCore.Tr("）");
	}

	public object ai_send(object text, [Def("")] object chat_id = null, object launch = null, object context = null, object images = null)
	{
		lock (_ai_lock)
		{
			if (_ai_busy)
			{
				return PyDict.Of("ok", false, "message", I18nCore.Tr("上一条还在处理"));
			}
			_ai_busy = true;
			_ai_cancel = false;
			_ai_steer = new List<object>();
		}
		_ui_launch = Py.DictCtor(Py.Truthy(launch) ? launch : new PyDict());
		Thread thread = new Thread((ThreadStart)delegate
		{
			_ai_run(text, chat_id, context, images);
		});
		thread.IsBackground = true;
		thread.Name = "ai-send";
		thread.Start();
		return PyDict.Of("ok", true, "started", true);
	}

	private void _ai_run(object text0, object chat_id, object context, object images = null)
	{
		string text1 = ((text0 == null) ? null : ((text0 as string) ?? Py.Str(text0)));
		PyDict pyDict = Store.Load();
		if (Py.Truthy(chat_id))
		{
			Store.SetActive(pyDict, chat_id);
		}
		string runCid = Py.S(pyDict.Get("active_id"));
		List<object> history = Store.ApiMessages(Py.ListOr(Py.Or((Store.GetChat(pyDict, runCid) ?? new PyDict()).Get("messages"), new List<object>())));
		HttpCancel httpCancel = new HttpCancel();
		_ai_http_abort = httpCancel.Abort;
		List<string> notes = new List<string>();
		StringBuilder buf = new StringBuilder();
		double last = 0.0;
		Timer delayed = null;
		object deltaLock = new object();
		List<object> unsent = new List<object>();
		bool released = false;
		PyDict pyDict2 = _settings_full().Copy();
		pyDict2["ai_session_id"] = ((runCid.Length > 0) ? runCid : "active");
		if (Py.Truthy(context))
		{
			pyDict2["ai_extra_context"] = Py.Str(context);
		}
		try
		{
			AgentResult agentResult = Agent.RunAgent(this, pyDict2, history, text1, OnDelta, OnStatus, ConfirmFn, AskFn, Cancelled, httpCancel, DrainInputs, images as List<object>);
			FlushDelta(force: true);
			if (_ai_cancel)
			{
				throw new AgentCancelled();
			}
			string text2 = agentResult?.Text ?? "";
			List<string> list;
			lock (notes)
			{
				list = new List<string>(notes);
			}
			if (list.Count > 0)
			{
				string text3 = I18nCore.Tr("（本轮：{0}）").Replace("{0}", string.Join(I18nCore.Tr("；"), list.Take(8)));
				if (!text2.Contains(text3))
				{
					text2 = Py.Strip(text2 + "\n\n" + text3);
				}
			}
			string text4 = _stop_note(agentResult);
			string text5 = ((text4.Length > 0) ? (text2 + text4) : text2);
			PyDict pyDict3 = PyDict.Of("role", "assistant", "content", text2);
			if (text4.Length > 0)
			{
				pyDict3["note"] = text4;
			}
			(List<object> compacts, List<object> rows) tuple = TurnCompactAndTrajectory(agentResult);
			List<object> item = tuple.compacts;
			List<object> item2 = tuple.rows;
			List<object> list2 = new List<object>(item);
			list2.Add(PyDict.Of("role", "user", "content", text1));
			List<object> list3 = list2;
			list3.AddRange(item2);
			list3.Add(pyDict3);
			PyDict pyDict4 = Persist(list3);
			Release();
			try
			{
				if (agentResult.plan != null && agentResult.plan.Count > 0)
				{
					Store.SetPlan(Store.Load(), runCid, PyDict.Of("turn_id", runCid, "items", new List<object>(agentResult.plan)));
				}
			}
			catch (Exception)
			{
			}
			_bus.Emit("ai.done", PyDict.Of("text", text5, "note", text4, "store", pyDict4, "chat_id", runCid, "stop_reason", agentResult.stop_reason, "detail", agentResult.detail ?? "", "pending_tasks", new List<object>(agentResult.pending_tasks ?? new List<object>()), "plan", new List<object>(agentResult.plan ?? new List<object>()), "usage", (agentResult.usage ?? new PyDict()).Copy(), "unsent", new List<object>(unsent)));
		}
		catch (AgentCancelled)
		{
			Fail(I18nCore.Tr("已停止"), stopped: true);
		}
		catch (AIClientError aIClientError)
		{
			Fail(aIClientError.Message, stopped: false);
		}
		catch (Exception e)
		{
			Fail(Py.ExcStr(e), stopped: false);
		}
		finally
		{
			lock (deltaLock)
			{
				if (delayed != null)
				{
					delayed.Dispose();
					delayed = null;
				}
			}
			Release();
		}
		object AskFn(List<object> questions, string title)
		{
			if (_ai_cancel)
			{
				return null;
			}
			_ai_ask_ev.Reset();
			_ai_ask_result = null;
			PyDict pyDict5 = PyDict.Of("questions", questions ?? new List<object>(), "title", title ?? "", "chat_id", runCid);
			PyDict pyDict6 = PyDict.Of("kind", "ask");
			foreach (KeyValuePair<string, object> item3 in pyDict5)
			{
				pyDict6[item3.Key] = item3.Value;
			}
			_ai_pending_card = pyDict6;
			_bus.Emit("ai.ask", pyDict5);
			try
			{
				if (!WaitCard(_ai_ask_ev))
				{
					return null;
				}
				return _ai_ask_result;
			}
			finally
			{
				_ai_pending_card = null;
			}
		}
		bool Cancelled()
		{
			return _ai_cancel;
		}
		object ConfirmFn(string name, PyDict args, string label, string reason)
		{
			if (_ai_cancel)
			{
				return false;
			}
			_ai_confirm_ev.Reset();
			_ai_confirm_ctx = (name, (args ?? new PyDict()).Copy());
			PyDict pyDict5;
			try
			{
				pyDict5 = Preview.ChangePreview(this, name, args ?? new PyDict());
			}
			catch (Exception)
			{
				pyDict5 = null;
			}
			ToolMetaInfo toolMetaInfo = ToolMeta.Get(name);
			PyDict pyDict6 = PyDict.Of("name", name, "args", args ?? new PyDict(), "label", label, "reason", reason ?? "", "rule_content", Py.Or(Permission.RuleContentFromInput(args ?? new PyDict()), ""), "preview", pyDict5 ?? new PyDict(), "allow_always", Py.S(pyDict2.Get("ai_mode")) != "local" && toolMetaInfo != null && toolMetaInfo.side_effect != "delete", "chat_id", runCid);
			PyDict pyDict7 = PyDict.Of("kind", "confirm");
			foreach (KeyValuePair<string, object> item4 in pyDict6)
			{
				pyDict7[item4.Key] = item4.Value;
			}
			_ai_pending_card = pyDict7;
			_bus.Emit("ai.confirm", pyDict6);
			try
			{
				if (!WaitCard(_ai_confirm_ev))
				{
					return false;
				}
				return _ai_confirm_ok;
			}
			finally
			{
				_ai_pending_card = null;
			}
		}
		List<object> DrainInputs()
		{
			lock (_ai_lock)
			{
				List<object> result = new List<object>(_ai_steer);
				_ai_steer = new List<object>();
				return result;
			}
		}
		void Fail(string shown, bool stopped)
		{
			FlushDelta(force: true);
			Release();
			try
			{
				Persist(Py.L(PyDict.Of("role", "user", "content", text1), PyDict.Of("role", "error", "content", shown)));
			}
			catch (Exception)
			{
			}
			_bus.Emit("ai.fail", PyDict.Of("text", shown, "stopped", stopped, "chat_id", runCid, "unsent", new List<object>(unsent)));
		}
		void FlushDelta(bool force)
		{
			lock (deltaLock)
			{
				if (buf.Length != 0)
				{
					double num = Py.Monotonic();
					if (!force && num - last < 0.033)
					{
						if (delayed == null)
						{
							delayed = new Timer(delegate
							{
								FlushDelta(force: true);
							}, null, 33, -1);
						}
					}
					else
					{
						if (delayed != null)
						{
							delayed.Dispose();
							delayed = null;
						}
						last = num;
						_bus.Emit("ai.delta", PyDict.Of("text", buf.ToString(), "chat_id", runCid));
						buf.Clear();
					}
				}
			}
		}
		void OnDelta(string piece)
		{
			if (!string.IsNullOrEmpty(piece))
			{
				lock (deltaLock)
				{
					buf.Append(piece);
				}
				FlushDelta(force: false);
			}
		}
		void OnStatus(string kind, PyDict payload)
		{
			payload = payload ?? new PyDict();
			if (kind == "tool_done" && Py.Truthy(payload.Get("label")))
			{
				lock (notes)
				{
					notes.Add(Py.Str(payload.Get("label")));
				}
			}
			PyDict pyDict5 = PyDict.Of("kind", kind, "chat_id", runCid);
			foreach (KeyValuePair<string, object> item5 in payload)
			{
				pyDict5[item5.Key] = item5.Value;
			}
			_bus.Emit("ai.status", pyDict5);
		}
		PyDict Persist(List<object> newMessages)
		{
			PyDict pyDict5 = Store.Load();
			PyDict chat = Store.GetChat(pyDict5, runCid);
			if (chat != null)
			{
				Store.UpsertMessages(pyDict5, runCid, Py.ListOr(Py.Or(chat.Get("messages"), new List<object>())).Concat<object>(newMessages).ToList());
			}
			return pyDict5;
		}
		void Release()
		{
			if (released)
			{
				return;
			}
			released = true;
			_ai_http_abort = null;
			lock (_ai_lock)
			{
				_ai_busy = false;
				unsent.AddRange(_ai_steer);
				_ai_steer = new List<object>();
			}
		}
		static (List<object> compacts, List<object> rows) TurnCompactAndTrajectory(AgentResult reply)
		{
			List<object> list4 = new List<object>();
			List<object> list5 = new List<object>();
			foreach (object item6 in reply?.turn_messages ?? new List<object>())
			{
				if (item6 is PyDict pyDict5)
				{
					object a = pyDict5.Get("role");
					if (Py.Eq(a, "tool") || (Py.Eq(a, "assistant") && Py.Truthy(pyDict5.Get("tool_calls"))) || Store.IsSteerMessage(pyDict5))
					{
						list5.Add(pyDict5.Copy());
					}
					else if (Py.Eq(a, "user") && Py.S(pyDict5.Get("id")).StartsWith("compact_", StringComparison.Ordinal))
					{
						list4.Add(pyDict5.Copy());
					}
				}
			}
			return (compacts: list4, rows: list5);
		}
		bool WaitCard(ManualResetEventSlim ev)
		{
			while (!ev.Wait(200))
			{
				if (_ai_cancel)
				{
					return false;
				}
			}
			return !_ai_cancel;
		}
	}

	internal string _mods_folder(Instance inst, object version = null)
	{
		if (Py.Truthy(version))
		{
			return VersionSettings.ModsDir(inst, version);
		}
		return PPath.Join(inst.path, "mods");
	}

	public object delete_mod(object instance, object filename, [Def("")] object version = null)
	{
		Instance instance2 = _instance(instance);
		string mods_dir = _mods_folder(instance2, version);
		Mods.DeleteMod(instance2, filename, mods_dir);
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object disable_mod(object instance, object filename, [Def("")] object version = null)
	{
		Instance instance2 = _instance(instance);
		string result = Mods.SetModEnabled(instance2, filename, enabled: false, _mods_folder(instance2, version));
		Emit("ui_changed", new PyDict());
		return result;
	}

	public object enable_mod(object instance, object filename, [Def("")] object version = null)
	{
		Instance instance2 = _instance(instance);
		string result = Mods.SetModEnabled(instance2, filename, enabled: true, _mods_folder(instance2, version));
		Emit("ui_changed", new PyDict());
		return result;
	}

	public object get_installed_mods(object instance, [Def("")] object version = null)
	{
		Instance inst = _instance(instance);
		return (from PyDict r in Mods.ListModEntriesAt(_mods_folder(inst, version))
			where Py.Truthy(r["enabled"])
			select r["filename"]).ToList();
	}

	public object get_installed_mod_entries(object instance, [Def("")] object version = null)
	{
		Instance instance2 = _instance(instance);
		if (Py.Truthy(version))
		{
			return Mods.ListModEntriesAt(_mods_folder(instance2, version));
		}
		return Mods.ListInstanceModEntries(instance2);
	}

	private List<object> ContentNames(object instance, string sub)
	{
		return ((IEnumerable<string>)Mods.ListContentFiles(_instance(instance), sub)).Select((Func<string, object>)((string p) => new PPath(p).Name)).ToList();
	}

	public object get_installed_shaders(object instance)
	{
		return ContentNames(instance, "shaderpacks");
	}

	public object get_installed_resourcepacks(object instance)
	{
		return ContentNames(instance, "resourcepacks");
	}

	public object get_installed_datapacks(object instance)
	{
		return ContentNames(instance, "datapacks");
	}

	private object DeleteContent(object instance, string sub, object filename)
	{
		Mods.DeleteContentFile(_instance(instance), sub, filename);
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object delete_shader(object instance, object filename)
	{
		return DeleteContent(instance, "shaderpacks", filename);
	}

	public object delete_resourcepack(object instance, object filename)
	{
		return DeleteContent(instance, "resourcepacks", filename);
	}

	public object delete_datapack(object instance, object filename)
	{
		return DeleteContent(instance, "datapacks", filename);
	}

	public object get_mods_targets(object instance)
	{
		Instance instance2 = _instance(instance);
		List<object> list = new List<object>();
		list.Add(PyDict.Of("label", I18nCore.Tr("实例共享 mods 目录"), "value", ""));
		List<object> list2 = list;
		foreach (string item in instance2.InstalledIds())
		{
			object a = VersionSettings.Load(instance2, item)["isolation"];
			if (Py.Eq(a, "mods") || Py.Eq(a, "all"))
			{
				list2.Add(PyDict.Of("label", item + " · " + I18nCore.Tr("独立 mods"), "value", item));
			}
		}
		return list2;
	}

	public object get_saves_targets([Def("")] object instance = null)
	{
		Instance instance2 = _instance(instance);
		List<object> list = new List<object>();
		list.Add(PyDict.Of("label", I18nCore.Tr("大锅饭（所有版本共用）"), "value", ""));
		List<object> list2 = list;
		foreach (string item in instance2.InstalledIds())
		{
			object a = VersionSettings.Load(instance2, item)["isolation"];
			if (Py.Eq(a, "saves") || Py.Eq(a, "all"))
			{
				list2.Add(PyDict.Of("label", item + " · " + I18nCore.Tr("独立存档"), "value", item));
			}
		}
		return list2;
	}

	public object open_mods_folder(object instance, [Def("")] object version = null)
	{
		string text = _mods_folder(_instance(instance), version);
		Utils.EnsureDir(text);
		Crash.ShellOpen(text);
		return text;
	}

	public object open_global_mods()
	{
		string path = GlobalMods.Root();
		Utils.EnsureDir(path);
		Crash.ShellOpen(path);
		return null;
	}

	public object get_installed_modpacks(object instance)
	{
		if (_instance(instance).Meta()["modpack"] is PyDict pyDict && Py.Truthy(pyDict["name"]))
		{
			object o = pyDict["name"];
			if (Py.Truthy(pyDict["version"]))
			{
				o = Py.Str(o) + " " + Py.Str(pyDict["version"]);
			}
			return new List<object> { Py.Str(o) };
		}
		return new List<object>();
	}

	public object delete_modpack(object instance, [Def("")] object filename = null, [Def(false)] object purge_instance = null)
	{
		Instance instance2 = _instance(instance);
		if (!(instance2.Meta()["modpack"] is PyDict pyDict) || !Py.Truthy(pyDict["name"]))
		{
			throw new InstanceError(I18nCore.Tr("该实例没有已安装整合包"));
		}
		if (Py.Truthy(purge_instance))
		{
			instance2.Delete();
		}
		else
		{
			instance2.SetMeta("modpack", null);
		}
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object cleaner_preview()
	{
		return Cleaner.Preview();
	}

	public object cleaner_apply(object kinds = null)
	{
		return Cleaner.Apply(kinds);
	}

	public object ensure_thumb(object url)
	{
		return Thumbnails.EnsureThumb(url);
	}

	public object create_desktop_shortcut(object instance, object version, [Def("")] object username = null, [Def("")] object account = null, [Def("")] object name = null)
	{
		return Shortcut.CreateLaunchShortcut(instance, version, username, account, name);
	}

	public object export_modpack(object instance, [Def("")] object dest = null)
	{
		return StartTask("导出整合包 " + Py.Str(instance), delegate(TaskContext ctx)
		{
			Instance instance2 = _instance(instance);
			string dest2 = (Py.Truthy(dest) ? Py.Str(dest) : PPath.Join(Utils.ROOT, "exports", instance2.name + ".mrpack"));
			DownloadManager dm = _dm(ctx);
			return ExportPack.ExportMrpack(instance2, dest2, dm, delegate(string m, long a, long b)
			{
				ctx.Progress(a, b, m);
			});
		});
	}

	public object install_modpack(object name, [Def("Modrinth")] object source = null, object extra = null)
	{
		string title = "安装整合包 " + new PPath(Py.Str(name)).Name;
		PyDict ex = Py.DictOr(Py.Or(extra, new PyDict()));
		return StartTask(title, (TaskContext ctx) => _install_modpack_impl(ctx, name, source, ex));
	}

	private object _install_modpack_impl(TaskContext ctx, object name, object source, PyDict extra)
	{
		Instance instance = _instance(extra["instance"]);
		DownloadManager downloadManager = _dm(ctx);
		object o = Py.Or(extra["path"], name);
		Action<string, long, long> on_progress = downloadManager.on_progress;
		string text = Py.Lower(Py.S(source));
		ctx.Log(I18nCore.Tr("整合包安装引擎：按声明的 Forge/Fabric 版本直装（不依赖残缺的 Maven 列表）"));
		PyDict pyDict;
		if (text.StartsWith("本地", StringComparison.Ordinal) || LocalFs.IsFile(Py.Str(o)))
		{
			string text2 = PPath.Norm(Py.Str(o));
			ctx.Log("从本地文件安装: " + text2);
			ctx.Log("实例: " + instance.name + "  路径: " + instance.path);
			pyDict = ((!(Py.Lower(new PPath(text2).Suffix) == ".mrpack")) ? Modpack.InstallCfZip(downloadManager, text2, instance, on_progress, downloadManager.cancel) : Modpack.InstallMrpack(downloadManager, text2, instance, on_progress, downloadManager.cancel));
		}
		else
		{
			pyDict = _install_modpack_online(ctx, downloadManager, instance, name, source, extra, text);
		}
		if (pyDict != null && Py.Truthy(pyDict["instance"]))
		{
			Config.CONFIG.Set("default_instance", pyDict["instance"]);
			Config.CONFIG.Save();
		}
		ctx.Log("整合包安装完成: " + Py.Str(Py.Or(pyDict?["name"], name)));
		return null;
	}

	public object check_mod_updates(object instance)
	{
		return ModUpdate.CheckUpdates(_instance(instance));
	}

	public object start_mod_updates(object instance)
	{
		return StartTask("检查模组更新 " + Py.Str(instance), delegate(TaskContext ctx)
		{
			Instance instance2 = _instance(instance);
			DownloadManager dm = _dm(ctx);
			List<object> list = ModUpdate.CheckUpdates(instance2, dm);
			if (list.Count == 0)
			{
				return I18nCore.Tr("没有可更新的模组");
			}
			for (int i = 0; i < list.Count; i++)
			{
				PyDict pyDict = (PyDict)list[i];
				ModUpdate.ApplyUpdate(instance2, pyDict, dm);
				ctx.Progress(i + 1, list.Count, Py.Str(Py.Or(pyDict["name"], "")));
			}
			return Py.Fmt(I18nCore.Tr("已更新 {0} 个模组"), (long)list.Count);
		});
	}

	public object apply_mod_update(object instance, object row)
	{
		Instance instance2 = _instance(instance);
		if (!(row is PyDict row2))
		{
			throw new AttributeError("'" + Py.TypeName(row) + "' object has no attribute 'get'");
		}
		string result = ModUpdate.ApplyUpdate(instance2, row2);
		Emit("ui_changed", new PyDict());
		return result;
	}

	private static object Gx(object o, string k)
	{
		return Mods.Gt(o, k);
	}

	private PyDict _lookup_pack(object name, object source)
	{
		string text = Py.Strip(Py.Lower(Py.S(name)));
		foreach (object item in _pack_cache)
		{
			string text2 = Py.Lower(Py.S(Gx(item, "name")));
			string text3 = Py.Lower(Py.S(Gx(item, "slug")));
			string text4 = Py.Lower(Py.S(Gx(item, "id")));
			if (text.Length > 0 && (text == text2 || text == text3 || text == text4))
			{
				return (PyDict)item;
			}
		}
		string text5 = (Py.Lower(Py.AsStr(source, "lower")).StartsWith("curse", StringComparison.Ordinal) ? "curseforge" : "modrinth");
		object[][] pOPULAR_MODPACKS = Catalog.POPULAR_MODPACKS;
		foreach (object[] obj in pOPULAR_MODPACKS)
		{
			string text6 = (string)obj[0];
			string text7 = (string)obj[1];
			object obj2 = obj[2];
			object obj3 = obj[3];
			if (Py.Lower(text6) == text || Py.Lower(Py.Str(obj2)) == text)
			{
				return PyDict.Of("name", text6, "source", text7, "id", (text7 == "curseforge") ? obj2 : null, "slug", (text7 == "curseforge") ? obj3 : obj2);
			}
		}
		return PyDict.Of("name", name, "source", text5, "slug", name);
	}

	private PyDict _lookup_mod(object name, object source)
	{
		string text = Py.Strip(Py.Lower(Py.S(name)));
		foreach (object item in _mod_cache)
		{
			string text2 = Py.Lower(Py.S(Gx(item, "name")));
			string text3 = Py.Lower(Py.S(Gx(item, "slug")));
			string text4 = Py.Lower(Py.S(Gx(item, "id")));
			if (text.Length > 0 && (text == text2 || text == text3 || text == text4))
			{
				return (PyDict)item;
			}
		}
		object[][] pOPULAR_MODS = Catalog.POPULAR_MODS;
		foreach (object[] obj in pOPULAR_MODS)
		{
			string text5 = (string)obj[0];
			string text6 = (string)obj[1];
			object obj2 = obj[2];
			if (Py.Lower(text5) == text || Py.Lower(Py.Str(obj2)) == text)
			{
				return PyDict.Of("name", text5, "source", text6, "id", (text6 == "curseforge") ? obj2 : null, "slug", (text6 != "curseforge") ? obj2 : null);
			}
		}
		string text7 = (Py.Lower(Py.AsStr(source, "lower")).StartsWith("curse", StringComparison.Ordinal) ? "curseforge" : "modrinth");
		return PyDict.Of("name", name, "source", text7, "slug", name);
	}

	private static string _project_url(PyDict hit, string kind = "mod")
	{
		object o = hit["slug"];
		if (Py.Lower(Py.S(hit["source"])).StartsWith("curse", StringComparison.Ordinal))
		{
			string text = Py.S(hit["website_url"]);
			if (text.Length > 0)
			{
				return text;
			}
			if (!Py.Truthy(o))
			{
				return "";
			}
			string text2 = ((kind == "modpack") ? "modpacks" : "mc-mods");
			return "https://www.curseforge.com/minecraft/" + text2 + "/" + Py.Str(o);
		}
		if (Py.Truthy(o))
		{
			return "https://modrinth.com/" + kind + "/" + Py.Str(o);
		}
		return "";
	}

	private PyDict _modpack_row(PyDict hit, string default_source = "")
	{
		string a = Py.Lower(Py.Str(Py.Or(hit["source"], default_source, "")));
		var row = PyDict.Of("name", Py.Or(hit["title"], hit["name"], "?"), "author", Py.Or(hit["author"], "?"), "downloads", Py.Int(Py.Or(hit["downloads"], 0L)), "id", hit["id"], "slug", hit["slug"], "source", Py.Or(a, default_source), "description", Py.Or(hit["description"], ""), "icon_url", Py.Or(hit["icon_url"], ""), "project_url", _project_url(hit, "modpack"));
        row.Update(CatalogPresentation.Artwork(hit));
        foreach (string field in new[] { "kind", "chinese_name", "name_localization", "search_aliases" })
            if (hit.ContainsKey(field)) row[field] = hit[field];
        row["original_description"] = Py.Or(hit.Get("original_description"), hit.Get("description"), Py.Or(hit.Get("summary"), ""));
        CatalogPresentation.Localize(row);
        return row;
	}

	private static string _catalog_source(object source)
	{
		string text = Py.Lower(Py.Strip(Py.S(source)));
		if ((text != null && text.Length == 0) || text == "全部" || text == "all")
		{
			return "all";
		}
		if (text.StartsWith("curse", StringComparison.Ordinal))
		{
			return "curseforge";
		}
		return "modrinth";
	}

	private static List<object> _gather_hits(List<KeyValuePair<string, Func<List<object>>>> fetchers)
	{
		List<object> list = new List<object>();
		List<Exception> list2 = new List<Exception>();
		foreach (KeyValuePair<string, Func<List<object>>> fetcher in fetchers)
		{
			try
			{
				foreach (object item2 in fetcher.Value() ?? new List<object>())
				{
					if (item2 is PyDict pyDict)
					{
						pyDict.SetDefault("source", fetcher.Key);
						list.Add(pyDict);
					}
				}
			}
			catch (Exception item)
			{
				list2.Add(item);
			}
		}
		if (list2.Count > 0 && list2.Count == fetchers.Count)
		{
			ExceptionDispatchInfo.Capture(list2[0]).Throw();
		}
		return list;
	}

	private PyDict _content_row(PyDict hit, string default_source = "")
	{
		object obj = Py.Or(hit["source"], default_source);
		var row = PyDict.Of("name", Py.Or(hit["title"], hit["name"], "?"), "author", Py.Or(hit["author"], "?"), "downloads", Py.Int(Py.Or(hit["downloads"], 0L)), "id", hit["id"], "slug", hit["slug"], "source", obj, "description", Py.Or(hit["description"], hit["summary"], ""), "tags", Py.Or(hit["tags"], new List<object>()), "updated", Py.Or(hit["updated"], ""), "icon_url", Py.Or(hit["icon_url"], ""), "project_url", _project_url(hit));
        row.Update(CatalogPresentation.Artwork(hit));
        foreach (string field in new[] { "kind", "chinese_name", "name_localization", "search_aliases" })
            if (hit.ContainsKey(field)) row[field] = hit[field];
        row["original_description"] = Py.Or(hit.Get("original_description"), hit.Get("description"), Py.Or(hit.Get("summary"), ""));
        CatalogPresentation.Localize(row);
        return row;
	}

	private static object GvFilter(PyDict extra)
	{
		object obj = Py.Or(extra["game_version"], extra["version"], "");
		if (obj is string text && text.StartsWith("全部", StringComparison.Ordinal))
		{
			obj = "";
		}
		return obj;
	}

	private static PyDict ExtraOr(object extra)
	{
		object obj = Py.Or(extra, new PyDict());
		if (obj is PyDict result)
		{
			return result;
		}
		throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'get'");
	}

	public object search_modpacks(object query, object source, object extra = null)
	{
		return _pack_cache = SearchCatalog("modpack", query, source, extra);
	}

	private List<object> _popular_mods_offline(string src)
	{
		List<object> list = new List<object>();
		object[][] pOPULAR_MODS = Catalog.POPULAR_MODS;
		foreach (object[] obj in pOPULAR_MODS)
		{
			string text = (string)obj[0];
			string text2 = (string)obj[1];
			object obj2 = obj[2];
			if (!(src != "all") || !(text2 != src))
			{
				list.Add(PyDict.Of("name", text, "author", "", "downloads", 0L, "id", (text2 == "curseforge") ? obj2 : null, "slug", (text2 == "curseforge") ? null : obj2, "source", text2));
			}
		}
		_mod_cache = list;
		return list;
	}

	public object search_mods(object query, object source, object extra = null)
	{
		return _mod_cache = SearchCatalog("mod", query, source, extra);
	}

	private List<object> _search_content(string kind, object query, object source, object extra)
	{
		return SearchCatalog(kind, query, source, extra);
	}

	public object search_shaders(object query, object source, object extra = null)
	{
		return _search_content("shader", query, source, extra);
	}

	public object search_resourcepacks(object query, object source, object extra = null)
	{
		return _search_content("resourcepack", query, source, extra);
	}

	public object search_datapacks(object query, object source, object extra = null)
	{
		return _search_content("datapack", query, source, extra);
	}

	public object list_catalog_files(object extra = null)
	{
		return CatalogFiles.ListProjectFiles(new DownloadManager(2L), Py.DictCtor(Py.Or(extra, new PyDict())));
	}

	public object search_worlds(object query, [Def("CurseForge")] object source = null, object extra = null)
	{
		PyDict pyDict = Py.DictCtor(Py.Or(extra, new PyDict()));
		pyDict.SetDefault("source", source);
		return SearchCatalog("world", query, source ?? "CurseForge", pyDict);
	}

	private static string TitleName(object name)
	{
		return new PPath(Py.Str(name)).Name;
	}

	public object install_mod(object name, [Def("default")] object instance = null, object extra = null)
	{
		object ex = Py.Or(extra, new PyDict());
		return StartTask("安装模组 " + TitleName(name), (TaskContext ctx) => _install_mod_impl(ctx, name, instance, ex));
	}

	public object install_shader(object name, [Def("default")] object instance = null, object extra = null)
	{
		object ex = Py.Or(extra, new PyDict());
		return StartTask("安装光影 " + TitleName(name), (TaskContext ctx) => _install_content_impl(ctx, "shader", name, instance, ex));
	}

	public object install_resourcepack(object name, [Def("default")] object instance = null, object extra = null)
	{
		object ex = Py.Or(extra, new PyDict());
		return StartTask("安装资源包 " + TitleName(name), (TaskContext ctx) => _install_content_impl(ctx, "resourcepack", name, instance, ex));
	}

	public object install_datapack(object name, [Def("default")] object instance = null, object extra = null)
	{
		object ex = Py.Or(extra, new PyDict());
		return StartTask("安装数据包 " + TitleName(name), (TaskContext ctx) => _install_content_impl(ctx, "datapack", name, instance, ex));
	}

	public object install_world(object name, [Def("default")] object instance = null, object extra = null)
	{
		object ex = Py.Or(extra, new PyDict());
		return StartTask("安装世界 " + TitleName(name), (TaskContext ctx) => _install_world_impl(ctx, name, instance, ex));
	}

	private string _install_mod_impl(TaskContext ctx, object name, object instance, object extra0)
	{
		PyDict pyDict = ExtraOr(extra0);
		Instance instance2 = _instance(Py.Or(instance, pyDict["instance"]));
		DownloadManager downloadManager = _dm(ctx);
		Action<string, long, long> on_progress = downloadManager.on_progress;
		string text = Py.Lower(Py.S(pyDict["source"]));
		object version_id = pyDict["version_id"];
		object obj = pyDict["file_id"];
		object mc_version = Py.Or(pyDict["game_version"], pyDict["mc_version"]);
		List<object> list = new List<object>();
		if (Py.Truthy(pyDict["path"]) || Py.Truthy(pyDict["url"]))
		{
			object o = Py.Or(pyDict["path"], pyDict["url"]);
			ctx.Log("安装模组: " + Py.Str(o));
			Mods.InstallModFromSource(downloadManager, Py.Str(o), instance2, null, null, on_progress, version_id);
		}
		else if (text.StartsWith("curse", StringComparison.Ordinal) && Py.Truthy(pyDict["id"]))
		{
			ctx.Log("从 CurseForge 安装模组 id=" + Py.Str(pyDict["id"]));
			list = Py.ListOr(Py.Or(Py.DictOr(Py.Or(Mods.InstallCurseforgeMod(downloadManager, pyDict.Req("id"), instance2, mc_version, null, null, on_progress, obj), new PyDict()))["dependencies"], new List<object>()));
		}
		else
		{
			PyDict pyDict2 = (Py.Truthy(pyDict["slug"]) ? pyDict : _lookup_mod(Py.Str(name), Py.Or(pyDict["source"], "Modrinth")));
			if (Py.Truthy(pyDict2["id"]) && Py.Lower(Py.Str(Py.Or(pyDict2["source"], text))).StartsWith("curse", StringComparison.Ordinal))
			{
				ctx.Log("从 CurseForge 安装模组 id=" + Py.Str(pyDict2["id"]));
				list = Py.ListOr(Py.Or(Mods.InstallCurseforgeMod(downloadManager, pyDict2.Req("id"), instance2, mc_version, null, null, on_progress, Py.Or(obj, pyDict["version_id"]))["dependencies"], new List<object>()));
			}
			else
			{
				object o2 = Py.Or(pyDict2["slug"], name);
				ctx.Log("从 Modrinth 安装模组 " + Py.Str(o2));
				Mods.InstallModFromSource(downloadManager, Py.Str(o2), instance2, mc_version, null, on_progress, version_id);
			}
		}
		if (list.Count > 0)
		{
			ctx.Log(I18nCore.Tr("已随装前置：") + string.Join("、", list.Select(Py.Str)));
		}
		ctx.Log(I18nCore.Tr("模组安装完成"));
		return null;
	}

	private string _install_content_impl(TaskContext ctx, string kind, object name, object instance, object extra0)
	{
		PyDict pyDict = Py.DictCtor(Py.Or(extra0, new PyDict()));
		pyDict.SetDefault("name", name);
		pyDict.SetDefault("slug", Py.Or(pyDict["slug"], name));
		Instance instance2 = _instance(Py.Or(instance, pyDict["instance"]));
		PyDict pyDict2 = Mods.CONTENT_KINDS[kind];
		DownloadManager downloadManager = _dm(ctx);
		ctx.Log("安装到 " + instance2.name + "/" + Py.Str(pyDict2["subdir"]));
		List<object> source = Py.ListOr(Py.Or(Py.DictOr(Py.Or(Mods.InstallContentFromSource(downloadManager, instance2, (string)pyDict2["subdir"], pyDict, downloadManager.on_progress), new PyDict()))["files"], new List<object>()));
		string text = string.Join(", ", source.Select(Py.Str));
		ctx.Log("完成: " + ((text.Length > 0) ? text : Py.Str(name)));
		if (kind == "datapack")
		{
			ctx.Log(I18nCore.Tr("数据包已放到实例 datapacks 目录，请复制到对应存档的 datapacks 文件夹后进入世界。"));
		}
		return null;
	}

	private string _install_world_impl(TaskContext ctx, object name, object instance, object extra0)
	{
		PyDict pyDict = Py.DictCtor(Py.Or(extra0, new PyDict()));
		pyDict.SetDefault("name", name);
		Instance instance2 = _instance(Py.Or(instance, pyDict["instance"]));
		DownloadManager downloadManager = _dm(ctx);
		string version_id = Py.S(pyDict["version"]);
		ctx.Log("安装世界到 " + Worlds.SavesRoot(instance2, version_id));
		List<object> source = Py.ListOr(Py.Or(Py.DictOr(Py.Or(Worlds.InstallWorld(downloadManager, pyDict, instance2, downloadManager.on_progress, version_id), new PyDict()))["files"], new List<object>()));
		string text = string.Join(", ", source.Select(Py.Str));
		ctx.Log("完成: " + ((text.Length > 0) ? text : Py.Str(name)));
		return Py.Fmt(I18nCore.Tr("已安装世界 {0}"), (text.Length > 0) ? text : Py.Str(name));
	}

	private PyDict _install_modpack_online(TaskContext ctx, DownloadManager dm, Instance inst, object name, object source, PyDict extra, string srcL)
	{
		Action<string, long, long> on_progress = dm.on_progress;
		if (srcL.StartsWith("curse", StringComparison.Ordinal))
		{
			PyDict pyDict = ((Py.Truthy(extra["id"]) || Py.Truthy(extra["slug"])) ? extra : _lookup_pack(name, source));
			object obj = pyDict["id"];
			object obj2 = pyDict["slug"];
			if (!Py.Truthy(obj) && !Py.Truthy(obj2))
			{
				throw new RuntimeError(Py.Fmt(I18nCore.Tr("无法解析整合包: {0}"), name));
			}
			ctx.Log("从 CurseForge 安装 " + Py.Str(Py.Or(pyDict["name"], name)) + " (id=" + Py.Str(obj) + " slug=" + Py.Str(obj2) + ")");
			ctx.Log("实例: " + inst.name + "  路径: " + inst.path);
			if (Py.Str(obj) == Py.Str(1238396L) || Py.S(obj2) == "create-the-brass-concerto")
			{
				ctx.Log(I18nCore.Tr("目标包：机械动力：黄铜协奏曲（CBC），Minecraft 1.20.1 Forge。这不是 Create+ / CDC。"));
			}
			else if (Py.Str(obj) == Py.Str(1059094L) || Py.S(obj2) == "create-delight-remake")
			{
				ctx.Log(I18nCore.Tr("目标包：机械动力：齿轮盛宴（CDC），Minecraft 1.20.1 Forge。"));
			}
			if (Py.DictOr(Py.Or(inst.Meta(), new PyDict()))["modpack"] is PyDict pyDict2 && Py.Truthy(pyDict2["name"]))
			{
				ctx.Log("注意：实例 " + inst.name + " 当前已是 " + Py.Str(pyDict2["name"]) + " " + Py.Str(Py.Or(pyDict2["version"], "")) + " / " + Py.Str(Py.Or(pyDict2["mc_version"], "")) + "。覆盖安装会混入旧模组，建议先新建实例再装。");
			}
			return Modpack.InstallCfModpack(dm, obj, inst, Config.CurseforgeKey(), on_progress, dm.cancel, obj2, Py.Or(extra["file_id"], extra["version_id"]));
		}
		PyDict pyDict3 = (Py.Truthy(extra["slug"]) ? extra : _lookup_pack(name, source));
		object obj3 = Py.Or(pyDict3["slug"], name);
		ctx.Log("从 Modrinth 安装 " + Py.Str(Py.Or(pyDict3["name"], obj3)) + " (" + Py.Str(obj3) + ")");
		ctx.Log("实例: " + inst.name + "  路径: " + inst.path);
		return Modpack.InstallMrpackBySlug(dm, obj3, inst, on_progress, dm.cancel, force: false, null, extra["version_id"]);
	}

	public BackendApi(EventBus bus)
	{
		_bus = bus ?? new EventBus();
		InitAccounts();
		EnsureDefaultInstance();
		ThreadPool.QueueUserWorkItem(_ =>
		{
			try { LocalModels.Resume(); }
			catch (Exception ex) { System.Diagnostics.Trace.TraceError("Local model queue startup: " + ex); }
		});
		// 启动后在后台预热微软登录域名（系统代理探测 + DNS + TLS），点「微软登录」时验证码能更快出来
		ThreadPool.QueueUserWorkItem(delegate
		{
			Thread.Sleep(1500);
			AuthHttp.Warmup("https://login.live.com/");
		});
	}

	private void InitAccounts()
	{
		accounts = new AccountManager();
	}

	private void EnsureDefaultInstance()
	{
		if (Instances.ListInstances().Count > 0)
		{
			return;
		}
		object name = Py.Or(Config.CONFIG.Get("default_instance", "default"), "default");
		try
		{
			new Instance(name).Create();
		}
		catch (InstanceError)
		{
		}
	}

	private void TerracottaStop()
	{
		Terracotta.Stop();
	}

	public void invalidate_instances()
	{
		_inst_cache = null;
	}

	public void Emit(string @event, PyDict data)
	{
		switch (@event)
		{
		case "ui_changed":
		case "finished":
		case "instance_changed":
			_inst_cache = null;
			break;
		}
		if (@event == "crash")
		{
			object obj = (data ?? new PyDict()).Get("task_id");
			if (Py.Truthy(obj))
			{
				lock (_crashes)
				{
					_crashes[Py.HashKey(obj)] = data ?? new PyDict();
					if (_crashes.Count > 40)
					{
						foreach (string item in _crashes.Keys.Take(_crashes.Count - 20).ToList())
						{
							_crashes.Remove(item);
						}
					}
				}
			}
			_bus.Emit("crash", data);
		}
		else if (@event == "finished")
		{
			object obj2 = data.Get("task_id");
			string tid = ((obj2 == null) ? null : Py.HashKey(obj2));
			lock (_resultsLock)
			{
				int num = _task_results.FindIndex((KeyValuePair<string, (bool ok, string msg)> kv) => kv.Key == tid);
				(bool, string) value = (Py.Truthy(data.Get("success")), Py.S(data.Get("message")));
				if (num >= 0)
				{
					_task_results[num] = new KeyValuePair<string, (bool, string)>(tid, value);
				}
				else
				{
					_task_results.Add(new KeyValuePair<string, (bool, string)>(tid, value));
				}
				if (_task_results.Count > 80)
				{
					_task_results.RemoveRange(0, _task_results.Count - 40);
				}
			}
			int count;
			lock (_lock)
			{
				if (tid != null)
				{
					_workers.Remove(tid);
				}
				count = _workers.Count;
			}
			_bus.Emit("finished", data);
			_bus.Emit("task_count_changed", PyDict.Of("count", (long)count));
			if (Py.Truthy(data.Get("success")))
			{
				_bus.Emit("ui_changed", new PyDict());
			}
		}
		else
		{
			_bus.Emit(@event, data);
		}
	}

	public string StartTask(object title, Func<TaskContext, object> fn)
	{
		string text = $"task-{Interlocked.Increment(ref _counter)}";
		BackendWorker backendWorker = new BackendWorker(text, fn, Emit);
		int count;
		lock (_lock)
		{
			_workers[text] = backendWorker;
			_titles[text] = title;
			count = _workers.Count;
		}
		Emit("task_added", PyDict.Of("task_id", text, "title", title));
		Emit("task_count_changed", PyDict.Of("count", (long)count));
		backendWorker.Start();
		return text;
	}

	public string start_task(object title, object fn, [VarArgs] List<object> args = null, [VarKw] PyDict kwargs = null)
	{
		Func<TaskContext, object> fn2 = (fn as Func<TaskContext, object>) ?? ((Func<TaskContext, object>)delegate
		{
			throw Py.NotCallable(fn);
		});
		return StartTask(title, fn2);
	}

	private static string TaskKey(object task_id)
	{
		if (task_id is PyDict || task_id is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(task_id) + "'");
		}
		if (task_id != null)
		{
			return Py.HashKey(task_id);
		}
		return null;
	}

	public object cancel_task(object task_id)
	{
		string text = TaskKey(task_id);
		BackendWorker value = null;
		lock (_lock)
		{
			if (text != null)
			{
				_workers.TryGetValue(text, out value);
			}
		}
		value?.cancel();
		bool flag;
		if (task_id != null)
		{
			if (!(task_id is string text2))
			{
				goto IL_00ac;
			}
			flag = text2 == _launch_task_id;
		}
		else
		{
			flag = _launch_task_id == null;
		}
		if (flag)
		{
			Process game_proc;
			lock (_game_lock)
			{
				game_proc = _game_proc;
			}
			if (game_proc != null)
			{
				try
				{
					game_proc.Kill();
				}
				catch (Exception)
				{
				}
			}
			return null;
		}
		goto IL_00ac;
		IL_00ac:
		return null;
	}

	public object task_title(object task_id)
	{
		string text = TaskKey(task_id);
		lock (_lock)
		{
			if (text != null && _titles.TryGetValue(text, out object value))
			{
				return value;
			}
		}
		return task_id;
	}

	public PyDict shutdown([Def(800L)] object timeout_ms = null)
	{
        HelL.Core.Launcher.Ai.Minecraft.MinecraftSession.Stop();
        HelL.Core.Launcher.Ai.Minecraft.MinecraftModels.Stop();
		long val = Py.Int(timeout_ms);
		double num = Py.Monotonic() + (double)Math.Max(0L, val) / 1000.0;
		List<KeyValuePair<string, BackendWorker>> list;
		lock (_lock)
		{
			list = _workers.Where((KeyValuePair<string, BackendWorker> kv) => kv.Key != _launch_task_id).ToList();
		}
		foreach (KeyValuePair<string, BackendWorker> item in list)
		{
			item.Value.cancel();
		}
		foreach (KeyValuePair<string, BackendWorker> item2 in list)
		{
			double num2 = num - Py.Monotonic();
			if (num2 <= 0.0)
			{
				break;
			}
			item2.Value.Join(num2);
		}
		try
		{
			TerracottaStop();
		}
		catch (Exception)
		{
		}
		List<object> list2 = new List<object>();
		foreach (KeyValuePair<string, BackendWorker> item3 in list)
		{
			if (item3.Value.IsAlive)
			{
				list2.Add(item3.Key);
			}
		}
		return PyDict.Of("pending", list2);
	}

	public object get_crash([Def("")] object task_id = null)
	{
		lock (_crashes)
		{
			if (Py.Truthy(task_id))
			{
				string key = TaskKey(task_id);
				if (_crashes.ContainsKey(key))
				{
					return _crashes[key];
				}
			}
			if (_crashes.Count > 0)
			{
				return _crashes[_crashes.Keys[_crashes.Count - 1]];
			}
		}
		return new PyDict();
	}

	public PyDict wait_task(object task_id, [Def(1800L)] object timeout = null, object cancelled = null)
	{
		double num = Py.Time();
		string key = TaskKey(task_id);
		while (true)
		{
			lock (_resultsLock)
			{
				int num2 = _task_results.FindIndex((KeyValuePair<string, (bool ok, string msg)> kv) => kv.Key == key);
				if (num2 >= 0)
				{
					var (flag, text) = _task_results[num2].Value;
					return PyDict.Of("ok", flag, "message", text, "task_id", task_id);
				}
			}
			if (Py.Truthy(cancelled) && ((cancelled as Func<bool>) ?? throw Py.NotCallable(cancelled))())
			{
				cancel_task(task_id);
				return PyDict.Of("ok", false, "message", I18nCore.Tr("已停止"), "task_id", task_id);
			}
			if (!Py.IsNum(timeout) && !(timeout is bool))
			{
				throw new TypeError("'>' not supported between instances of 'float' and '" + Py.TypeName(timeout) + "'");
			}
			if (Py.Time() - num > Py.Float(timeout))
			{
				break;
			}
			Thread.Sleep(300);
		}
		return PyDict.Of("ok", false, "message", I18nCore.Tr("等待任务超时"), "task_id", task_id, "timeout", true);
	}

	public PyDict list_tasks()
	{
		List<object> list = new List<object>();
		lock (_lock)
		{
			foreach (string item in _workers.Keys.ToList())
			{
				list.Add(PyDict.Of("task_id", item, "title", _titles.TryGetValue(item, out object value) ? value : item));
			}
		}
		List<object> list2 = new List<object>();
		lock (_resultsLock)
		{
			foreach (KeyValuePair<string, (bool, string)> task_result in _task_results)
			{
				list2.Add(PyDict.Of("task_id", task_result.Key, "success", task_result.Value.Item1, "message", task_result.Value.Item2 ?? ""));
			}
		}
		return PyDict.Of("running", list, "finished", list2);
	}

	public static bool is_download_title(object title)
	{
		string t = Py.S(title);
		return !_INTERACTIVE_TITLES.Any((string k) => t.StartsWith(k, StringComparison.Ordinal) || t.StartsWith(I18nCore.Tr(k), StringComparison.Ordinal));
	}

	internal static string _secret_hint(object value)
	{
		string text = Py.Strip(Py.S(value));
		if (text.Length < 12)
		{
			if (text.Length <= 0)
			{
				return "";
			}
			return "••••";
		}
		return "••••" + text.Substring(text.Length - 4);
	}

	internal static bool _is_builtin_secret(string key, string value)
	{
		if (key == "curseforge_api_key")
		{
			return value == Config.BuiltinCurseforgeKey();
		}
		if (key == "ai_api_key")
		{
			if (!string.IsNullOrEmpty(Defaults.DEFAULT_API_KEY))
			{
				return value == Defaults.DEFAULT_API_KEY;
			}
			return false;
		}
		return false;
	}

	internal static string _norm_theme_color(object value)
	{
		string text = Py.Strip(Py.S(value));
		if (text.Length == 9 && text.StartsWith("#") && text.Substring(1, 2).ToUpperInvariant() == "FF")
		{
			text = "#" + text.Substring(3);
		}
		if (text.Length != 7 || !text.StartsWith("#"))
		{
			return "";
		}
		if (!PyHexOk(text.Substring(1)))
		{
			return "";
		}
		return text.ToUpperInvariant();
	}

	private static bool PyHexOk(string s)
	{
		string text = Py.Strip(s);
		if (text.StartsWith("+") || text.StartsWith("-"))
		{
			text = text.Substring(1);
		}
		if (text.StartsWith("0x") || text.StartsWith("0X"))
		{
			text = text.Substring(2).TrimStart('_');
		}
		if (text.Length == 0 || text.StartsWith("_") || text.EndsWith("_") || text.Contains("__"))
		{
			return false;
		}
		string text2 = text;
		foreach (char c in text2)
		{
			if (c != '_' && !Uri.IsHexDigit(c))
			{
				return false;
			}
		}
		return true;
	}

	internal static string _norm_bg_preset(object value)
	{
		string text = Py.Strip(Py.S(value)).ToLowerInvariant();
		switch (text)
		{
		default:
			return "default";
		case "default":
		case "eye":
		case "warm":
		case "mist":
			return text;
		}
	}

	internal static long _clamp_int(object value, long lo, long hi, long fallback)
	{
		long val;
		try
		{
			if (value is double num && !double.IsNaN(num) && !double.IsInfinity(num) && (num > 9.223372036854776E+18 || num < -9.223372036854776E+18))
			{
				return (num > 0.0) ? Math.Max(lo, hi) : lo;
			}
			if (value is BigInteger bigInteger && (bigInteger > long.MaxValue || bigInteger < long.MinValue))
			{
				return (bigInteger.Sign > 0) ? Math.Max(lo, hi) : lo;
			}
			val = Py.Int(value);
		}
		catch (TypeError)
		{
			return fallback;
		}
		catch (ValueError)
		{
			return fallback;
		}
		return Math.Max(lo, Math.Min(hi, val));
	}

	internal static long _clamp_opacity(object value)
	{
		return _clamp_int(value, 30L, 100L, 100L);
	}

	internal static string _window_aspect(object value)
	{
		string text = Py.Strip(Py.S(value));
		if (Array.IndexOf<string>(_WINDOW_ASPECTS, text) < 0)
		{
			return "4:3";
		}
		return text;
	}

	internal static List<object> _nav_keys(object raw)
	{
		HashSet<string> hashSet = new HashSet<string>();
		List<object> list = new List<object>();
		if (raw is List<object> list2)
		{
			foreach (object item in list2)
			{
				string text = Py.Strip(Py.Str(item));
				if (text.Length > 0 && hashSet.Add(text))
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	internal static PyDict _nav_members(object raw)
	{
		if (!(raw is PyDict pyDict))
		{
			return new PyDict();
		}
		PyDict pyDict2 = new PyDict();
		bool flag = false;
		string[] nAV_SECTIONS = _NAV_SECTIONS;
		foreach (string key in nAV_SECTIONS)
		{
			object obj = (pyDict2[key] = _nav_keys(pyDict.Get(key)));
			if (((List<object>)obj).Count > 0)
			{
				flag = true;
			}
		}
		if (!flag)
		{
			return new PyDict();
		}
		return pyDict2;
	}

	internal static List<object> _nav_groups(object raw)
	{
		List<object> list = new List<object>();
		if (!(raw is List<object> list2))
		{
			return list;
		}
		foreach (object item in list2)
		{
			if (item is PyDict pyDict)
			{
				string text = Py.Strip(Py.S(pyDict.Get("title")));
				if (text.Length > 0)
				{
					list.Add(PyDict.Of("title", text, "keys", _nav_keys(pyDict.Get("keys"))));
				}
			}
		}
		return list;
	}

	public string get_language()
	{
		return I18nCore.CurrentLanguage();
	}

	public object set_language(object lang)
	{
		I18nCore.SetLanguage(lang);
		return null;
	}

	public PyDict available_languages()
	{
		return I18nCore.AvailableLanguages();
	}

	public object translate(object key, [Def("")] object lang = null)
	{
		return I18nCore.TrAny(key, Py.Truthy(lang) ? lang : null);
	}

	public object create_instance(object name)
	{
		new Instance(name, forCreation: true).Create();
		invalidate_instances();
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object delete_instance(object name)
	{
		new Instance(name).Delete();
        invalidate_instances();
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object rename_instance(object name, object new_name)
	{
		new Instance(name).Rename(new_name);
        invalidate_instances();
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object open_instance_folder(object name)
	{
		Crash.ShellOpen(_instance(name).path);
		return null;
	}

	public object game_root_name()
	{
		return Instances.RootName();
	}

	public object game_root_path()
	{
		return _instance().path;
	}

	public object set_game_dir(object path)
	{
		string text = Py.AsStr(path, "__fspath__");
		PPath pPath = new PPath(ExpandUser(text));
		string text2 = (pPath.IsAbsolute ? pPath.ToString() : PPath.Join(Utils.ROOT, text));
		try
		{
			MkdirParents(text2);
			string path2 = PPath.Join(text2, ".tipcl-write-test");
			File.WriteAllText(PPath.Abs(path2), "ok");
			File.Delete(PPath.Abs(path2));
		}
		catch (OSError oSError)
		{
			throw new InstanceError(Py.Fmt(I18nCore.Tr("游戏目录不可写: {0}"), text2) + "\n" + oSError.Message);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			throw new InstanceError(Py.Fmt(I18nCore.Tr("游戏目录不可写: {0}"), text2) + "\n" + Py.ExcStr(ex));
		}
		CONFIG.Set("instances_dir", pPath.IsAbsolute ? pPath.ToString() : text);
		CONFIG.Save();
		Emit("ui_changed", new PyDict());
		return CONFIG.instances_dir;
	}

	private static string ExpandUser(string s)
	{
		if (!s.StartsWith("~"))
		{
			return s;
		}
		int num = s.IndexOfAny(new char[2] { '/', '\\' });
		if (((num < 0) ? s : s.Substring(0, num)) != "~")
		{
			return s;
		}
		string text = (Utils.IS_WINDOWS ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME"));
		if (string.IsNullOrEmpty(text))
		{
			text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		return text + ((num < 0) ? "" : s.Substring(num));
	}

	private static void MkdirParents(string target)
	{
		string path = PPath.Abs(target);
		if (Directory.Exists(path))
		{
			return;
		}
		if (File.Exists(path))
		{
			throw new FileExistsError("[Errno 17] File exists: " + Py.Repr(target));
		}
		PPath pPath = new PPath(target);
		PPath parent = pPath.Parent;
		while (!parent.Equals(pPath))
		{
			string path2 = PPath.Abs(parent.ToString());
			if (Directory.Exists(path2))
			{
				break;
			}
			if (File.Exists(path2))
			{
				throw new OSError("[Errno 20] Not a directory: " + Py.Repr(target));
			}
			pPath = parent;
			parent = pPath.Parent;
		}
		Directory.CreateDirectory(path);
	}

	public object get_installed_versions(object instance, [Def(false)] object include_hidden = null)
	{
		if (Py.Truthy(instance))
		{
			Instance inst = _instance(instance);
			List<object> list = inst.InstalledIds();
			if (Py.Truthy(include_hidden) || Py.Truthy(CONFIG.Get("show_hidden_versions")))
			{
				return list;
			}
			return list.Where((object vid) => !Py.Truthy(VersionSettings.Load(inst, vid)["hidden"])).ToList();
		}
		List<object> list2 = new List<object>();
		foreach (string item in Instances.ListInstances())
		{
			foreach (string item2 in new Instance(item).InstalledIds())
			{
				list2.Add(item + " / " + item2);
			}
		}
		return list2;
	}

	public object loader_of(object version_id)
	{
		string text = Py.Lower(Py.S(version_id));
		string[][] lOADER_TAGS = LOADER_TAGS;
		foreach (string[] array in lOADER_TAGS)
		{
			if (text.Contains(array[0]))
			{
				return new List<object>
				{
					array[1],
					array[2]
				};
			}
		}
		return new List<object>
		{
			I18nCore.Tr("原版"),
			"#8A9099"
		};
	}

	private static string _version_mc_id(Instance inst, string version_id)
	{
		PyDict pyDict = (inst.VersionJson(version_id) as PyDict) ?? new PyDict();
		return Py.Str(Py.Or(pyDict["inheritsFrom"], pyDict["id"], version_id));
	}

	private static long _count_mods(string folder)
	{
		if (!LocalFs.IsDir(folder))
		{
			return 0L;
		}
		return LocalFs.Children(folder).LongCount((string f) => LocalFs.IsFile(f) && Py.Lower(new PPath(f).Name).EndsWith(".jar"));
	}

	public object get_version_rows([Def(false)] object include_hidden = null)
	{
		Instance instance = _instance();
		List<object> list = new List<object>();
		foreach (string item in instance.InstalledIds())
		{
			PyDict pyDict = VersionSettings.Load(instance, item);
			bool flag = Py.Truthy(pyDict["hidden"]);
			if (!flag || Py.Truthy(include_hidden) || Py.Truthy(CONFIG.Get("show_hidden_versions")))
			{
				bool flag2 = VersionSettings.IsIsolated(pyDict);
				string text2 = VersionSettings.ModsDir(instance, item, pyDict);
				List<object> list2 = (List<object>)loader_of(item);
				object obj = Py.Or(pyDict["isolation"], "none");
				object obj2 = ((obj is string key) ? VersionSettings.ISOLATION_LABELS.Get(key, "") : "");
				list.Add(PyDict.Of("id", item, "loader", list2[0], "loader_color", list2[1], "mc", _version_mc_id(instance, item), "isolation", obj, "isolation_label", I18nCore.Tr((string)obj2), "isolated", flag2, "mods", _count_mods(text2), "mods_dir", text2, "hidden", flag, "java", Py.Or(pyDict["java"], "自动选择"), "memory_mb", Py.Or(pyDict["memory_mb"], 0L)));
			}
		}
		return list;
	}

	public object uninstall_version(object spec)
	{
		string text = Py.AsStr(spec, "split");
		int num = text.IndexOf(" / ", StringComparison.Ordinal);
		object name;
		string s;
		if (num >= 0)
		{
			name = text.Substring(0, num);
			s = text.Substring(num + 3);
		}
		else
		{
			name = CONFIG.Get("default_instance", "default");
			s = text;
		}
		new Installer(_instance(name)).UninstallVersion(Py.Strip(s));
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object rename_version(object instance, object version, object new_id)
	{
		return VersionOps.RenameVersion(_instance(instance), version, new_id);
	}

	public object copy_version(object instance, object version, object new_id)
	{
		return VersionOps.CopyVersion(_instance(instance), version, new_id);
	}

	public object hide_version(object instance, object version, [Def(true)] object hidden = null)
	{
		return VersionOps.SetHidden(_instance(instance), version, hidden);
	}

	public object open_version_folder(object instance, [Def("")] object version = null, [Def("root")] object which = null)
	{
		return VersionOps.OpenFolder(_instance(instance), version, which);
	}

	public object get_version_settings(object instance, object version)
	{
		return VersionSettings.Load(_instance(instance), version);
	}

	public object save_version_settings(object instance, object version, object data)
	{
		PyDict result = VersionSettings.Save(_instance(instance), version, Py.Truthy(data) ? data : new PyDict());
		Emit("ui_changed", new PyDict());
		return result;
	}

	public object get_version_isolation(object version)
	{
		return Py.Or(VersionSettings.Load(_instance(), version)["isolation"], "none");
	}

	public object set_version_isolation(object version, object mode, [Def(false)] object seed = null)
	{
		PyDict result = VersionSettings.SetIsolation(_instance(), version, mode, Py.Truthy(seed));
		Emit("ui_changed", new PyDict());
		return result;
	}

	public object toggle_version_isolation(object version, object isolated, [Def(false)] object seed = null)
	{
		string mode = (Py.Truthy(isolated) ? "all" : "none");
		return set_version_isolation(version, mode, seed);
	}

	public object normalize_java_pref(object java)
	{
		if (!Py.Truthy(java) || Py.Eq(java, "自动选择") || Py.Eq(java, "auto") || Py.Eq(java, "default"))
		{
			return "自动选择";
		}
		foreach (object item in AllJavasHook?.Invoke() ?? new List<object>())
		{
			if (item is PyDict pyDict && (Py.Eq(pyDict["name"], java) || Py.Eq(pyDict["exe"], java)))
			{
				return Py.Or(pyDict["exe"], java);
			}
		}
		PPath pPath = new PPath(Py.AsStr(java, "__fspath__"));
		if (pPath.IsFile())
		{
			return pPath.ToString();
		}
		return java;
	}

	public object set_instance_java(object name, object java)
	{
		_instance(name).SetJavaPref(normalize_java_pref(java));
		return null;
	}

	public object allow_multi_instance()
	{
		return Py.Truthy(CONFIG.Get("allow_multi_instance", false));
	}

	public object set_multi_instance(object allow)
	{
		CONFIG.Set("allow_multi_instance", Py.Truthy(allow));
		CONFIG.Save();
		return null;
	}

	public object default_export_dir()
	{
		return ContentExport.DefaultExportDir();
	}

	public object remember_export_dir(object path)
	{
		return ContentExport.RememberExportDir(path);
	}

	static BackendApi()
	{
		_INTERACTIVE_TITLES = new string[3] { "启动游戏", "微软登录", "皮肤站登录" };
		_NAV_SECTIONS = new string[2] { "download", "more" };
		_SECRET_SETTING_KEYS = new string[2] { "curseforge_api_key", "ai_api_key" };
		_PERM_MODES = new HashSet<string>
		{
			"default", "plan", "edit", "acceptEdits", "auto", "dontAsk", "autoEdit", "yolo", "bypassPermissions", "build",
			"custom", "standard", "full"
		};
		_WINDOW_ASPECTS = new string[3] { "4:3", "16:9", "free" };
		LOADER_TAGS = new string[6][]
		{
			new string[3] { "neoforge", "NeoForge", "#D84B28" },
			new string[3] { "fabric", "Fabric", "#7C5CD6" },
			new string[3] { "quilt", "Quilt", "#C25BD6" },
			new string[3] { "forge", "Forge", "#E8862E" },
			new string[3] { "optifine", "OptiFine", "#2E9B6B" },
			new string[3] { "liteloader", "LiteLoader", "#4C8BF5" }
		};
		_B64_OK = new Regex("\\A[A-Za-z0-9+/]*={0,2}\\z");
		_UPLOAD_BAD = new Regex("[<>:\"/\\\\|?*\\x00-\\x1f]");
		AllJavasHook = Java.AllJavas;
	}

	internal DownloadManager _dm(TaskContext ctx)
	{
		string lastKey = "";
		return new DownloadManager(Config.CONFIG.Get("download_threads", 8L), onProgress, () => ctx.Cancelled, null, ctx.Token);
		void onProgress(string message, long done, long total)
		{
			string text = message ?? "";
			ctx.Progress(done, total, text);
			if (text.Contains("  |  "))
			{
				string text2 = Py.Strip(Py.Split(text, "  |  ", 1)[0], " ·");
				if (text2.Length > 0 && text2 != lastKey)
				{
					lastKey = text2;
					ctx.Log(text);
				}
			}
			else
			{
				string text3 = Py.Strip(text);
				if (text3.Length > 0 && text3 != lastKey)
				{
					lastKey = text3;
					ctx.Log(text3);
				}
			}
		}
	}

	private static List<object> ArgList(object o)
	{
		if (o == null)
		{
			return null;
		}
		if (o is string source)
		{
			return ((IEnumerable<char>)source).Select((Func<char, object>)((char c) => c.ToString())).ToList();
		}
		if (o is List<object> result)
		{
			return result;
		}
		if (o is PyDict pyDict)
		{
			return pyDict.Keys.Cast<object>().ToList();
		}
		throw new TypeError("'" + Py.TypeName(o) + "' object is not iterable");
	}

	public object install_game(object version, [Def("无")] object loader = null, [Def("")] object loader_version = null, [Def("")] object instance = null, object extra = null)
	{
		object inst = Py.Or(instance, Config.CONFIG.Get("default_instance", "default"));
		object ex = (Py.Truthy(extra) ? extra : new PyDict());
		PyDict pyDict = Py.DictOr(ex);
		List<string> list = new List<string> { Py.Str(version) };
		if (Py.Truthy(loader) && !Py.Eq(loader, "") && !Py.Eq(loader, "无"))
		{
			list.Add(Py.Str(loader));
		}
		if (Py.Truthy(pyDict.Get("optifine")))
		{
			list.Add("OptiFine");
		}
		if (Py.Truthy(pyDict.Get("liteloader")))
		{
			list.Add("LiteLoader");
		}
		string title = "安装游戏 " + string.Join(" + ", list);
		return StartTask(title, (TaskContext ctx) => _install_game_impl(ctx, version, loader, loader_version, inst, ex));
	}

	private object _install_game_impl(TaskContext ctx, object version, object loader, object loader_version, object instance, object extra0)
	{
		PyDict pyDict = Py.DictCtor(Py.Or(extra0, new PyDict()));
		pyDict.SetDefault("skip_assets", Py.Truthy(Config.CONFIG.Get("skip_assets")));
		Instance instance2 = _instance(instance);
		DownloadManager downloadManager = _dm(ctx);
		Installer installer = new Installer(instance2, downloadManager, downloadManager.on_progress, downloadManager.cancel);
		ctx.Log("安装到实例 " + instance2.name);
		string text = GameInstall.InstallGame(installer, version, loader, loader_version, pyDict);
		ctx.Log("版本安装完成: " + text);
		object obj = Py.Or(Config.CONFIG.Get("default_isolation"), "none");
		if (Py.Truthy(obj) && !Py.Eq(obj, "none"))
		{
			VersionSettings.Save(instance2, text, PyDict.Of("isolation", obj));
		}
		return "版本安装完成: " + text;
	}

	public object repair_version(object instance, object version)
	{
		return StartTask("修复 " + Py.Str(version), delegate(TaskContext ctx)
		{
			Instance instance2 = _instance(instance);
			DownloadManager downloadManager = _dm(ctx);
			return Repair.RepairVersion(new Installer(instance2, downloadManager, downloadManager.on_progress, downloadManager.cancel), version);
		});
	}

	public object get_java_list([Def(false)] object scan_system = null)
	{
		List<object> obj = (Py.Truthy(scan_system) ? Java.AllJavas() : Java.ListInstalledJavas());
		List<object> list = new List<object>();
		foreach (PyDict item in obj)
		{
			list.Add(PyDict.Of("name", Py.Or(item.Get("name"), "Java " + Py.Str(item.Get("major"))), "major", Py.Str(Py.Or(item.Get("major"), "?")), "path", Py.Or(item.Get("exe"), item.Get("path"), "")));
		}
		return list;
	}

	public object java_combo_options(object instance, [Def(false)] object scan_system = null)
	{
		List<object> list = new List<object>();
		list.Add(PyDict.Of("label", "自动选择", "value", "自动选择"));
		List<object> list2 = list;
		HashSet<string> hashSet = new HashSet<string>();
		foreach (PyDict item in (List<object>)get_java_list(scan_system))
		{
			object obj = Py.Or(item.Get("path"), "");
			if (Py.Truthy(obj) && !hashSet.Contains(Py.Str(obj)))
			{
				hashSet.Add(Py.Str(obj));
				list2.Add(PyDict.Of("label", Py.Or(item.Get("name"), obj), "value", obj));
			}
		}
		string text = (string)get_instance_java(instance);
		if (text != "自动选择" && !hashSet.Contains(text))
		{
			list2.Add(PyDict.Of("label", Py.Fmt(I18nCore.Tr("已保存 ({0})"), text), "value", text));
		}
		return list2;
	}

	public object java_combo_label_for(object instance, object options = null)
	{
		string b = (string)get_instance_java(instance);
		foreach (object item in ArgList(Py.Truthy(options) ? options : java_combo_options(instance, false)))
		{
			if (!(item is PyDict pyDict))
			{
				throw new TypeError((Py.TypeName(item) == "str") ? "string indices must be integers, not 'str'" : ("'" + Py.TypeName(item) + "' object is not subscriptable"));
			}
			if (Py.Eq(pyDict.Req("value"), b))
			{
				return pyDict.Req("label");
			}
		}
		return "自动选择";
	}

	public object download_java(object major, [Def("adoptium")] object vendor = null)
	{
		string text = Py.Strip(Py.AsStr(Py.Or(vendor, "adoptium"), "strip"));
		if (text.Length == 0)
		{
			text = "adoptium";
		}
		if (text != "adoptium")
		{
			try
			{
				return install_java(Py.Int(major), text);
			}
			catch (Exception)
			{
			}
		}
		return StartTask("下载 Java " + Py.Str(major), delegate(TaskContext ctx)
		{
			DownloadManager downloadManager = _dm(ctx);
			ctx.Log("下载 Adoptium Java " + Py.Str(major));
			string text2 = Java.InstallAdoptium(downloadManager, Py.Int(major), downloadManager.on_progress);
			ctx.Log("Java " + Py.Str(major) + " 就绪: " + text2);
			return (object)null;
		});
	}

	public object install_java(object major, [Def("adoptium")] object vendor = null)
	{
		return StartTask("下载 " + Py.Str(vendor) + " Java " + Py.Str(major), delegate(TaskContext ctx)
		{
			DownloadManager downloadManager = _dm(ctx);
			string text = Java.InstallJavaVendor(downloadManager, major, vendor, downloadManager.on_progress);
			ctx.Log("Java 已安装: " + text);
			return Py.Fmt(I18nCore.Tr("Java {0} ({1}) 安装完成"), major, vendor);
		});
	}

	private PyDict _require_launch_account(object account, object username = null)
	{
		if (!_is_offline_account(account))
		{
			PyDict account2 = accounts.GetAccount(account);
			if (account2 == null)
			{
				throw new LaunchError(Py.Fmt(I18nCore.Tr("账号不存在: {0}"), account));
			}
			return accounts.EnsureValid(account2);
		}
		if (accounts.accounts.Count == 0)
		{
			throw new LaunchError(I18nCore.Tr("启动游戏前请先创建一个离线用户（「账号」页 → 添加离线账号）"));
		}
		string text = Py.Strip(Py.Str(Py.Or(username, "")));
		if (text.Length > 0)
		{
			PyDict account3 = accounts.GetAccount(text);
			if (account3 == null)
			{
				throw new LaunchError(Py.Fmt(I18nCore.Tr("离线用户「{0}」还没创建，请先到「账号」页添加"), text));
			}
			return accounts.EnsureValid(account3);
		}
		return accounts.EnsureValid(accounts.GetActive() ?? ((PyDict)accounts.accounts[0]));
	}

	public object build_launch_command(object instance, object version, object account, object username, object memory_mb, object width, object height, [Def("自动选择")] object java = null)
	{
		Instance instance2 = _instance(instance);
		if (!Py.Truthy(version))
		{
			throw new LaunchError(I18nCore.Tr("请先选择版本"));
		}
		PyDict account2 = _require_launch_account(account, username);
		PyDict pyDict = accounts.LaunchProps(account2);
		string text = Py.Strip(Py.Str(Py.Or(VersionSettings.Load(instance2, version).Get("auth_server"), "")));
		if (text.Length > 0 && !Py.Truthy(pyDict.Get("authlib_api")))
		{
			pyDict = new PyDict(pyDict) { ["authlib_api"] = text };
		}
		return McLauncher.BuildLaunchCommand(java_exe: (Py.Eq(java, "自动选择") || Py.Eq(java, "")) ? "自动选择" : java, instance: instance2, version_id: Py.Str(version), account_props: pyDict, memory_mb: memory_mb, width: width, height: height, extra_game_args: null, extra_jvm_args: null, game_directory: null, authlib_api: pyDict.Get("authlib_api")).cmd.Cast<object>().ToList();
	}

	public object get_launch_command(object instance, object version, [Def("")] object account = null, [Def("")] object username = null, [Def(0L)] object memory_mb = null)
	{
		Instance inst = _instance(instance);
		if (!Py.Truthy(version))
		{
			throw new LaunchError(I18nCore.Tr("请先选择版本"));
		}
		string version_id = Py.Str(version);
		PyDict pyDict = (Py.Or(inst.VersionJson(version_id), new PyDict()) as PyDict) ?? new PyDict();
		PyDict version_json;
		try
		{
			version_json = Manifest.ResolveInherits(pyDict, (string pid) => inst.VersionJson(pid) as PyDict);
		}
		catch (Exception)
		{
			version_json = pyDict;
		}
		string text = Java.ResolveLaunchJava(version_json);
		if (string.IsNullOrEmpty(text))
		{
			text = Java.ResolveLaunchJava(version_json, null, new DownloadManager(2L));
		}
		if (string.IsNullOrEmpty(text))
		{
			throw new LaunchError(I18nCore.Tr("无法确定 Java 路径"));
		}
		PyDict account2 = _require_launch_account(account, username);
		PyDict account_props = accounts.LaunchProps(account2);
		PyDict pyDict2 = LaunchFlow.Prepare(inst, version_id, null, Py.Or(memory_mb, Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L))));
		McLauncher.LaunchCommand launchCommand = McLauncher.BuildLaunchCommand(inst, version_id, account_props, text, pyDict2["memory_mb"], null, null, (List<object>)pyDict2["extra_game_args"], pyDict2["jvm_args"], (string)pyDict2["game_dir"]);
		return string.Join(" ", launchCommand.cmd);
	}

	public object export_launch_script(object instance, object version, [Def("")] object dest = null)
	{
		return StartTask("导出启动脚本 " + Py.Str(version), delegate(TaskContext ctx)
		{
			Instance instance2 = _instance(instance);
			PyDict account = _require_launch_account("", "");
			PyDict pyDict = accounts.LaunchProps(account);
			string text = Py.Str(version);
			PyDict pyDict2 = LaunchFlow.Prepare(instance2, text, null, Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L)));
			string java_exe = Java.ResolveLaunchJava((Py.Or(instance2.VersionJson(text), new PyDict()) as PyDict) ?? new PyDict(), null, null, null, delegate(string t)
			{
				ctx.Log(t);
			});
			McLauncher.LaunchCommand launchCommand = McLauncher.BuildLaunchCommand(instance2, text, pyDict, java_exe, Py.Or(pyDict2["memory_mb"], 4096L), null, null, (List<object>)pyDict2["extra_game_args"], pyDict2["jvm_args"], (string)pyDict2["game_dir"], pyDict.Get("authlib_api"));
			string text2 = VersionOps.ExportLaunchBat(PPath.Norm(Py.Truthy(dest) ? Py.Str(dest) : PPath.Join(Utils.ROOT, "exports", "launch-" + instance2.name + "-" + text + ".bat")), launchCommand.cmd.Cast<object>(), launchCommand.game_dir);
			ctx.Log("已写出 " + text2);
			return text2;
		});
	}

	public object preflight_launch([Def("")] object instance = null, [Def("")] object version = null, [Def(0L)] object memory_mb = null, [Def("")] object java = null)
	{
		return Preflight.CheckLaunch(_instance(Py.Or(instance, "")), Py.Or(version, ""), Py.Int(Py.Or(memory_mb, 0L)), Py.S(java));
	}

	public object launch_game(object instance, object version, object account, object username, object memory_mb, object width, object height, [Def("自动选择")] object java = null, object extra_game_args = null, [Def(false)] object force = null)
	{
		return _launch_task_id = StartTask("启动游戏 " + Py.Str(version), (TaskContext ctx) => _launch_game_impl(ctx, instance, version, account, username, memory_mb, width, height, java, extra_game_args, force));
	}

	private object _launch_game_impl(TaskContext ctx, object instance, object version, object account, object username, object memory_mb, object width, object height, object java, object extra_game_args, object force)
	{
		Action<string> action = delegate(string t)
		{
			ctx.Log(t);
		};
		if (!Py.Truthy(version))
		{
			throw new LaunchError(I18nCore.Tr("请先选择版本（到「版本」页安装）"));
		}
		if (!Py.Truthy(Config.CONFIG.Get("allow_multi_instance", false)) && Py.Truthy(is_game_running()))
		{
			throw new LaunchError(I18nCore.Tr("游戏正在运行中\n若要同时运行多个游戏，请到设置开启「允许多开」"));
		}
		string text = Py.Str(version);
		string text2 = Py.S(java);
		PyDict pyDict = Preflight.CheckLaunch(_instance(instance), text, Py.Int(Py.Or(memory_mb, 0L)), text2);
		List<PyDict> list = Py.ListOr(Py.Or(pyDict.Get("items"), new List<object>())).Cast<PyDict>().ToList();
		foreach (PyDict item in list)
		{
			object obj = item.Get("level");
			if (Py.Eq(obj, "error") || Py.Eq(obj, "warn"))
			{
				action("[预检:" + Py.Str(obj) + "] " + Py.Str(item.Get("title")) + ": " + Py.Str(item.Get("detail")));
			}
		}
		if (!Py.Truthy(pyDict.Get("ok", true)))
		{
			List<PyDict> list2 = list.Where((PyDict it) => Py.Eq(it.Get("level"), "error")).ToList();
			if (!Py.Truthy(force))
			{
				string text3 = string.Join("\n\n", list2.Select((PyDict e) => "· " + Py.Str(e.Get("title")) + "\n" + Py.Str(e.Get("detail"))));
				if (text3.Length == 0)
				{
					text3 = I18nCore.Tr("启动预检未通过");
				}
				throw new LaunchError(I18nCore.Tr("启动预检未通过") + "\n\n" + text3);
			}
			action("[预检:强制启动] 忽略 " + list2.Count + " 条 error 强制启动：" + string.Join("; ", list2.Select((PyDict e) => Py.Str(e.Get("code")) + "·" + Py.Str(e.Get("title")))));
		}
		Instance inst = _instance(instance);
		action("实例: " + inst.name + " | 版本: " + text);
		action("实例 Java 设置: " + inst.JavaPref());
		Config.CONFIG.Set("default_instance", inst.name);
		Config.CONFIG.Save();
		object obj2 = Py.Or(VersionSettings.Load(inst, text).Get("login_account"), "");
		if (Py.Truthy(obj2))
		{
			account = obj2;
			action("该版本绑定账号: " + Py.Str(obj2));
		}
		PyDict account2 = _require_launch_account(account, username);
		PyDict pyDict2 = accounts.LaunchProps(account2);
		string text4 = (Py.Eq(pyDict2.Get("user_type"), "msa") ? I18nCore.Tr("正版") : (Py.Truthy(pyDict2.Get("authlib_api")) ? I18nCore.Tr("皮肤站") : (Py.Truthy(pyDict2.Get("nide8_id")) ? I18nCore.Tr("统一通行证") : I18nCore.Tr("离线"))));
		action("账号: " + Py.Str(pyDict2.Get("name")) + " (" + text4 + ")");
		action("内存: " + Py.Str(memory_mb) + " MB | 分辨率: " + Py.Str(width) + "x" + Py.Str(height));
		PyDict pyDict3 = LaunchFlow.Prepare(inst, text, ArgList(extra_game_args), memory_mb);
		memory_mb = Py.Or(pyDict3["memory_mb"], memory_mb);
		List<object> extra_game_args2 = (List<object>)pyDict3["extra_game_args"];
		string text5 = (string)pyDict3["game_dir"];
		PyDict pyDict4 = (PyDict)pyDict3["settings"];
		LaunchFlow.RunHook(Py.Str(Py.Or(pyDict4.Get("pre_launch"), "")), text5, action, Py.Truthy(pyDict3.Get("pre_launch_wait", true)));
		ctx.Progress(1L, 4L, I18nCore.Tr("检查 Java"));
		PyDict pyDict5 = (Py.Or(inst.VersionJson(text), new PyDict()) as PyDict) ?? new PyDict();
		PyDict version_json;
		try
		{
			version_json = Manifest.ResolveInherits(pyDict5, (string pid) => inst.VersionJson(pid) as PyDict);
		}
		catch (Exception)
		{
			version_json = pyDict5;
		}
		string preference = LaunchJava.Preference(inst, text, java);
		var javaCheck = LaunchJava.Inspect(version_json, preference);
		if (Py.S(javaCheck.Get("level")) == "error") throw new LaunchError(Py.S(javaCheck.Get("title")) + "\n" + Py.S(javaCheck.Get("detail")));
		object obj3 = javaCheck.Get("path");
		long num = Java.RequiredJavaMajor(version_json);
		string text6 = Java.ResolveLaunchJava(version_json, obj3, null, null, action);
		if (!Java.JavaUsableFor(version_json, text6))
		{
			action("未找到 Java " + num + "，自动下载中…");
			DownloadManager downloadManager = _dm(ctx);
			text6 = Java.ResolveLaunchJava(version_json, null, downloadManager, downloadManager.on_progress, action);
		}
		string text7 = (from l in Py.SplitLines(Java.JavaVersionOutput(text6) ?? "")
			select Py.Strip(l)).FirstOrDefault((string l) => l.Length > 0) ?? "?";
		action("Java -version: " + text7);
		long? javaMajor = Java.GetJavaMajor(text6);
		action("使用 Java " + ((javaMajor.HasValue && javaMajor.Value != 0L) ? javaMajor.Value.ToString() : "?") + ": " + Py.Str(text6));
		ctx.Progress(2L, 4L, I18nCore.Tr("构建启动参数"));
		if (Py.Truthy(pyDict2.Get("authlib_api")))
		{
			Authlib.EnsureInjector(_dm(ctx), action);
		}
		if (Py.Truthy(pyDict2.Get("nide8_id")) || Py.Truthy(pyDict3.Get("nide8_id")))
		{
			Nide8.EnsureJar(_dm(ctx), action);
			if (Py.Truthy(pyDict3.Get("nide8_id")) && !Py.Truthy(pyDict2.Get("nide8_id")))
			{
				pyDict2 = new PyDict(pyDict2);
				pyDict2["nide8_id"] = pyDict3["nide8_id"];
			}
		}
		object[] array = LaunchFlow.ResolveResolution(pyDict3, width, height);
		McLauncher.LaunchCommand launchCommand = McLauncher.BuildLaunchCommand(inst, text, pyDict2, text6, memory_mb, array[0], array[1], extra_game_args2, pyDict3["jvm_args"], text5, pyDict2.Get("authlib_api"));
		text5 = launchCommand.game_dir;
		action("实际启动: " + launchCommand.cmd[0]);
		action(I18nCore.Tr("正在启动游戏进程…"));
		ctx.Progress(3L, 4L, I18nCore.Tr("游戏启动中"));
		GameProcess gameProcess = new GameProcess(launchCommand.cmd, text5, action, null, Py.Str(pyDict3["priority"]), Py.Str(Py.Or(pyDict3.Get("window_title"), "")), start_readers: false);
		lock (_game_lock)
		{
			_game_proc = gameProcess.proc;
		}
		Emit("game_started", new PyDict());
		gameProcess.StartReaders();
		object obj5 = null;
		PlaytimeTracker playtimeTracker = null;
		try
		{
			playtimeTracker = new PlaytimeTracker(inst.name, text);
		}
		catch (Exception)
		{
			playtimeTracker = null;
		}
		playtimeTracker?.Start();
		try
		{
			obj5 = gameProcess.Wait();
		}
		finally
		{
			if (playtimeTracker != null)
			{
				try
				{
					long num2 = playtimeTracker.Stop();
					if (Py.Truthy(num2))
					{
						action("本次游玩 " + Py.Str(Playtime.FormatDuration(num2)));
					}
				}
				catch (Exception)
				{
				}
			}
			lock (_game_lock)
			{
				if (_game_proc == gameProcess.proc)
				{
					_game_proc = null;
				}
			}
			Emit("game_exited", PyDict.Of("code", obj5));
		}
		if (ctx.Cancelled)
		{
			action(I18nCore.Tr("已停止游戏"));
			return null;
		}
		action("游戏已退出，退出码 " + Py.Str(obj5));
		LaunchFlow.RunHook(Py.Str(Py.Or(pyDict4.Get("post_launch"), "")), text5, action);
		PyDict pyDict7 = Crash.AnalyzeLaunch(inst, obj5, gameProcess.LastLines(), gameProcess.started_at, cancelled: false, text, new object[1] { text5 });
		if (Py.Truthy(pyDict7.Get("is_crash")))
		{
			action("[崩溃分析] " + Py.Str(Py.Or(pyDict7.Get("summary"), pyDict7.Get("headline"))));
			throw new GameCrashError(pyDict7);
		}
		return I18nCore.Tr("游戏已退出");
	}

	public object export_crash_report([Def("")] object task_id = null, [Def("")] object dest = null)
	{
		PyDict obj = get_crash(task_id) as PyDict;
		if (!Py.Truthy(obj))
		{
			throw new LaunchError(I18nCore.Tr("没有可导出的错误报告"));
		}
		return Crash.ExportReport(obj, Py.Truthy(dest) ? Py.Str(dest) : null);
	}

	public object open_crash_file([Def("")] object path = null, [Def("")] object task_id = null)
	{
		object obj = Py.Or(path, Py.Or(Py.DictOr(get_crash(task_id)).Get("direct_file"), ""));
		if (!Py.Truthy(obj))
		{
			throw new LaunchError(I18nCore.Tr("没有可打开的日志文件"));
		}
		if (!Crash.OpenPath(obj))
		{
			throw new LaunchError(Py.Fmt(I18nCore.Tr("无法打开: {0}"), obj));
		}
		return obj;
	}

	public object apply_crash_action(object action = null, object report = null)
	{
		PyDict pyDict = Py.DictOr(Py.Or(action, new PyDict()));
		PyDict pyDict2 = Py.DictOr(Py.Or(report, new PyDict()));
		string text = Py.Strip(Py.AsStr(Py.Or(pyDict.Get("id"), ""), "strip"));
		object obj = Py.Or(Py.Or(pyDict.Get("instance"), pyDict2.Get("instance")), Py.Or(Config.CONFIG.Get("default_instance"), "default"));
		object obj2 = Py.Or(pyDict.Get("version"), pyDict2.Get("version"), "");
		switch (text)
		{
		case "disable_mods":
		{
			List<object> list = Py.ListCtor(Py.Or(pyDict.Get("mods"), new List<object>()));
			List<object> list2 = new List<object>();
			List<string> list3 = new List<string>();
			foreach (object item in list)
			{
				try
				{
					disable_mod(obj, item, obj2);
					list2.Add(item);
				}
				catch (Exception e)
				{
					list3.Add(Py.Str(item) + ": " + Py.ExcStr(e));
				}
			}
			if (list2.Count == 0 && list3.Count > 0)
			{
				return PyDict.Of("ok", false, "message", I18nCore.Tr("未能禁用：") + string.Join("; ", list3));
			}
			string text3 = Py.Fmt(I18nCore.Tr("已禁用 {0} 个 Mod"), (long)list2.Count);
			if (list3.Count > 0)
			{
				text3 = text3 + I18nCore.Tr("；部分失败：") + string.Join("; ", list3);
			}
			return PyDict.Of("ok", true, "message", text3);
		}
		case "repair_version":
		{
			if (!Py.Truthy(obj2))
			{
				return PyDict.Of("ok", false, "message", I18nCore.Tr("报告里没有版本号，无法修复"));
			}
			object obj3 = repair_version(obj, obj2);
			return PyDict.Of("ok", true, "message", Py.Fmt(I18nCore.Tr("已开始修复 {0}"), obj2), "task_id", obj3);
		}
		case "need_java":
		{
			long num = Py.Int(Py.Or(pyDict.Get("major"), 17L));
			object obj4 = download_java(num.ToString(), "adoptium");
			return PyDict.Of("ok", true, "message", Py.Fmt(I18nCore.Tr("已开始下载 Java {0}"), num), "task_id", obj4);
		}
		case "bump_memory":
		{
			long val = Py.Int(Py.Or(pyDict.Get("memory_mb"), 6144L));
			val = Math.Max(1024L, Math.Min(32768L, val));
			Config.CONFIG.Set("memory_mb", val);
			Config.CONFIG.Save();
			Emit("ui_changed", new PyDict());
			return PyDict.Of("ok", true, "message", Py.Fmt(I18nCore.Tr("默认内存已设为 {0} MB"), val));
		}
		case "open_mods_folder":
		{
			Instance inst = _instance(obj);
			string path = _mods_folder(inst, obj2);
			Directory.CreateDirectory(Utils.AbsPath(path));
			Crash.OpenPath(path);
			return PyDict.Of("ok", true, "message", I18nCore.Tr("已打开 Mods 文件夹"));
		}
		case "open_crash_file":
		{
			string text2 = Py.Strip(Py.AsStr(Py.Or(pyDict.Get("path"), pyDict2.Get("direct_file"), ""), "strip"));
			if (text2.Length == 0 || !new PPath(text2).IsFile())
			{
				return PyDict.Of("ok", false, "message", I18nCore.Tr("没有可打开的崩溃文件"));
			}
			Crash.OpenPath(text2);
			return PyDict.Of("ok", true, "message", I18nCore.Tr("已打开崩溃报告"));
		}
		case "open_gpu_hint":
			return PyDict.Of("ok", true, "message", I18nCore.Tr("显卡/OpenGL 相关崩溃：请更新显卡驱动，关闭独显强制、超采样/滤镜，并确认不是远程桌面/虚拟机缺 OpenGL。"));
		case "reset_jvm_args":
			Config.CONFIG.Set("default_jvm_args", "");
			Config.CONFIG.Save();
			try
			{
				Instance instance = _instance(obj);
				if (Py.Truthy(obj2))
				{
					PyDict pyDict3 = VersionSettings.Load(instance, obj2);
					pyDict3["jvm_args"] = "";
					VersionSettings.Save(instance, obj2, pyDict3);
				}
			}
			catch (Exception)
			{
			}
			Emit("ui_changed", new PyDict());
			return PyDict.Of("ok", true, "message", I18nCore.Tr("已清空自定义 JVM 参数"));
		default:
			return PyDict.Of("ok", false, "message", Py.Fmt(I18nCore.Tr("未知动作: {0}"), text));
		}
	}

	public object get_smart_recommendation()
	{
		return Sysinfo.GetSmartRecommendation();
	}

	public PyDict get_layout()
	{
		(LayoutDoc doc, string profile, PyDict profiles) tuple = UiLayout.LayoutSnapshot();
		LayoutDoc item = tuple.doc;
		string item2 = tuple.profile;
		PyDict item3 = tuple.profiles;
		PyDict pyDict = new PyDict();
		foreach (KeyValuePair<string, (long, long)> item4 in UiLayout.CARD_MIN_SIZE)
		{
			pyDict[item4.Key] = Py.L(item4.Value.Item1, item4.Value.Item2);
		}
		return PyDict.Of("doc", item.ToDict(), "profile", item2, "profiles", Py.Sorted(item3.Keys.Cast<object>()), "default", UiLayout.DefaultDoc().ToDict(), "min_sizes", pyDict);
	}

	public PyDict save_layout(object doc, object profile = null)
	{
		var (flag, text) = UiLayout.SaveCurrent(UiLayout.ParseDoc(doc) ?? throw new ValueError(I18nCore.Tr("不是有效的布局文档")), profile as string);
		return PyDict.Of("profile", text, "saved", flag);
	}

	public PyDict save_layout_profile(object name, object doc = null)
	{
		string text = Py.Strip(Py.AsStr(Py.Or(name, ""), "strip"));
		if (text.Length == 0)
		{
			throw new ValueError(I18nCore.Tr("方案名称不能为空"));
		}
		LayoutDoc layoutDoc;
		if (doc == null)
		{
			layoutDoc = UiLayout.LoadActiveDoc();
		}
		else
		{
			layoutDoc = UiLayout.ParseDoc(doc);
			if (layoutDoc == null)
			{
				throw new ValueError(I18nCore.Tr("不是有效的布局文档"));
			}
		}
		UiLayout.SaveProfile(text, layoutDoc);
		return get_layout();
	}

	public PyDict activate_layout_profile([Def("")] object name = null)
	{
		UiLayout.ActivateProfile(Py.AsStr(Py.Or(name, ""), "strip"));
		return get_layout();
	}

	public PyDict delete_layout_profile(object name)
	{
		if (!UiLayout.DeleteProfile(Py.AsStr(Py.Or(name, ""), "strip")))
		{
			throw new ValueError(Py.Fmt(I18nCore.Tr("布局方案「{0}」不存在"), name));
		}
		return get_layout();
	}

	public PyDict reset_layout()
	{
		UiLayout.ResetToDefault();
		return get_layout();
	}

	public PyDict import_layout(object doc)
	{
		UiLayout.SaveActiveDoc(UiLayout.ParseDoc(doc) ?? throw new ValueError(I18nCore.Tr("不是有效的布局文件")));
		return get_layout();
	}

	internal Instance _instance(object name = null)
	{
		name = Py.Or(name, Config.CONFIG.Get("default_instance", "default"));
		Instance instance = new Instance(name);
		if (!LocalFs.IsDir(instance.path))
		{
			instance.Create();
		}
		else
		{
			instance.EnsureStandardDirs();
		}
		return instance;
	}

	public object get_instances()
	{
		double num = Py.Monotonic();
		List<object> inst_cache = _inst_cache;
		if (inst_cache != null && num - _inst_cache_at < 2.5)
		{
			return ((IEnumerable<object>)inst_cache).Select((Func<object, object>)((object r) => Py.DictCtor(r))).ToList();
		}
		EnsureDefaultInstance();
		List<object> list = new List<object>();
		foreach (string item in Instances.ListInstances())
		{
			Instance instance = new Instance(item);
			List<object> list2 = instance.InstalledIds();
			PyDict pyDict = instance.Meta();
			PyDict pyDict2 = (pyDict["modpack"] as PyDict) ?? new PyDict();
			object a = ((pyDict2.Count > 0) ? pyDict2["name"] : null);
			object o = Py.Or(a, pyDict["mc_version"], (list2.Count > 0) ? list2[0] : I18nCore.Tr("未安装版本"));
			list.Add(PyDict.Of("name", item, "group", Py.S(pyDict.Get("group")), "versions", (long)list2.Count, "mc", Py.Str(o), "pack", Py.Or(a, ""), "pack_version", Py.Or((pyDict2.Count > 0) ? pyDict2["version"] : "", ""), "mc_version", Py.Or((pyDict2.Count > 0) ? pyDict2["mc_version"] : null, pyDict["mc_version"], ""), "java", instance.JavaPref(), "java_label", instance_java_label(item)));
		}
		_inst_cache = list;
		_inst_cache_at = num;
		return ((IEnumerable<object>)list).Select((Func<object, object>)((object r) => Py.DictCtor(r))).ToList();
	}

	public object get_instance_java(object name)
	{
		return _instance(name).JavaPref();
	}

	public object instance_java_label(object name)
	{
		string text = (string)get_instance_java(name);
		if (text == "自动选择")
		{
			return "自动选择";
		}
		foreach (object item in AllJavasHook?.Invoke() ?? new List<object>())
		{
			if (item is PyDict pyDict && Py.Eq(pyDict["exe"], text))
			{
				return "Java " + Py.Str(Py.Or(pyDict["major"], "?"));
			}
		}
		return new PPath(text).Name;
	}

	public object get_playtime([Def("")] object instance = null)
	{
		return Playtime.GetPlaytime(Py.Or(instance, Config.CONFIG.Get("default_instance", "default")));
	}

	public object get_all_playtime()
	{
		return Playtime.GetAllPlaytime();
	}

	public object get_total_playtime()
	{
		return Playtime.GetTotalPlaytime();
	}

	public object format_playtime(object seconds)
	{
		return Playtime.FormatDuration(seconds);
	}

	public object clear_playtime([Def("")] object instance = null, [Def("")] object version = null)
	{
		Playtime.ClearPlaytime(instance, version);
		return null;
	}

	public object list_themes()
	{
		return Themes.ListThemes();
	}

	public object save_theme(object name)
	{
		return Themes.SaveTheme(name);
	}

	public object load_theme(object name)
	{
		return Themes.LoadTheme(name);
	}

	public object delete_theme(object name)
	{
		Themes.DeleteTheme(name);
		return null;
	}

	public object import_theme(object path)
	{
		return Themes.ImportTheme(path);
	}

	public object export_theme(object name, object dest)
	{
		return Themes.ExportTheme(name, dest);
	}

	public object list_global_mods()
	{
		return GlobalMods.ListEntries();
	}

	public object set_global_mod_enabled(object filename, object enabled)
	{
		return GlobalMods.SetEnabled(filename, enabled);
	}

	public object catalog_favorites()
	{
		var favorites = Py.ListCtor(Py.Or(Config.CONFIG.Get("catalog_favorites"), new List<object>())).OfType<PyDict>().Select(row => row.Copy()).ToList();
		foreach (var row in favorites) CatalogPresentation.Localize(row);
		return favorites.Cast<object>().ToList();
	}

	private static List<object> FavKey(object r)
	{
		PyDict pyDict = (r as PyDict) ?? throw new AttributeError("'" + Py.TypeName(r) + "' object has no attribute 'get'");
		return new List<object>
		{
			Py.S(pyDict["source"]),
			Py.Str(Py.Or(Py.Or(pyDict["slug"], pyDict["id"], pyDict["name"]), ""))
		};
	}

	public object toggle_favorite(object item)
	{
		List<object> list = Py.ListCtor(Py.Or(Config.CONFIG.Get("catalog_favorites"), new List<object>()));
		List<object> list2 = FavKey(item);
		List<object> list3 = new List<object>();
		bool flag = false;
		foreach (object item2 in list)
		{
			List<object> list4 = FavKey(item2);
			if (Py.Eq(list4[0], list2[0]) && Py.Eq(list4[1], list2[1]))
			{
				flag = true;
			}
			else
			{
				list3.Add(item2);
			}
		}
		if (!flag)
		{
			PyDict pyDict = (PyDict)item;
            var favorite = PyDict.Of("name", pyDict["name"], "source", pyDict["source"], "slug", pyDict["slug"], "id", pyDict["id"],
                "author", pyDict.Get("author"), "description", pyDict.Get("description"), "original_description", pyDict.Get("original_description"), "localization", pyDict.Get("localization"));
            foreach (string field in new[] { "kind", "chinese_name", "name_localization", "search_aliases" })
                if (pyDict.ContainsKey(field)) favorite[field] = pyDict[field];
            favorite.Update(CatalogPresentation.Artwork(pyDict));
            list3.Add(favorite);
		}
		Config.CONFIG.Set("catalog_favorites", list3);
		Config.CONFIG.Save();
		return list3;
	}

	public object help_articles([Def("")] object query = null)
	{
		return (from a in HelpContent.SearchArticles(query)
			select _localize_article(a)).ToList();
	}

	public object help_article(object article_id)
	{
		return _localize_article(HelpContent.GetArticle(article_id) ?? new PyDict());
	}

	private static object _localize_article(object article)
	{
		if (!(article is PyDict pyDict) || !Py.Truthy(pyDict["title"]))
		{
			return article;
		}
		PyDict pyDict2 = new PyDict(pyDict);
		pyDict2["title"] = I18nCore.Tr(Py.Str(pyDict2["title"]));
		return pyDict2;
	}

	public object feedback_history()
	{
		return Feedback.History();
	}

	public object cached_news()
	{
		return News.LoadCached();
	}

	private static bool _is_offline_account(object account)
	{
		string text = Py.S(account);
		if (text.Length != 0 && !(text == "离线模式"))
		{
			return text == I18nCore.Tr("离线模式");
		}
		return true;
	}

	public object skin_urls([Def("")] object account_name = null)
	{
		PyDict account = ((!_is_offline_account(account_name)) ? (accounts.GetAccount(account_name) ?? PyDict.Of("type", "offline", "name", account_name)) : PyDict.Of("type", "offline", "name", "Steve"));
		return PyDict.Of("avatar", Skin.AvatarUrl(account), "body", Skin.BodyUrl(account));
	}

	public object authlib_presets()
	{
		return ((IEnumerable<string[]>)Authlib.PRESETS).Select((Func<string[], object>)((string[] p) => PyDict.Of("name", p[0], "api", p[1]))).ToList();
	}

	public object java_vendor_list()
	{
		return Java.JavaVendorList();
	}

	public object java_vendor_label(object vendor)
	{
		return Java.JavaVendorLabel(vendor);
	}

	public object thumb_path(object url)
	{
		return Thumbnails.ThumbPath(url);
	}

	public object is_game_running()
	{
		Process game_proc;
		lock (_game_lock)
		{
			game_proc = _game_proc;
		}
		if (game_proc == null)
		{
			return false;
		}
		try
		{
			return !game_proc.HasExited;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public object export_content(object kind, object name, [Def("")] object dest_dir = null, [Def("")] object version = null, [Def("")] object instance = null)
	{
		return ContentExport.ExportOne(_instance(instance), kind, name, dest_dir, version);
	}

	public object export_contents(object kind, object names, [Def("")] object dest_dir = null, [Def("")] object version = null, [Def("")] object instance = null)
	{
		return ContentExport.ExportMany(_instance(instance), kind, names, dest_dir, version);
	}

	public object get_version_list()
	{
		object obj = Utils.ReadJson(PPath.Join(Utils.ROOT, "cache", "version_manifest.json"));
		PyDict pyDict = (Py.Truthy(obj) ? (obj as PyDict) : null);
		if (obj != null && Py.Truthy(obj) && pyDict == null)
		{
			throw new AttributeError("'" + Py.TypeName(obj) + "' object has no attribute 'get'");
		}
		PyDict pyDict2 = new PyDict();
		if ((pyDict?.Get("versions", new List<object>()) ?? new List<object>()) is List<object> list)
		{
			foreach (object item in list)
			{
				if (item is PyDict pyDict3 && Py.Truthy(pyDict3["id"]))
				{
					if (pyDict3["id"] is PyDict || pyDict3["id"] is List<object>)
					{
						throw new TypeError("unhashable type: '" + Py.TypeName(pyDict3["id"]) + "'");
					}
					pyDict2[Py.HashKey(pyDict3["id"])] = pyDict3;
				}
			}
		}
		return _version_rows(pyDict2);
	}

	internal static List<object> _version_rows(PyDict versions)
	{
		List<PyDict> list = new List<PyDict>();
		foreach (KeyValuePair<string, object> version in versions)
		{
			PyDict pyDict = (version.Value as PyDict) ?? new PyDict();
			object obj = Py.Or(pyDict["type"], "snapshot");
			string text = ((!Py.Eq(obj, "release")) ? ((!Py.Eq(obj, "old_alpha") && !Py.Eq(obj, "old_beta")) ? "snapshot" : ((string)obj)) : "release");
			list.Add(PyDict.Of("version", version.Key, "type", text, "date", Py.Slice(Py.Str(Py.Or(pyDict["releaseTime"], pyDict["time"], "")), 0, 10)));
		}
		return Py.Sorted(list, (PyDict r) => r["date"], reverse: true).Cast<object>().ToList();
	}

	private static byte[] B64DecodeStrict(string raw)
	{
		if (!_B64_OK.IsMatch(raw))
		{
			return null;
		}
		string text = raw.TrimEnd('=');
		int num = raw.Length - text.Length;
		int length = text.Length;
		if (raw.Length % 4 != 0)
		{
			return null;
		}
		if (num != (4 - length % 4) % 4)
		{
			return null;
		}
		try
		{
			return Convert.FromBase64String(raw);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	public object stash_upload(object name, object data)
	{
		string text = Py.Strip(Py.AsStr(Py.Or(data, ""), "strip"));
		byte[] array = B64DecodeStrict(text.StartsWith("data:") ? Py.Split(text, ",", 1).Last() : text);
		if (array == null)
		{
			throw new ValueError(I18nCore.Tr("上传的数据不是有效的 base64"));
		}
		if (array.Length == 0)
		{
			throw new ValueError(I18nCore.Tr("上传的文件是空的"));
		}
		string text2 = Py.AsStr(Py.Or(name, ""), "__fspath__");
		string input = (Utils.IS_WINDOWS ? text2.Substring(Math.Max(text2.LastIndexOf('/'), text2.LastIndexOf('\\')) + 1) : text2.Substring(text2.LastIndexOf('/') + 1));
		string text3 = Py.Strip(_UPLOAD_BAD.Replace(input, "_"), " .");
		string text4 = PPath.Join(Utils.ROOT, "uploads");
		Directory.CreateDirectory(PPath.Abs(text4));
		_prune_uploads(text4, 86400L);
		string text5 = PPath.Join(text4, (text3.Length > 0) ? text3 : "upload.bin");
		if (LocalFs.Exists(text5))
		{
			PPath pPath = new PPath(text5);
			text5 = PPath.Join(text4, pPath.Stem + "-" + (long)Py.Time() + pPath.Suffix);
		}
		File.WriteAllBytes(PPath.Abs(text5), array);
		return text5;
	}

	private static void _prune_uploads(string folder, long keep_seconds = 86400L)
	{
		double num = Py.Time() - (double)keep_seconds;
		foreach (string item in LocalFs.Children(folder))
		{
			try
			{
				if (LocalFs.IsFile(item) && LocalFs.MTime(item) < num)
				{
					File.Delete(PPath.Abs(item));
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
	}

	public object collect_sysinfo([Def(false)] object force = null, [Def(false)] object scan_system_java = null)
	{
		return Sysinfo.Collect(Py.Truthy(force), Py.Truthy(scan_system_java));
	}

	public object sysinfo_text(object info = null)
	{
		return Sysinfo.FormatText(info);
	}

	public object submit_feedback(object category, object title, object body, [Def("")] object contact = null, [Def(true)] object include_sysinfo = null)
	{
		return Feedback.Submit(category, title, body, contact, include_sysinfo);
	}

	public object submit_crash_feedback(object report, [Def("")] object extra = null)
	{
		return Feedback.SubmitCrash(report, extra);
	}

	public object submit_crash_report([Def("")] object task_id = null)
	{
		object obj = get_crash(task_id);
		if (!Py.Truthy(obj))
		{
			throw new LaunchError(I18nCore.Tr("没有可上传的崩溃报告"));
		}
		return Py.Or(Feedback.SubmitCrash(obj)["message"], I18nCore.Tr("已上传"));
	}

	public object check_update()
	{
		return Updater.Check();
	}

	public object fetch_news()
	{
		return News.Fetch();
	}

	public object start_self_update()
	{
		return StartTask("更新启动器", delegate(TaskContext ctx)
		{
			PyDict pyDict = Updater.Check(_dm(ctx));
			if (!Py.Truthy(pyDict["ok"]))
			{
				throw new RuntimeError(Py.Str(pyDict["message"]));
			}
			if (!Py.Truthy(pyDict["has_update"]))
			{
				return Py.Str(Py.Or(pyDict["message"], I18nCore.Tr("已是最新")));
			}
			ctx.Log(Py.Str(Py.Or(pyDict["message"], I18nCore.Tr("下载更新"))));
			string text = Updater.Download(pyDict, _dm(ctx));
			ctx.Log("更新包已下载: " + text);
			Emit("update_staged", PyDict.Of("package", text, "version", Py.Or(pyDict["latest"], "")));
			return I18nCore.Tr("更新包已下载，重启启动器即可完成更新");
		});
	}

	public object detect_official_launcher()
	{
		return OfficialMigrate.DetectOfficial();
	}

	public object official_launcher_dir()
	{
		return OfficialMigrate.OfficialDir();
	}

	public object scan_official_versions()
	{
		return OfficialMigrate.ScanVersions(OfficialMigrate.OfficialDir());
	}

	public object migrate_official_launcher([Def("default")] object instance = null)
	{
		return StartTask("导入官方启动器", delegate(TaskContext ctx)
		{
			string text = OfficialMigrate.OfficialDir();
			ctx.Log("正在从 " + text + " 迁移…");
			ctx.Progress(1L, 3L, I18nCore.Tr("扫描版本"));
			List<object> list = OfficialMigrate.ScanVersions(text);
			if (list.Count == 0)
			{
				ctx.Log(I18nCore.Tr("未发现版本"));
				return I18nCore.Tr("无版本可导入");
			}
			ctx.Log($"发现 {list.Count} 个版本");
			ctx.Progress(2L, 3L, Py.Fmt(I18nCore.Tr("导入 {0} 个版本（含依赖库）"), (long)list.Count));
			PyDict pyDict = OfficialMigrate.Migrate(text, instance);
			List<object> list2 = Py.ListOr(Py.Or(pyDict["versions"], new List<object>()));
			List<object> list3 = Py.ListOr(Py.Or(pyDict["accounts"], new List<object>()));
			ctx.Log($"已导入 {list2.Count} 个版本（含各版本用到的 libraries）");
			if (list3.Count > 0)
			{
				ctx.Log("已导入账号: " + string.Join("、", list3.Select(Py.Str)));
				ctx.Log(I18nCore.Tr("官方只存了访问令牌、没有刷新令牌，过期后需要重新登录"));
			}
			Emit("ui_changed", new PyDict());
			string text2 = Py.Fmt(I18nCore.Tr("已导入 {0} 个版本"), (long)list2.Count);
			if (list3.Count > 0)
			{
				text2 += Py.Fmt(I18nCore.Tr("、{0} 个账号"), (long)list3.Count);
			}
			return text2;
		});
	}

	public object fetch_version_list()
	{
		return _version_rows(Manifest.ListRemoteVersions(new DownloadManager(2L)) ?? new PyDict());
	}

	public object list_loader_versions(object mc_version, object loader)
	{
		return LoaderMeta.ListLoaderVersions(new DownloadManager(2L), mc_version, loader);
	}

	public object backup_save(object instance, object name, [Def("")] object version = null)
	{
		return StartTask("备份存档 " + Py.Str(name), delegate(TaskContext ctx)
		{
			PyDict pyDict = Saves.BackupSave(_instance(instance), name, version, delegate(string text, long cur, long total)
			{
				ctx.Progress(cur, total, text);
			});
			ctx.Log("备份完成: " + Py.Str(pyDict["path"]));
			Emit("ui_changed", new PyDict());
			return Py.Fmt(I18nCore.Tr("已备份到 {0}"), pyDict["name"]);
		});
	}

	public object list_save_backups(object instance, [Def("")] object name = null, [Def("")] object version = null)
	{
		return Saves.ListBackups(_instance(instance), name, version);
	}

	public object restore_save_backup(object instance, object backup_name, [Def("")] object version = null, [Def(false)] object overwrite = null)
	{
		PyDict result = Saves.RestoreBackup(_instance(instance), backup_name, version, "", Py.Truthy(overwrite));
		Emit("ui_changed", new PyDict());
		return result;
	}

	public object delete_save_backup(object instance, object backup_name, [Def("")] object version = null)
	{
		Saves.DeleteBackup(_instance(instance), backup_name, version);
		Emit("ui_changed", new PyDict());
		return null;
	}

	public object export_save(object instance, object name, object dest, [Def("")] object version = null)
	{
		return Saves.ExportSave(_instance(instance), name, dest, version);
	}

	public object list_saves(object instance, [Def("")] object version = null)
	{
		return Saves.ListSaves(_instance(instance), version);
	}

	public object delete_save(object instance, object name, [Def("")] object version = null)
	{
		Saves.DeleteSave(_instance(instance), name, version);
		return null;
	}

	public object open_save(object instance, object name, [Def("")] object version = null)
	{
		return Saves.OpenSave(_instance(instance), name, version);
	}

	public object install_datapack_into_save(object instance, object filename, object save_name, [Def("")] object version = null)
	{
		return Saves.InstallDatapackIntoSave(_instance(instance), filename, save_name, version);
	}

	public object list_media(object instance, object kind, [Def("")] object version = null)
	{
		return Saves.ListMedia(_instance(instance), kind, version);
	}

	public object open_media(object path)
	{
		return Crash.OpenPath(path);
	}

	public object list_servers([Def("")] object instance = null)
	{
		return Servers.ListServers(_instance(instance));
	}

	public object add_server(object instance, object name, object ip, [Def(25565L)] object port = null, [Def("")] object description = null)
	{
		return Servers.AddServer(_instance(instance), name, ip, port, description);
	}

	public object update_server(object instance, object index, [VarKw] PyDict kwargs = null)
	{
		return Servers.UpdateServer(_instance(instance), index, kwargs);
	}

	public object delete_server(object instance, object index)
	{
		Servers.DeleteServer(_instance(instance), index);
		return null;
	}

	public object import_servers(object instance, object text)
	{
		return Servers.ImportServersTxt(_instance(instance), text);
	}

	public object export_servers(object instance)
	{
		return Servers.ExportServersTxt(_instance(instance));
	}

	public object get_setting(object key, object @default = null)
	{
		PyDict settings = get_settings();
		if (key is PyDict || key is List<object>)
		{
			throw new TypeError("unhashable type: '" + Py.TypeName(key) + "'");
		}
		if (!(key is string key2))
		{
			return @default;
		}
		return settings.Get(key2, @default);
	}

	public PyDict get_settings()
	{
		PyDict pyDict = new PyDict(_settings_full());
		string[] sECRET_SETTING_KEYS = _SECRET_SETTING_KEYS;
		foreach (string text in sECRET_SETTING_KEYS)
		{
			string text2 = Py.Strip(Py.S(CONFIG.Get(text)));
			pyDict[text] = "";
			pyDict[text + "_set"] = text2.Length > 0;
			pyDict[text + "_hint"] = _secret_hint(text2);
		}
		pyDict["ai_gateway_url"] = Py.Strip(Py.S(CONFIG.Get("ai_gateway_url")));
		pyDict["ai_base_url"] = Py.Strip(Py.S(CONFIG.Get("ai_base_url")));
		return pyDict;
	}

	internal PyDict _with_stored_secrets(object settings)
	{
		PyDict pyDict = ((settings is PyDict other) ? new PyDict(other) : new PyDict());
		PyDict pyDict2 = _settings_full();
		string[] sECRET_SETTING_KEYS = _SECRET_SETTING_KEYS;
		foreach (string key in sECRET_SETTING_KEYS)
		{
			if (Py.Strip(Py.S(pyDict.Get(key))).Length == 0)
			{
				pyDict[key] = Py.Strip(Py.S(CONFIG.Get(key)));
			}
		}
		foreach (string item in new List<string>(pyDict.Keys))
		{
			if (item.EndsWith("_set") || item.EndsWith("_hint"))
			{
				pyDict.Pop(item);
			}
		}
		foreach (KeyValuePair<string, object> item2 in pyDict2)
		{
			pyDict.SetDefault(item2.Key, item2.Value);
		}
		return pyDict;
	}

	private static string StripAttr(object v)
	{
		return Py.Strip(Py.AsStr(v, "strip"));
	}

	internal PyDict _settings_full()
	{
		LauncherConfig c = CONFIG;
		PyDict pyDict = new PyDict();
		pyDict["share_libraries"] = Py.Truthy(GD("shared_libraries", false));
		pyDict["share_assets"] = Py.Truthy(GD("shared_assets", false));
		pyDict["download_threads"] = Py.Int(GD("download_threads", 8L));
		pyDict["download_segments"] = Py.Int(GD("download_segments", 4L));
		pyDict["default_memory_mb"] = Py.Int(GD("memory_mb", 4096L));
		pyDict["default_resolution"] = Py.L(Py.Int(GD("width", 854L)), Py.Int(GD("height", 480L)));
		pyDict["ms_client_id"] = Py.Or(G("microsoft_client_id"), "");
		pyDict["curseforge_api_key"] = Py.Or(G("curseforge_api_key"), "");
		pyDict["ai_mode"] = Py.Or(G("ai_mode"), "public");
		pyDict["ai_gateway_url"] = Py.Or(StripAttr(Py.Or(G("ai_gateway_url"), "")), Defaults.DEFAULT_GATEWAY_URL, "");
		pyDict["ai_base_url"] = Py.Or(StripAttr(Py.Or(G("ai_base_url"), "")), Defaults.DEFAULT_API_BASE, "");
		pyDict["ai_api_key"] = Py.Or(StripAttr(Py.Or(G("ai_api_key"), "")), Defaults.DEFAULT_API_KEY, "");
		pyDict["ai_model"] = Py.Or(G("ai_model"), "deepseek-v4-flash");
		foreach (string key in LocalSettingKeys) pyDict[key] = c.Get(key);
		pyDict["ai_confirm_writes"] = Py.Truthy(GD("ai_confirm_writes", true));
		pyDict["ai_permission_mode"] = Permission.NormalizePermissionMode(G("ai_permission_mode"), Py.Truthy(GD("ai_confirm_writes", true)));
		pyDict["ai_permission_rules"] = Py.ListCtor(Py.Or(G("ai_permission_rules"), new List<object>()));
		pyDict["ai_permission_dont_ask"] = Py.Truthy(GD("ai_permission_dont_ask", false));
		pyDict["ai_context_window"] = Math.Max(8192L, Py.Int(Py.Or(G("ai_context_window"), 131072L)));
		pyDict["ai_max_tokens"] = Py.Int(Py.Or(G("ai_max_tokens"), 8192L));
		pyDict["ai_fallback_model"] = Py.S(G("ai_fallback_model"));
		pyDict["root"] = Utils.ROOT;
		pyDict["feedback_url"] = Py.Or(G("feedback_url"), "http://114.66.28.184:53611", "");
		pyDict["feedback_heartbeat"] = Py.Truthy(GD("feedback_heartbeat", true));
		object obj = G("feedback_consent");
		bool flag = false;
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		pyDict["feedback_consent"] = (byte)(num & (flag ? 1 : 0)) != 0;
		pyDict["default_isolation"] = Py.Or(G("default_isolation"), "none");
		pyDict["default_jvm_args"] = Py.Or(G("default_jvm_args"), "");
		pyDict["update_url"] = Py.Or(G("update_url"), "");
		pyDict["download_source"] = Py.Or(G("download_source"), "auto");
		pyDict["community_source"] = Py.Or(G("community_source"), "auto");
		pyDict["use_system_proxy"] = Py.Truthy(GD("use_system_proxy", true));
		pyDict["launcher_visibility"] = Py.Or(G("launcher_visibility"), "keep");
		pyDict["gc_preset"] = Py.Or(G("gc_preset"), "auto");
		pyDict["download_limit_kbps"] = Py.Int(Py.Or(G("download_limit_kbps"), 0L));
		pyDict["auto_check_update"] = Py.Truthy(GD("auto_check_update", true));
		pyDict["custom_homepage"] = Py.Or(G("custom_homepage"), "");
		pyDict["homepage_mode"] = Py.Or(G("homepage_mode"), "news");
		pyDict["window_mode"] = Py.Or(G("window_mode"), "window");
		pyDict["game_dir"] = c.instances_dir;
		pyDict["offline_skin"] = Py.Or(G("offline_skin"), "default");
		pyDict["default_java"] = Py.Or(G("default_java"), "");
		pyDict["default_instance"] = Py.Or(G("default_instance"), "default");
		pyDict["ui_dark"] = Py.Truthy(GD("ui_dark", false));
		pyDict["theme_color"] = Py.Or(_norm_theme_color(G("theme_color")), "#2E9B6B");
		pyDict["ui_bg_preset"] = _norm_bg_preset(G("ui_bg_preset"));
		pyDict["ui_background"] = Py.Or(G("ui_background"), "");
		pyDict["ui_wallpaper_mode"] = Py.Or(G("ui_wallpaper_mode"), "local");
		pyDict["ui_font_family"] = Py.Or(G("ui_font_family"), "");
		pyDict["ui_wallpaper_engine_live"] = Py.Truthy(GD("ui_wallpaper_engine_live", false));
        pyDict["ui_wallpaper_video_optimize"] = Py.Truthy(GD("ui_wallpaper_video_optimize", true));
        pyDict["ui_wallpaper_cache_mib"] = _clamp_int(GD("ui_wallpaper_cache_mib", 512L), 256L, 4096L, 512L);
        pyDict["ui_wallpaper_cache_days"] = _clamp_int(GD("ui_wallpaper_cache_days", 0L), 0L, 365L, 0L);
		pyDict["ui_plugin_local_only"] = Py.Truthy(GD("ui_plugin_local_only", false));
		foreach (string key in new[] { "ui_wallpaper_engine_config", "ui_wallpaper_monitor", "ui_plugin_catalog" }) pyDict[key] = Py.Or(G(key), "");
		pyDict["ui_background_folder"] = Py.Or(G("ui_background_folder"), "");
		pyDict["ui_background_shuffle"] = Py.Truthy(GD("ui_background_shuffle", false));
		pyDict["ui_background_interval"] = _clamp_int(GD("ui_background_interval", 10L), 1L, 1440L, 10L);
		pyDict["ui_background_history"] = Config.HistoryPaths(G("ui_background_history"));
		pyDict["ui_background_blur"] = _clamp_int(GD("ui_background_blur", 0L), 0L, 40L, 0L);
		pyDict["ui_background_dim"] = _clamp_int(GD("ui_background_dim", 0L), 0L, 80L, 0L);
        pyDict["ui_glass_transparency"] = _clamp_int(GD("ui_glass_transparency", 100L), 0L, 100L, 100L);
        pyDict["ui_glass_frost"] = _clamp_int(GD("ui_glass_frost", 30L), 0L, 60L, 30L);
        foreach (string key in new[] { "ui_glass_cards", "ui_glass_controls", "ui_glass_popups" }) pyDict[key] = _clamp_int(GD(key, -1L), -1L, 100L, -1L);
		pyDict["ui_sidebar_opacity"] = _clamp_opacity(GD("ui_sidebar_opacity", 100L));
		pyDict["ui_window_aspect"] = _window_aspect(G("ui_window_aspect"));
		pyDict["ui_motion"] = Py.Truthy(GD("ui_motion", true));
		pyDict["ui_fly_animation"] = Py.Truthy(GD("ui_fly_animation", true));
		pyDict["ui_fly_duration_ms"] = Py.Int(Py.Or(GD("ui_fly_duration_ms", 620L), 620L));
		pyDict["first_run"] = Py.Truthy(GD("first_run", true));
		pyDict["skip_assets"] = Py.Truthy(GD("skip_assets", false));
		pyDict["allow_multi_instance"] = Py.Truthy(GD("allow_multi_instance", false));
		pyDict["show_hidden_versions"] = Py.Truthy(GD("show_hidden_versions", false));
		pyDict["export_dir"] = Py.Or(G("export_dir"), "");
		pyDict["default_priority"] = Py.Or(G("default_priority"), "normal");
		pyDict["global_mods_dir"] = Py.Or(G("global_mods_dir"), "");
		pyDict["instances_dir"] = Py.Str(Py.Or(G("instances_dir"), ".minecraft"));
		pyDict["ui_nav_order"] = Py.ListCtor(Py.Or(G("ui_nav_order"), new List<object>()));
		pyDict["ui_nav_pinned"] = Py.ListCtor(Py.Or(G("ui_nav_pinned"), new List<object>()));
		pyDict["ui_nav_hidden"] = Py.ListCtor(Py.Or(G("ui_nav_hidden"), new List<object>()));
		pyDict["ui_nav_style"] = Py.Or(G("ui_nav_style"), "");
		pyDict["ui_nav_defaults"] = Py.Or(G("ui_nav_defaults"), "");
		pyDict["ui_nav_groups"] = _nav_groups(G("ui_nav_groups"));
		pyDict["ui_section_members"] = _nav_members(G("ui_section_members"));
		pyDict["ui_sidebar_width"] = Py.Int(Py.Or(G("ui_sidebar_width"), 0L));
		return pyDict;
		object G(string k)
		{
			return c.Get(k);
		}
		object GD(string k, object d)
		{
			return c.Get(k, d);
		}
	}

	internal PyDict _current_background()
	{
		return PyDict.Of("image", Py.S(CONFIG.Get("ui_background")), "folder", Py.S(CONFIG.Get("ui_background_folder")));
	}

	internal void _after_background_change()
	{
		Emit("ui_changed", new PyDict());
	}

	public List<object> background_history()
	{
		return Config.BackgroundHistory().images;
	}

	public bool can_undo_background()
	{
		return Config.BackgroundHistory().images.Count > 0;
	}

	public PyDict undo_background()
	{
		var (list, list2) = Config.BackgroundHistory();
		if (list.Count == 0)
		{
			return _current_background();
		}
		PyDict pyDict = PyDict.Of("image", list[list.Count - 1], "folder", list2[list2.Count - 1]);
		list.RemoveAt(list.Count - 1);
		list2.RemoveAt(list2.Count - 1);
		CONFIG.Update(PyDict.Of("ui_background_history", list, "ui_background_folder_history", list2, "ui_background", pyDict["image"], "ui_background_folder", pyDict["folder"]));
		CONFIG.Save();
		_after_background_change();
		return pyDict;
	}

	public PyDict reset_background()
	{
		PyDict pyDict = PyDict.Of("image", Py.S(Config.DEFAULT_CONFIG.Get("ui_background")), "folder", Py.S(Config.DEFAULT_CONFIG.Get("ui_background_folder")));
		PyDict pyDict2 = _current_background();
		PyDict pyDict3 = PyDict.Of("ui_background", pyDict["image"], "ui_background_folder", pyDict["folder"]);
		if (!Py.Eq(pyDict2, pyDict))
		{
			(List<object> images, List<object> folders) tuple = Config.PushBackgroundHistory(pyDict2["image"], pyDict2["folder"]);
			List<object> item = tuple.images;
			List<object> item2 = tuple.folders;
			pyDict3["ui_background_history"] = item;
			pyDict3["ui_background_folder_history"] = item2;
		}
		CONFIG.Update(pyDict3);
		CONFIG.Save();
		_after_background_change();
		return pyDict;
	}

	public object update_settings(object settings = null, [VarKw] PyDict extra = null)
	{
		if (extra != null && extra.ContainsKey("data"))
		{
			throw new TypeError("BackendAPI.save_settings() got multiple values for argument 'data'");
		}
		save_settings(settings, extra);
		return null;
	}

	private static object Idx(object seq, int i)
	{
		if (seq is List<object> list)
		{
			if (i >= list.Count)
			{
				throw new IndexError("list index out of range");
			}
			return list[i];
		}
		if (seq is string text)
		{
			if (i >= text.Length)
			{
				throw new IndexError("string index out of range");
			}
			return text[i].ToString();
		}
		if (seq is PyDict)
		{
			throw new KeyError((long)i);
		}
		throw new TypeError("'" + Py.TypeName(seq) + "' object is not subscriptable");
	}

	private static string StripOrEmpty(object v)
	{
		return Py.Strip(Py.AsStr(Py.Or(v, ""), "strip"));
	}

	public object save_settings(object data = null, [VarKw] PyDict extra = null)
	{
		PyDict d = new PyDict();
		if (data is PyDict other)
		{
			d.Update(other);
		}
		if (extra != null)
		{
			d.Update(extra);
		}
		PyDict pyDict = new PyDict();
		if (Has("default_resolution"))
		{
			object seq = Py.Or(G("default_resolution"), Py.L(854, 480));
			pyDict["width"] = Py.Int(Idx(seq, 0));
			pyDict["height"] = Py.Int(Idx(seq, 1));
		}
		if (Has("share_libraries"))
		{
			pyDict["shared_libraries"] = Py.Truthy(G("share_libraries"));
		}
		if (Has("share_assets"))
		{
			pyDict["shared_assets"] = Py.Truthy(G("share_assets"));
		}
		if (Has("download_threads"))
		{
			pyDict["download_threads"] = Py.Int(Py.Or(G("download_threads"), 8L));
		}
		if (Has("download_segments"))
		{
			pyDict["download_segments"] = Math.Max(1L, Math.Min(16L, Py.Int(Py.Or(G("download_segments"), 1L))));
		}
		if (Has("default_memory_mb"))
		{
			pyDict["memory_mb"] = Py.Int(Py.Or(G("default_memory_mb"), 4096L));
		}
		if (Has("ms_client_id"))
		{
			string text = StripOrEmpty(G("ms_client_id"));
			pyDict["microsoft_client_id"] = ((text.Length > 0) ? text : CONFIG.Get("microsoft_client_id"));
		}
		string[] sECRET_SETTING_KEYS = _SECRET_SETTING_KEYS;
		bool flag = false;
		string[] array = sECRET_SETTING_KEYS;
		foreach (string text2 in array)
		{
			object obj = d.Get(text2 + "_clear");
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			if (((uint)num & (flag ? 1u : 0u)) != 0)
			{
				pyDict[text2] = "";
			}
			else if (Has(text2))
			{
				string text3 = Py.Strip(Py.S(G(text2)));
				if (text3.Length > 0)
				{
					pyDict[text2] = (_is_builtin_secret(text2, text3) ? "" : text3);
				}
			}
		}
		if (Has("ai_mode"))
		{
			pyDict["ai_mode"] = Py.Or(G("ai_mode"), "public");
		}
		if (Has("ai_gateway_url"))
		{
			pyDict["ai_gateway_url"] = StripOrEmpty(G("ai_gateway_url"));
		}
		if (Has("ai_base_url"))
		{
			pyDict["ai_base_url"] = StripOrEmpty(G("ai_base_url"));
		}
		if (Has("ai_model"))
		{
			pyDict["ai_model"] = Py.Or(G("ai_model"), CONFIG.Get("ai_model"), "deepseek-v4-flash");
		}
		foreach (string key in LocalSettingKeys)
		{
			if (Has(key)) pyDict[key] = G(key);
		}
		if (Has("ai_local_url")) pyDict["ai_local_url"] = Launcher.Ai.Local.ModelRules.Endpoint(Py.S(G("ai_local_url")));
		if (Has("ai_model_source")) pyDict["ai_model_source"] = Launcher.Ai.Local.ModelRepository.ValidateSource(Py.S(G("ai_model_source")));
		if (Has("ai_permission_mode"))
		{
			string text4 = Py.S(G("ai_permission_mode"));
			if (!_PERM_MODES.Contains(text4))
			{
				text4 = ((Has("ai_confirm_writes") ? Py.Truthy(d.Req("ai_confirm_writes")) : Py.Truthy(CONFIG.Get("ai_confirm_writes", true))) ? "default" : "yolo");
			}
			pyDict["ai_permission_mode"] = text4;
		}
		if (Has("ai_confirm_writes"))
		{
			pyDict["ai_confirm_writes"] = Py.Truthy(G("ai_confirm_writes"));
		}
		if (Has("ai_permission_rules"))
		{
			pyDict["ai_permission_rules"] = Py.ListCtor(Py.Or(G("ai_permission_rules"), new List<object>()));
		}
		if (Has("ai_permission_dont_ask"))
		{
			pyDict["ai_permission_dont_ask"] = Py.Truthy(G("ai_permission_dont_ask"));
		}
		if (Has("ai_context_window"))
		{
			pyDict["ai_context_window"] = Math.Max(8192L, Math.Min(Py.Int(Py.Or(G("ai_context_window"), 131072L)), 2000000L));
		}
		if (Has("ai_max_tokens"))
		{
			pyDict["ai_max_tokens"] = Py.Int(Py.Or(G("ai_max_tokens"), 8192L));
		}
		if (Has("ai_fallback_model"))
		{
			pyDict["ai_fallback_model"] = Py.Strip(Py.S(G("ai_fallback_model")));
		}
		if (Has("feedback_url"))
		{
			pyDict["feedback_url"] = StripOrEmpty(G("feedback_url"));
		}
		if (Has("feedback_heartbeat"))
		{
			pyDict["feedback_heartbeat"] = Py.Truthy(G("feedback_heartbeat"));
		}
		if (Has("feedback_consent"))
		{
			pyDict["feedback_consent"] = Py.Truthy(G("feedback_consent"));
		}
		if (Has("default_isolation"))
		{
			pyDict["default_isolation"] = Py.Or(G("default_isolation"), "none");
		}
		if (Has("default_jvm_args"))
		{
			pyDict["default_jvm_args"] = Py.Or(G("default_jvm_args"), "");
		}
		if (Has("update_url"))
		{
			pyDict["update_url"] = Py.Or(G("update_url"), "");
		}
		if (Has("download_source"))
		{
			pyDict["download_source"] = Py.Or(G("download_source"), "auto");
		}
		if (Has("community_source"))
		{
			pyDict["community_source"] = Py.Or(G("community_source"), "auto");
		}
		if (Has("use_system_proxy"))
		{
			pyDict["use_system_proxy"] = Py.Truthy(G("use_system_proxy"));
		}
		array = new string[8] { "launcher_visibility", "gc_preset", "custom_homepage", "homepage_mode", "window_mode", "offline_skin", "instances_dir", "default_java" };
		foreach (string text5 in array)
		{
			if (Has(text5))
			{
				pyDict[text5] = G(text5);
			}
		}
		if (Has("default_instance"))
		{
			string text6 = Py.Strip(Py.S(G("default_instance")));
			if (text6.Length > 0)
			{
				pyDict["default_instance"] = text6;
			}
		}
		if (Has("download_limit_kbps"))
		{
			pyDict["download_limit_kbps"] = Py.Int(Py.Or(G("download_limit_kbps"), 0L));
		}
		if (Has("auto_check_update"))
		{
			pyDict["auto_check_update"] = Py.Truthy(G("auto_check_update"));
		}
		if (Has("skip_assets"))
		{
			pyDict["skip_assets"] = Py.Truthy(G("skip_assets"));
		}
		if (Has("ui_dark"))
		{
			pyDict["ui_dark"] = Py.Truthy(G("ui_dark"));
		}
		if (Has("theme_color"))
		{
			string text7 = _norm_theme_color(G("theme_color"));
			if (text7.Length > 0)
			{
				pyDict["theme_color"] = text7;
			}
		}
		if (Has("ui_bg_preset"))
		{
			pyDict["ui_bg_preset"] = _norm_bg_preset(G("ui_bg_preset"));
		}
		array = new string[6] { "ui_motion", "ui_fly_animation", "allow_multi_instance", "show_hidden_versions", "first_run", "ui_background_shuffle" };
		foreach (string text8 in array)
		{
			if (Has(text8))
			{
				pyDict[text8] = Py.Truthy(G(text8));
			}
		}
		if (Has("ui_fly_duration_ms"))
		{
			pyDict["ui_fly_duration_ms"] = _clamp_int(G("ui_fly_duration_ms"), 1L, 10000L, 620L);
		}
		if (Has("ui_background_interval"))
		{
			pyDict["ui_background_interval"] = _clamp_int(G("ui_background_interval"), 1L, 1440L, 10L);
		}
		if (Has("ui_background_blur"))
		{
			pyDict["ui_background_blur"] = _clamp_int(G("ui_background_blur"), 0L, 40L, 0L);
		}
		if (Has("ui_background_dim"))
		{
			pyDict["ui_background_dim"] = _clamp_int(G("ui_background_dim"), 0L, 80L, 0L);
		}
		if (Has("ui_wallpaper_mode"))
		{
			string mode = Py.S(G("ui_wallpaper_mode"));
			if (mode != "local" && mode != "windows" && mode != "engine" && mode != "material" && mode != "transparent") throw new ValueError("壁纸模式无效。");
			pyDict["ui_wallpaper_mode"] = mode;
            AppearanceProfiles.RememberSource(pyDict, Py.S(Config.CONFIG.Get("ui_wallpaper_mode")));
		}
        if (Has("ui_glass_transparency")) pyDict["ui_glass_transparency"] = _clamp_int(G("ui_glass_transparency"), 0L, 100L, 100L);
        if (Has("ui_glass_frost")) pyDict["ui_glass_frost"] = _clamp_int(G("ui_glass_frost"), 0L, 60L, 30L);
        foreach (string key in new[] { "ui_glass_cards", "ui_glass_controls", "ui_glass_popups" }) if (Has(key)) pyDict[key] = _clamp_int(G(key), -1L, 100L, -1L);
		if (Has("ui_font_family"))
		{
			string family = Py.S(G("ui_font_family")).Trim();
			if (family.Length > 128 || family.Any(char.IsControl) || family.IndexOfAny(new[] { '/', '\\', '#', ',' }) >= 0) throw new ValueError("请选择有效的本机字体名称。");
			pyDict["ui_font_family"] = family;
		}
		if (Has("ui_wallpaper_engine_live")) pyDict["ui_wallpaper_engine_live"] = Py.Truthy(G("ui_wallpaper_engine_live"));
        if (Has("ui_wallpaper_video_optimize")) pyDict["ui_wallpaper_video_optimize"] = Py.Truthy(G("ui_wallpaper_video_optimize"));
        if (Has("ui_wallpaper_cache_mib")) pyDict["ui_wallpaper_cache_mib"] = _clamp_int(G("ui_wallpaper_cache_mib"), 256L, 4096L, 512L);
        if (Has("ui_wallpaper_cache_days")) pyDict["ui_wallpaper_cache_days"] = _clamp_int(G("ui_wallpaper_cache_days"), 0L, 365L, 0L);
		if (Has("ui_plugin_local_only")) pyDict["ui_plugin_local_only"] = Py.Truthy(G("ui_plugin_local_only"));
		foreach (string key in new[] { "ui_wallpaper_engine_config", "ui_wallpaper_monitor", "ui_plugin_catalog" })
		{
			if (!Has(key)) continue;
			string value = Py.S(G(key)).Trim();
			if (value.Length > 2048) throw new ValueError("设置内容过长。");
			if (key == "ui_plugin_catalog" && value.Length > 0 && !HelL.Core.Launcher.Plugins.PluginManifest.ValidUrl(value)) throw new ValueError("市场源需要使用 HTTPS。");
			pyDict[key] = value;
		}
		if (Has("ui_sidebar_opacity"))
		{
			pyDict["ui_sidebar_opacity"] = _clamp_opacity(G("ui_sidebar_opacity"));
		}
		if (Has("ui_window_aspect"))
		{
			pyDict["ui_window_aspect"] = _window_aspect(G("ui_window_aspect"));
		}
		array = new string[2] { "export_dir", "global_mods_dir" };
		foreach (string text9 in array)
		{
			if (Has(text9))
			{
				pyDict[text9] = Py.Strip(Py.S(G(text9)));
			}
		}
		if (Has("default_priority"))
		{
			pyDict["default_priority"] = Py.Str(Py.Or(G("default_priority"), "normal"));
		}
		if (Has("ui_background") || Has("ui_background_folder"))
		{
			string text10 = Py.S(CONFIG.Get("ui_background"));
			string text11 = Py.S(CONFIG.Get("ui_background_folder"));
			string text12 = Py.S(d.Get("ui_background", text10));
			string text13 = Py.S(d.Get("ui_background_folder", text11));
			if (text12 != text10 || text13 != text11)
			{
				(List<object> images, List<object> folders) tuple = Config.PushBackgroundHistory(text10, text11);
				List<object> item = tuple.images;
				List<object> item2 = tuple.folders;
				pyDict["ui_background_history"] = item;
				pyDict["ui_background_folder_history"] = item2;
			}
			pyDict["ui_background"] = text12;
			pyDict["ui_background_folder"] = text13;
		}
		array = new string[3] { "ui_nav_order", "ui_nav_pinned", "ui_nav_hidden" };
		foreach (string text14 in array)
		{
			if (Has(text14))
			{
				pyDict[text14] = _nav_keys(G(text14));
			}
		}
		if (Has("ui_section_members"))
		{
			PyDict pyDict2 = _nav_members(G("ui_section_members"));
			pyDict["ui_section_members"] = ((pyDict2.Count > 0) ? pyDict2 : null);
		}
		if (Has("ui_nav_groups"))
		{
			List<object> list = _nav_groups(G("ui_nav_groups"));
			pyDict["ui_nav_groups"] = ((list.Count > 0) ? list : null);
		}
		if (Has("ui_nav_style"))
		{
			string text15 = Py.Strip(Py.S(G("ui_nav_style")));
			pyDict["ui_nav_style"] = ((text15 == "compact" || text15 == "grouped") ? text15 : "grouped");
		}
		if (Has("ui_nav_defaults"))
		{
			pyDict["ui_nav_defaults"] = Py.S(G("ui_nav_defaults"));
		}
		if (Has("ui_sidebar_width"))
		{
			long num2;
			try
			{
				num2 = Math.Max(140L, Math.Min(320L, Py.Int(G("ui_sidebar_width"))));
			}
			catch (Exception ex) when (ex is TypeError || ex is ValueError)
			{
				num2 = 0L;
			}
			pyDict["ui_sidebar_width"] = ((num2 != 0L) ? ((object)num2) : null);
		}
		CONFIG.Update(pyDict);
		CONFIG.Save();
		if (pyDict.ContainsKey("use_system_proxy")) { Net.ApplyProxyPolicy(); Net.ResetClients(); }
		if (pyDict.ContainsKey("download_source") || pyDict.ContainsKey("use_system_proxy")) Source.InvalidateProbe();
		if (pyDict.ContainsKey("instances_dir"))
		{
			_inst_cache = null;
		}
		return null;
		object G(string k)
		{
			return d[k];
		}
		bool Has(string k)
		{
			return d.ContainsKey(k);
		}
	}

	public object lan_hint([Def(25565L)] object port = null)
	{
		return Lan.LanHint(port);
	}

	public object local_ips()
	{
		return Lan.LocalIps();
	}

	public object terracotta_player()
	{
		PyDict active = accounts.GetActive();
		if (active != null && active.Count > 0 && Py.Truthy(active["name"]))
		{
			return Py.Str(active["name"]);
		}
		return "Player";
	}

	private bool _game_on()
	{
		Process game_proc;
		lock (_game_lock)
		{
			game_proc = _game_proc;
		}
		if (game_proc == null)
		{
			return false;
		}
		try
		{
			return !game_proc.HasExited;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public object terracotta_snapshot()
	{
		return Terracotta.Snapshot((string)terracotta_player(), _game_on());
	}

	public object terracotta_prepare()
	{
		return StartTask("准备陶瓦联机", delegate(TaskContext ctx)
		{
			Terracotta.Install(_dm(ctx), delegate(string t)
			{
				ctx.Log(t);
			});
			ctx.Progress(1L, 1L, I18nCore.Tr("启动内核"));
			Terracotta.Start(delegate(string t)
			{
				ctx.Log(t);
			});
			return I18nCore.Tr("陶瓦联机已就绪");
		});
	}

	public object terracotta_host()
	{
		Terracotta.SetScanning((string)terracotta_player());
		return null;
	}

	public object terracotta_join(object room)
	{
		Terracotta.SetGuesting(room, (string)terracotta_player());
		return null;
	}

	public object terracotta_idle()
	{
		Terracotta.SetWaiting();
		return null;
	}

	public object terracotta_allow_firewall()
	{
		return Terracotta.AllowFirewall();
	}

	public object terracotta_open_firewall_settings()
	{
		Terracotta.OpenFirewallSettings();
		return null;
	}

	public object terracotta_shutdown()
	{
		Terracotta.Stop();
		return null;
	}

	public object terracotta_enter_world()
	{
		PyDict obj = (PyDict)terracotta_snapshot();
		string text = Py.Str(Py.Or(obj["url"], ""));
		if (!Py.Eq(obj["state"], "guest-ok") || text.Length == 0)
		{
			throw new TerracottaError(I18nCore.Tr("还没连上房间。请先输入邀请码加入。"));
		}
		return _launch_into_server(text, I18nCore.Tr("请到游戏「多人游戏」双击「陶瓦联机大厅」。"));
	}

	public object terracotta_direct_connect(object address)
	{
		var (text, num) = Terracotta.SplitJoinUrl(address);
		if (text.Length == 0 || text == "127.0.0.1" || text == "localhost")
		{
			throw new TerracottaError(I18nCore.Tr("请输入房主的公网地址，例如 1.2.3.4:25565"));
		}
		return _launch_into_server($"{text}:{num}", I18nCore.Tr("请到游戏「多人游戏」双击「陶瓦联机大厅」。"));
	}

	private object _launch_into_server(string url, string alreadyMsg)
	{
		Instance instance = _instance();
		Terracotta.RememberLobby(url, instance.path);
		if (Py.Truthy(((PyDict)terracotta_snapshot())["game_running"]))
		{
			return alreadyMsg;
		}
		List<object> list = instance.InstalledIds();
		if (list.Count == 0)
		{
			throw new LaunchError(I18nCore.Tr("请先到「启动」页安装一个版本。"));
		}
		string text = null;
		DateTime dateTime = DateTime.MinValue;
		foreach (string item3 in list)
		{
			DateTime lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(Utils.AbsPath(PPath.Join(instance.VersionsDir(), item3)));
			if (text == null || lastWriteTimeUtc > dateTime)
			{
				text = item3;
				dateTime = lastWriteTimeUtc;
			}
		}
		(string host, long port) tuple = Terracotta.SplitJoinUrl(url);
		string item = tuple.host;
		long item2 = tuple.port;
		PyDict active = accounts.GetActive();
		object account;
		object username;
		if (active != null && active.Count > 0 && Py.Eq(active["type"], "microsoft"))
		{
			account = Py.Or(active["name"], I18nCore.Tr("离线模式"));
			username = Py.Or(active["name"], "Player");
		}
		else
		{
			account = I18nCore.Tr("离线模式");
			username = Py.Or((active ?? new PyDict())["name"], terracotta_player());
		}
		return launch_game(instance.name, text, account, username, Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L)), Py.Int(Py.Or(Config.CONFIG.Get("width"), 854L)), Py.Int(Py.Or(Config.CONFIG.Get("height"), 480L)), "自动选择", new List<object>
		{
			"--server",
			item,
			"--port",
			Py.Str(item2)
		});
	}
}
