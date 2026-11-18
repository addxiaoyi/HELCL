using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class HelpContent
{
	private static List<PyDict> _articles;

	public static List<PyDict> ARTICLES
	{
		get
		{
			List<PyDict> list = _articles;
			if (list == null)
			{
				List<PyDict> list2 = new List<PyDict>();
				list2.Add(PyDict.Of("id", "launch-fail", "title", "启动失败 / 闪退怎么办", "body", "1. 看崩溃弹窗里的「建议操作」，能禁用嫌疑 Mod、提高内存、下载合适 Java、修复版本文件。\n2. 启动页点启动前会做预检：磁盘不足、Mods 被解压成文件夹、Java 过旧会直接拦住。\n3. 仍不行：到「反馈」页把错误报告发给开发者（同意上传后才会发送）。"));
				list2.Add(PyDict.Of("id", "java", "title", "Java 怎么选", "body", "启动时默认自动匹配。也可在「下载 → Java」按版本下载：\n· Java 8：1.16 及更早\n· Java 17：1.18 – 1.20.4\n· Java 21：1.20.5 – 1.21.11\n· Java 25：26.1 及更新（含 26.3）\n发行版推荐 Adoptium；也可用 Zulu / Microsoft。"));
				list2.Add(PyDict.Of("id", "mods", "title", "模组 / 整合包安装", "body", "到「下载」页搜索 Modrinth / CurseForge（国内走镜像）。\n原版版本不会加载 mods 文件夹里的 jar，需要先装 Fabric / Forge / Quilt / NeoForge。\n不要把 .jar 解压成文件夹，否则会预检失败。"));
				list2.Add(PyDict.Of("id", "account", "title", "账号与正版登录", "body", "支持离线、微软设备码、皮肤站（Yggdrasil）、统一通行证（Nide8）。\n微软登录请按弹窗打开链接并输入代码；关掉窗口会取消后台轮询。"));
				list2.Add(PyDict.Of("id", "multiplayer", "title", "陶瓦联机", "body", "「联机」页可开房 / 加入。房间号形如 U/XXXX-XXXX-XXXX-XXXX。\n双方都要用兼容的陶瓦内核；防火墙提示按页面指引放行。"));
				list2.Add(PyDict.Of("id", "isolation", "title", "版本隔离与存档", "body", "每个实例是独立的 .minecraft。版本设置里可选隔离档位。\n存档可在版本相关对话框里备份 / 还原；删世界前建议先备份。"));
				list = (_articles = list2);
			}
			return list;
		}
	}

	public static List<object> ListArticles()
	{
		return ((IEnumerable<PyDict>)ARTICLES).Select((Func<PyDict, object>)((PyDict a) => PyDict.Of("id", a["id"], "title", a["title"]))).ToList();
	}

	public static PyDict GetArticle(object article_id)
	{
		string text = Py.Strip(Py.AsStr(Py.Or(article_id, ""), "strip"));
		foreach (PyDict aRTICLE in ARTICLES)
		{
			if ((string)aRTICLE["id"] == text)
			{
				return new PyDict(aRTICLE);
			}
		}
		return null;
	}

	public static List<object> SearchArticles(object query = null)
	{
		string text = Py.Lower(Py.Strip(Py.AsStr(Py.Or(query, ""), "strip")));
		if (text.Length == 0)
		{
			return ((IEnumerable<PyDict>)ARTICLES).Select((Func<PyDict, object>)((PyDict a) => new PyDict(a))).ToList();
		}
		List<object> list = new List<object>();
		foreach (PyDict aRTICLE in ARTICLES)
		{
			if (Py.Lower((string)aRTICLE["title"] + "\n" + (string)aRTICLE["body"]).Contains(text))
			{
				list.Add(new PyDict(aRTICLE));
			}
		}
		return list;
	}
}
