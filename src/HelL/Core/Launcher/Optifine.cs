using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher;

public static class Optifine
{
	public const string BMCL = "https://bmclapi2.bangbang93.com";

	public static List<object> ListBuilds(DownloadManager dm, string mc_version)
	{
		mc_version = Py.Strip(mc_version ?? "");
		if (mc_version.Length == 0)
		{
			return new List<object>();
		}
		object obj;
		try
		{
			obj = dm.FetchJson("https://bmclapi2.bangbang93.com/optifine/" + mc_version, 30L);
		}
		catch (Exception ex) when (!(ex is TaskCancelled))
		{
			throw new InstallError("无法获取 OptiFine 列表 (" + mc_version + "): " + Py.ExcStr(ex));
		}
		List<object> obj2 = (obj as List<object>) ?? new List<object>();
		List<object> list = new List<object>();
		foreach (object item in obj2)
		{
			if (item is PyDict pyDict)
			{
				object obj3 = Py.Or(pyDict.Get("type"), "HD_U");
				object obj4 = Py.Or(pyDict.Get("patch"), "");
				object obj5 = Py.Or(pyDict.Get("filename"), "OptiFine_" + mc_version + "_" + Py.Str(obj3) + "_" + Py.Str(obj4) + ".jar");
				list.Add(PyDict.Of("mc", Py.Or(pyDict.Get("mcversion"), mc_version), "type", obj3, "patch", obj4, "filename", obj5, "id", Py.RStrip(mc_version + "-OptiFine_" + Py.Str(obj3) + "_" + Py.Str(obj4), "_")));
			}
		}
		return list;
	}

	private static PyDict _latest(List<object> rows)
	{
		if (rows == null || rows.Count == 0)
		{
			throw new InstallError("该 Minecraft 版本没有 OptiFine");
		}
		return (PyDict)rows[0];
	}

	private static string _extract_launchwrapper(string opti_jar, string libs_dir)
	{
		ZipArchive zipArchive;
		try
		{
			zipArchive = Utils.OpenZip(opti_jar);
		}
		catch (InvalidDataException ex)
		{
			throw new InstallError("OptiFine 包损坏: " + ex.Message);
		}
		using (zipArchive)
		{
			List<ZipArchiveEntry> list = zipArchive.Entries.Where(delegate(ZipArchiveEntry e)
			{
				string text4 = Py.Lower(Utils.ZipEntryName(e));
				return text4.EndsWith(".jar") && text4.Contains("launchwrapper");
			}).ToList();
			if (list.Count == 0)
			{
				return null;
			}
			ZipArchiveEntry zipArchiveEntry = list[0];
			byte[] bytes;
			using (Stream stream = zipArchiveEntry.Open())
			{
				using MemoryStream memoryStream = new MemoryStream();
				stream.CopyTo(memoryStream);
				bytes = memoryStream.ToArray();
			}
			string name = new PPath(Utils.ZipEntryName(zipArchiveEntry)).Name;
			string text = "2.3";
			string[] array = name.Replace(".jar", "").Split('-');
			foreach (string text2 in array)
			{
				if (text2.Length > 0 && char.IsDigit(text2[0]))
				{
					text = text2;
					break;
				}
			}
			string text3 = PPath.Join(libs_dir, "net", "minecraft", "launchwrapper", text, "launchwrapper-" + text + ".jar");
			Utils.EnsureDir(new PPath(text3).Parent.ToString());
			File.WriteAllBytes(Utils.AbsPath(text3), bytes);
			return "net.minecraft:launchwrapper:" + text;
		}
	}

	private static bool _has_tweaker(string opti_jar)
	{
		try
		{
			using ZipArchive zipArchive = Utils.OpenZip(opti_jar);
			return zipArchive.Entries.Select(Utils.ZipEntryName).Any((string n) => n.Contains("OptiFineTweaker.class") || n.Contains("OptiFineForgeTweaker.class"));
		}
		catch (InvalidDataException)
		{
			return false;
		}
	}

	private static PyDict _profile(string mc_version, string typ, string patch, string wrapper)
	{
		string text = Py.RStrip(mc_version + "-OptiFine_" + typ + "_" + patch, "_");
		string text2 = Py.RStrip("optifine:OptiFine:" + mc_version + "_" + typ + "_" + patch, "_");
		List<object> list = new List<object>();
		list.Add(PyDict.Of("name", text2));
		List<object> list2 = list;
		list2.Add(PyDict.Of("name", wrapper ?? "net.minecraft:launchwrapper:1.12"));
		PyDict pyDict = PyDict.Of("id", text, "inheritsFrom", mc_version, "jar", mc_version, "type", "release", "mainClass", "net.minecraft.launchwrapper.Launch", "libraries", list2);
		List<long> list3 = Manifest.McVersionTuple(mc_version);
		if (Py.Cmp((list3 != null && list3.Count > 0) ? ((object)list3.Cast<object>().ToList()) : ((object)Py.L(1, 0)), Py.L(1, 13)) >= 0)
		{
			pyDict["arguments"] = PyDict.Of("game", Py.L("--tweakClass", "optifine.OptiFineTweaker"));
		}
		else
		{
			pyDict["minecraftArguments"] = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --tweakClass optifine.OptiFineTweaker";
		}
		return pyDict;
	}

	private static string _java_exe()
	{
		foreach (PyDict item in Java.ListInstalledJavas().Concat<object>(Java.FindSystemJavas()))
		{
			object o = item.Get("exe");
			if (Py.Truthy(o) && new PPath(Py.Str(o)).IsFile())
			{
				return Py.Str(o);
			}
		}
		return null;
	}

	private static bool _run_patcher(Installer installer, string opti_jar, string mc_version, string vid)
	{
		string text = PPath.Join(installer.instance.VersionsDir(), mc_version, mc_version + ".jar");
		if (!new PPath(text).IsFile())
		{
			return false;
		}
		string text2 = _java_exe();
		if (text2 == null)
		{
			installer._note("没有可用 Java，跳过 OptiFine Patcher", 0L, 1L);
			return false;
		}
		string text3 = PPath.Join(installer.instance.VersionsDir(), vid);
		Utils.EnsureDir(text3);
		string text4 = PPath.Join(text3, vid + ".jar");
		installer._note("运行 OptiFine Patcher", 0L, 1L);
		string so = "";
		string se = "";
		try
		{
			ProcessStartInfo processStartInfo = Utils.MakeStartInfo(new List<string> { text2, "-cp", opti_jar, "optifine.Patcher", text, text4, opti_jar });
			Process p = Process.Start(processStartInfo);
			try
			{
				try
				{
					p.StandardInput.Close();
				}
				catch (Exception)
				{
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					try
					{
						so = p.StandardOutput.ReadToEnd();
					}
					catch (Exception)
					{
					}
				})
				{
					IsBackground = true
				};
				Thread thread2 = new Thread((ThreadStart)delegate
				{
					try
					{
						se = p.StandardError.ReadToEnd();
					}
					catch (Exception)
					{
					}
				})
				{
					IsBackground = true
				};
				thread.Start();
				thread2.Start();
				if (!p.WaitForExit(180000))
				{
					try
					{
						p.Kill();
					}
					catch (Exception)
					{
					}
					installer._note("Patcher 失败: Command " + Py.Repr(processStartInfo.FileName) + " timed out after 180 seconds", 0L, 1L);
					return false;
				}
				thread.Join(2000);
				thread2.Join(2000);
			}
			finally
			{
				if (p != null)
				{
					((IDisposable)p).Dispose();
				}
			}
		}
		catch (Win32Exception ex3)
		{
			installer._note("Patcher 失败: " + ex3.Message, 0L, 1L);
			return false;
		}
		if (new PPath(text4).IsFile() && new FileInfo(Utils.AbsPath(text4)).Length > 1024)
		{
			return true;
		}
		string text5 = se + so;
		text5 = ((text5.Length > 240) ? text5.Substring(text5.Length - 240) : text5);
		installer._note("Patcher 未生成客户端 jar " + text5, 0L, 1L);
		return false;
	}

	private static PyDict Choose(Installer installer, List<object> rows, string typ, string patch, bool noteMismatch)
	{
		if (!string.IsNullOrEmpty(typ) || !string.IsNullOrEmpty(patch))
		{
			PyDict pyDict = rows.Cast<PyDict>().FirstOrDefault((PyDict r) => (string.IsNullOrEmpty(typ) || Py.Eq(r.Req("type"), typ)) && (string.IsNullOrEmpty(patch) || Py.Eq(r.Req("patch"), patch)));
			if (pyDict == null)
			{
				pyDict = _latest(rows);
				if (noteMismatch)
				{
					installer._note("未精确匹配 OptiFine " + typ + " " + patch + "，改用 " + Py.Str(pyDict.Req("type")) + " " + Py.Str(pyDict.Req("patch")), 0L, 1L);
				}
			}
			return pyDict;
		}
		return _latest(rows);
	}

	public static string InstallAsMod(Installer installer, string mc_version, string dest_dir, string typ = "", string patch = "")
	{
		Directory.CreateDirectory(Utils.AbsPath(dest_dir));
		DownloadManager dm = installer.dm;
		List<object> rows = ListBuilds(dm, mc_version);
		PyDict pyDict = Choose(installer, rows, typ, patch, noteMismatch: false);
		typ = Py.Str(pyDict.Req("type"));
		patch = Py.Str(pyDict.Req("patch"));
		string url = "https://bmclapi2.bangbang93.com/optifine/" + mc_version + "/" + typ + "/" + patch;
		string text = PPath.Join(dest_dir, Py.Str(pyDict.Req("filename")));
		installer._note("下载 OptiFine " + typ + "_" + patch + " 到 mods", 0L, 1L);
		dm.Download(url, text);
		if (!new PPath(text).IsFile() || new FileInfo(Utils.AbsPath(text)).Length < 1024)
		{
			throw new InstallError("OptiFine 下载失败或文件过小");
		}
		return new PPath(text).Name;
	}

	public static string Install(Installer installer, string mc_version, string typ = "", string patch = "", bool force = false)
	{
		DownloadManager dm = installer.dm;
		mc_version = Py.Strip(mc_version ?? "");
		List<object> rows = ListBuilds(dm, mc_version);
		PyDict pyDict = Choose(installer, rows, typ, patch, noteMismatch: true);
		typ = Py.Str(pyDict.Req("type"));
		patch = Py.Str(pyDict.Req("patch"));
		string text = Py.Str(pyDict.Req("id"));
		if (installer.instance.HasVersion(text) && !force)
		{
			installer._note("已安装 " + text, 0L, 1L);
			return text;
		}
		installer._note("安装原版 " + mc_version + "（OptiFine 依赖）", 0L, 1L);
		installer.InstallVersion(mc_version);
		string url = "https://bmclapi2.bangbang93.com/optifine/" + mc_version + "/" + typ + "/" + patch;
		string text2 = installer.instance.LibrariesDir();
		string text3 = Py.RStrip(mc_version + "_" + typ + "_" + patch, "_");
		string text4 = PPath.Join(text2, "optifine", "OptiFine", text3, "OptiFine-" + text3 + ".jar");
		installer._note("下载 OptiFine " + typ + "_" + patch, 0L, 1L);
		bool force2 = force;
		dm.Download(url, text4, null, null, force2);
		if (!new PPath(text4).IsFile() || new FileInfo(Utils.AbsPath(text4)).Length < 1024)
		{
			throw new InstallError("OptiFine 下载失败或文件过小");
		}
		if (!_has_tweaker(text4))
		{
			if (_run_patcher(installer, text4, mc_version, text))
			{
				PyDict vjson = PyDict.Of("id", text, "inheritsFrom", mc_version, "jar", text, "type", "release");
				installer._note("写入 Patcher 版本 " + text, 0L, 1L);
				installer._install_json(text, vjson, force);
				return text;
			}
			installer._note("未找到 OptiFineTweaker，仍按 Tweaker 结构写入版本", 0L, 1L);
		}
		string wrapper = _extract_launchwrapper(text4, text2);
		PyDict pyDict2 = _profile(mc_version, typ, patch, wrapper);
		installer._note("写入版本 " + Py.Str(pyDict2["id"]), 0L, 1L);
		installer._install_json(Py.Str(pyDict2["id"]), pyDict2, force);
		return Py.Str(pyDict2["id"]);
	}
}
