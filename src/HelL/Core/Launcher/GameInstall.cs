using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class GameInstall
{
	public static string[] ParseOptifineToken(object raw)
	{
		string text = Py.Strip(Py.S(raw)).Replace("-", "_");
		if (text.Length != 0)
		{
			List<string> list = (from p in text.Replace(" ", "_").Split('_')
				where p.Length > 0
				select p).ToList();
			if (list.Count >= 2)
			{
				return new string[2]
				{
					list[0],
					string.Join("_", list.Skip(1))
				};
			}
			return new string[2] { text, "" };
		}
		return new string[2] { "", "" };
	}

	public static string InstallGame(Installer installer, object version, object loader = null, object loader_version = null, object extra0 = null)
	{
		loader = loader ?? "无";
		PyDict pyDict = Py.DictCtor(Py.Or(extra0, new PyDict()));
		installer.skip_assets = Py.Truthy(pyDict.Get("skip_assets"));
		string text = Py.Strip(Py.S(version));
		if (text.Length == 0)
		{
			throw new InstallError("缺少 Minecraft 版本");
		}
		string text2 = Py.Lower(Py.Strip(Py.AsStr(Py.Or(loader, pyDict.Get("loader"), "无"), "strip")));
		bool flag = Py.Truthy(pyDict.Get("optifine")) || text2 == "optifine";
		bool flag2 = Py.Truthy(pyDict.Get("liteloader")) || text2 == "liteloader";
		if (text2 == "optifine" || text2 == "liteloader")
		{
			text2 = "无";
		}
		object b = Py.Or(pyDict.Get("loader_version"), loader_version, null);
		string text3 = Py.Str(Py.Or(pyDict.Get("optifine_type"), ""));
		string text4 = Py.Str(Py.Or(pyDict.Get("optifine_patch"), ""));
		if (Py.Truthy(pyDict.Get("optifine_version")) && text3.Length <= 0 && text4.Length <= 0)
		{
			string[] array = ParseOptifineToken(Py.Str(pyDict["optifine_version"]));
			text3 = array[0];
			text4 = array[1];
		}
		bool flag3 = text2 == "" || text2 == "无" || text2 == "none";
		string text5 = text;
		switch (text2)
		{
		case "fabric":
			text5 = installer.InstallFabric(text, S(Py.Or(pyDict.Get("fabric_version"), b)));
			break;
		case "quilt":
			text5 = installer.InstallQuilt(text, S(Py.Or(pyDict.Get("quilt_version"), b)));
			break;
		case "forge":
			text5 = installer.InstallForge(text, S(Py.Or(pyDict.Get("forge_version"), b)));
			break;
		case "neoforge":
			text5 = installer.InstallNeoforge(text, S(Py.Or(pyDict.Get("neoforge_version"), b)));
			break;
		default:
			if (!flag3)
			{
				throw new InstallError("未知加载器: " + Py.Str(loader));
			}
			installer.InstallVersion(text);
			text5 = text;
			break;
		}
		if (flag2)
		{
			try
			{
				string text6 = installer.InstallLiteloader(text);
				if (flag3)
				{
					text5 = text6;
				}
				else
				{
					installer._note("LiteLoader 版本已写入: " + text6, 0L, 1L);
				}
			}
			catch (Exception ex) when (!(ex is TaskCancelled))
			{
				installer._note("LiteLoader 安装失败: " + Py.ExcStr(ex), 0L, 1L);
				if (flag3)
				{
					throw;
				}
			}
		}
		if (flag)
		{
			switch (text2)
			{
			case "fabric":
			case "quilt":
			case "neoforge":
				installer._note("OptiFine 不能与 Fabric / Quilt / NeoForge 同装，已跳过", 0L, 1L);
				break;
			case "forge":
			{
				string dest_dir = PPath.Join(installer.instance.path, "mods");
				string text7 = Optifine.InstallAsMod(installer, text, dest_dir, text3, text4);
				installer._note("OptiFine 已作为 Forge 模组放入 mods/" + text7, 0L, 1L);
				break;
			}
			default:
				text5 = installer.InstallOptifine(text, text3, text4);
				break;
			}
		}
		return text5;
		static string S(object o)
		{
			if (o != null)
			{
				return Py.Str(o);
			}
			return null;
		}
	}
}
