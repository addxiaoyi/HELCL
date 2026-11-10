using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class Crash
{
	private sealed class Analyzer
	{
		public string log_mc = "";

		public string log_debug = "";

		public string log_hs = "";

		public string log_crash = "";

		public string log_all = "";

		public readonly PyDict reasons = new PyDict();

		public readonly List<string> files = new List<string>();

		public string direct_file = "";

		public string raw_output = "";

		public void Append(string code, object extra = null)
		{
			List<string> list = new List<string>();
			if (extra != null)
			{
				if (extra is string text)
				{
					if (text.Length > 0)
					{
						list.Add(text);
					}
				}
				else
				{
					foreach (object item in (IEnumerable)extra)
					{
						if (Py.Truthy(item))
						{
							list.Add(Py.Str(item));
						}
					}
				}
			}
			if (reasons.ContainsKey(code))
			{
				IEnumerable<string> xs = ((List<object>)reasons[code]).Select(Py.Str).Concat<string>(list);
				reasons[code] = Dedup(xs).Cast<object>().ToList();
			}
			else
			{
				reasons[code] = Dedup(list).Cast<object>().ToList();
			}
		}

		public bool Prepare(List<string> fileList, List<string> output_lines)
		{
			Dictionary<string, List<KeyValuePair<string, List<string>>>> buckets = new Dictionary<string, List<KeyValuePair<string, List<string>>>>
			{
				{
					"hs",
					new List<KeyValuePair<string, List<string>>>()
				},
				{
					"crash",
					new List<KeyValuePair<string, List<string>>>()
				},
				{
					"mc",
					new List<KeyValuePair<string, List<string>>>()
				},
				{
					"extra_log",
					new List<KeyValuePair<string, List<string>>>()
				},
				{
					"extra_txt",
					new List<KeyValuePair<string, List<string>>>()
				}
			};
			foreach (string file in fileList)
			{
				string text = Classify(file);
				if (text == "ignore")
				{
					continue;
				}
				string s = ReadText(file, 0L);
				if (Py.Strip(s).Length != 0)
				{
					List<string> value = Py.SplitLines(s);
					buckets[text].Add(new KeyValuePair<string, List<string>>(file, value));
					if ((text == "hs" || text == "crash") && direct_file.Length == 0)
					{
						direct_file = file;
					}
					if (text == "mc")
					{
						direct_file = file;
					}
				}
			}
			if (output_lines != null && output_lines.Count > 0)
			{
				List<string> list = (from ln in output_lines
					where ln != null
					select Py.RStrip(ln, "\r")).ToList();
				raw_output = string.Join("\n", list);
				if (list.Count > 0)
				{
					buckets["mc"].Insert(0, new KeyValuePair<string, List<string>>("rawoutput.log", list));
				}
			}
			if (!new string[3] { "hs", "crash", "mc" }.Any((string k) => buckets[k].Count > 0) && buckets["extra_log"].Count > 0)
			{
				buckets["mc"] = buckets["extra_log"];
				buckets["extra_log"] = new List<KeyValuePair<string, List<string>>>();
			}
			if (buckets["hs"].Count > 0)
			{
				KeyValuePair<string, List<string>> keyValuePair = MaxMtime(buckets["hs"]);
				files.Add(keyValuePair.Key);
				log_hs = HeadTailLines(keyValuePair.Value, 200, 100);
				if (direct_file.Length == 0)
				{
					direct_file = keyValuePair.Key;
				}
			}
			if (buckets["crash"].Count > 0)
			{
				KeyValuePair<string, List<string>> keyValuePair2 = MaxMtime(buckets["crash"]);
				files.Add(keyValuePair2.Key);
				log_crash = HeadTailLines(keyValuePair2.Value, 300, 700);
				if (direct_file.Length == 0 || Classify(direct_file) == "mc")
				{
					direct_file = keyValuePair2.Key;
				}
			}
			if (buckets["mc"].Count > 0)
			{
				PyDict pyDict = new PyDict();
				foreach (KeyValuePair<string, List<string>> item in buckets["mc"])
				{
					pyDict[Py.Lower(new PPath(item.Key).Name)] = item;
				}
				foreach (KeyValuePair<string, List<string>> item2 in buckets["mc"])
				{
					files.Add(item2.Key);
				}
				bool flag = false;
				string[] array = new string[6] { "rawoutput.log", "游戏崩溃前的输出.txt", "latest.log", "latest log.txt", "debug.log", "debug log.txt" };
				foreach (string text2 in array)
				{
					if (pyDict.ContainsKey(text2))
					{
						KeyValuePair<string, List<string>> keyValuePair3 = (KeyValuePair<string, List<string>>)pyDict[text2];
						if (text2 == "rawoutput.log" || text2 == "游戏崩溃前的输出.txt")
						{
							log_mc += string.Join("\n", Py.Slice(keyValuePair3.Value, -500));
						}
						else
						{
							log_mc = log_mc + "\n" + HeadTailLines(keyValuePair3.Value, 1500, 500);
						}
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					log_mc = log_mc + "\n" + HeadTailLines(buckets["mc"][0].Value, 1500, 500);
				}
				array = new string[2] { "debug.log", "debug log.txt" };
				foreach (string key in array)
				{
					if (pyDict.ContainsKey(key))
					{
						log_debug = HeadTailLines(((KeyValuePair<string, List<string>>)pyDict[key]).Value, 1000, 0);
						break;
					}
				}
			}
			foreach (KeyValuePair<string, List<string>> item3 in buckets["extra_log"].Concat(buckets["extra_txt"]))
			{
				files.Add(item3.Key);
			}
			log_mc = Py.Strip(log_mc ?? "");
			log_debug = Py.Strip(log_debug ?? "");
			log_hs = Py.Strip(log_hs ?? "");
			log_crash = Py.Strip(log_crash ?? "");
			log_all = log_mc + log_hs + log_crash;
			if (log_mc.Length <= 0 && log_hs.Length <= 0)
			{
				return log_crash.Length > 0;
			}
			return true;
			static KeyValuePair<string, List<string>> MaxMtime(List<KeyValuePair<string, List<string>>> l)
			{
				KeyValuePair<string, List<string>> result = l[0];
				double num2 = Mtime(result.Key);
				foreach (KeyValuePair<string, List<string>> item4 in l)
				{
					double num3 = Mtime(item4.Key);
					if (num3 > num2)
					{
						result = item4;
						num2 = num3;
					}
				}
				return result;
			}
		}

		public void Analyze()
		{
			if (log_mc.Length == 0 && log_hs.Length == 0 && log_crash.Length == 0)
			{
				Append("no_files");
				return;
			}
			Crit1();
			if (reasons.Count > 0)
			{
				return;
			}
			Crit2();
			if (reasons.Count > 0)
			{
				return;
			}
			string blob = log_all;
			if (new string[4] { "orge", "abric", "uilt", "iteloader" }.Any((string t) => blob.Contains(t)))
			{
				if (log_crash.Length > 0)
				{
					throw new ReError("look-behind requires fixed-width pattern");
				}
				if (log_mc.Length > 0)
				{
					List<string> list = (from Match m in Rx("/FATAL] .+?(?=[\\n]+\\[)", RegexOptions.Singleline).Matches(log_mc)
						select m.Value).ToList();
					if (log_mc.Contains("Unreported exception thrown!"))
					{
						list.Add(Between(log_mc, "Unreported exception thrown!", "at oolloo.jlw.Wrapper"));
					}
					if (list.Count > 0)
					{
						throw new ReError("look-behind requires fixed-width pattern");
					}
				}
				if (log_hs.Length > 0)
				{
					throw new ReError("look-behind requires fixed-width pattern");
				}
			}
			Crit3();
		}

		private void Crit1()
		{
			string text = log_mc;
			string text2 = log_hs;
			string text3 = log_crash;
			if (text3.Length > 0)
			{
				if (text3.Contains("Unable to make protected final java.lang.Class java.lang.ClassLoader.defineClass"))
				{
					Append("java_too_new");
				}
				if (text3.Contains("Failed loading config file "))
				{
					BadPattern(text3);
				}
			}
			if (text.Length > 0)
			{
				if (text.Contains("Unrecognized option:"))
				{
					Append("jvm_args");
				}
				if (text.Contains("Found multiple arguments for option fml.forgeVersion, but you asked for only one"))
				{
					Append("multi_forge_json");
				}
				if (text.Contains("The driver does not appear to support OpenGL"))
				{
					Append("no_opengl");
				}
				if (text.Contains("java.lang.ClassCastException: java.base/jdk") || text.Contains("java.lang.ClassCastException: class jdk."))
				{
					Append("jdk");
				}
				if (new string[6] { "java.lang.NoSuchMethodError: 'void net.minecraft.client.renderer.texture.SpriteContents.", "java.lang.NoSuchMethodError: 'java.lang.String com.mojang.blaze3d.systems.RenderSystem.getBackendDescription", "java.lang.NoSuchMethodError: 'void net.minecraft.client.renderer.block.model.BakedQuad.", "java.lang.NoSuchMethodError: 'void net.minecraftforge.client.gui.overlay.ForgeGui.renderSelectedItemName", "java.lang.NoSuchMethodError: 'void net.minecraft.server.level.DistanceManager", "java.lang.NoSuchMethodError: 'net.minecraft.network.chat.FormattedText net.minecraft.client.gui.Font.ellipsize" }.Any(text.Contains))
				{
					Append("optifine_forge");
				}
				if (text.Contains("Open J9 is not supported") || text.Contains("OpenJ9 is incompatible") || text.Contains(".J9VMInternals."))
				{
					Append("openj9");
				}
				if (text.Contains("java.lang.NoSuchFieldException: ucp"))
				{
					Append("java_too_new");
				}
				if (text.Contains("because module java.base does not export"))
				{
					Append("java_too_new");
				}
				if (text.Contains("java.lang.ClassNotFoundException: jdk.nashorn.api.scripting.NashornScriptEngineFactory"))
				{
					Append("java_too_new");
				}
				if (text.Contains("java.lang.ClassNotFoundException: java.lang.invoke.LambdaMetafactory"))
				{
					Append("java_too_new");
				}
				if (text.Contains("The directories below appear to be extracted jar files. Fix this before you continue."))
				{
					Append("mod_unzipped");
				}
				if (text.Contains("Extracted mod jars found, loading will NOT continue"))
				{
					Append("mod_unzipped");
				}
				if (text.Contains("java.lang.ClassNotFoundException: org.spongepowered.asm.launch.MixinTweaker"))
				{
					Append("mixin_bootstrap");
				}
				if (text.Contains("Couldn't set pixel format"))
				{
					Append("pixel_format");
				}
				if (text.Contains("java.lang.OutOfMemoryError") || text.Contains("an out of memory error"))
				{
					Append("oom");
				}
				if (text.Contains("Shaders Mod detected. Please remove it, OptiFine has built-in support for shaders."))
				{
					Append("shaders_optifine");
				}
				if (text.Contains("java.lang.NoSuchMethodError: sun.security.util.ManifestEntryVerifier") || text.Contains("java.lang.NoSuchMethodError: 'void sun.security.util.ManifestEntryVerifier"))
				{
					Append("old_forge_new_java");
				}
				if (text.Contains("1282: Invalid operation"))
				{
					Append("opengl_1282");
				}
				if (text.Contains("signer information does not match signer information of other classes in the same package"))
				{
					Append("verify_fail", Seek(text, "(?<=class \")[^\"]+(?=\"'s signer information)"));
				}
				if (text.Contains("Maybe try a lower resolution resourcepack?"))
				{
					Append("hd_pack");
				}
				if (text.Contains("ChunkManager$ProxyTicketManager.shouldForceTicks(J)Z") && text.Contains("OptiFine"))
				{
					Append("optifine_world");
				}
				if (text.Contains("com.electronwill.nightconfig.core.io.ParsingException: Not enough data available") && !reasons.ContainsKey("mod_config"))
				{
					Append("nightconfig");
				}
				if (text.Contains("error remapping game jars"))
				{
					Append("fabric_remap", Seek(text, "(?<=the jar file ).+?(?= can't be written)"));
				}
				if (text.Contains("Cannot find launch target fmlclient, unable to launch"))
				{
					Append("forge_incomplete");
				}
				if (text.Contains("Invalid paths argument, contained no existing paths") && text.Contains("libraries\\net\\minecraftforge\\fmlcore"))
				{
					Append("forge_incomplete");
				}
				if (text.Contains("Invalid module name: '' is not a Java identifier"))
				{
					Append("mod_name_chars");
				}
				if (text.Contains("has been compiled by a more recent version of the Java Runtime (class file version 55.0)"))
				{
					Append("need_java11");
				}
				if (text.Contains("sun.misc.Unsafe.defineAnonymousClass(Class,byte[],Object[])Class/invokeVirtual"))
				{
					Append("need_java11");
				}
				if (text.Contains("The requested compatibility level JAVA_11 could not be set"))
				{
					Append("need_java11");
				}
				if (text.Contains("Unsupported class file major version") || text.Contains("Unsupported major.minor version"))
				{
					Append("java_mismatch");
				}
				if (text.Contains("Level is not supported by the active JRE or ASM version"))
				{
					Append("java_mismatch");
				}
				if (text.Contains("Invalid maximum heap size"))
				{
					Append("java32");
				}
				if (text.Contains("Could not reserve enough space"))
				{
					if (text.Contains("for 1048576KB object heap"))
					{
						Append("java32");
					}
					else
					{
						Append("oom");
					}
				}
				if (text.Contains("Caught exception from "))
				{
					string keyword = Py.Strip(Seek(text, "(?<=Caught exception from )[^\\n]+"));
					Append("mod_certain", TryModName(keyword, text3, log_debug));
				}
				if (text.Contains("DuplicateModsFoundException"))
				{
					BadPattern(text);
				}
				if (text.Contains("Found a duplicate mod"))
				{
					Append("mod_dup", SearchAll(Seek(text, "Found a duplicate mod[^\\n]+"), "[^\\\\/]+\\.jar", RegexOptions.IgnoreCase));
				}
				if (text.Contains("Found duplicate mods"))
				{
					Append("mod_dup", Dedup(SearchAll(text, "(?<=Mod ID: ')\\w+?(?=' from mod files:)")));
				}
				if (text.Contains("ModResolutionException: Duplicate"))
				{
					Append("mod_dup", SearchAll(Seek(text, "ModResolutionException: Duplicate[^\\n]+"), "[^\\\\/]+\\.jar", RegexOptions.IgnoreCase));
				}
				if (text.Contains("Incompatible mods found!"))
				{
					BadPattern(text);
				}
				if (text.Contains("Missing or unsupported mandatory dependencies:"))
				{
					List<string> extra = (from d in SearchAll(text, "(?<=Missing or unsupported mandatory dependencies:)(?:[\\n\\r]+\\t.*)+", RegexOptions.IgnoreCase)
						select Py.Strip(Rx("^\\s+", RegexOptions.Multiline).Replace(d, ""))).ToList();
					Append("mod_missing", extra);
				}
			}
			if (text2.Length > 0)
			{
				if (text2.Contains("The system is out of physical RAM or swap space") || text2.Contains("Out of Memory Error"))
				{
					Append("oom");
				}
				if (text2.Contains("EXCEPTION_ACCESS_VIOLATION"))
				{
					if (text2.Contains("# C [ig"))
					{
						Append("intel_av");
					}
					if (text2.Contains("# C [atio"))
					{
						Append("amd_av");
					}
					if (text2.Contains("# C [nvoglv"))
					{
						Append("nvidia_av");
					}
				}
			}
			if (text3.Length <= 0)
			{
				return;
			}
			if (text3.Contains("maximum id range exceeded"))
			{
				Append("mod_id_limit");
			}
			if (text3.Contains("java.lang.OutOfMemoryError"))
			{
				Append("oom");
			}
			if (text3.Contains("Pixel format not accelerated"))
			{
				Append("pixel_format");
			}
			if (text3.Contains("Manually triggered debug crash"))
			{
				Append("debug_crash");
			}
			if (text3.Contains("has mods that were not found") && Rx("The Mod File [^\\n]+optifine\\\\OptiFine[^\\n]+ has mods that were not found", RegexOptions.IgnoreCase).IsMatch(text3))
			{
				Append("optifine_forge");
			}
			if (text3.Contains("-- MOD "))
			{
				string text4 = Between(text3, "-- MOD ", "Failure message:");
				if (Py.Lower(text4).Contains(".jar"))
				{
					Append("mod_certain", Py.Strip(Seek(text4, "(?<=Mod File: ).+")));
				}
				else
				{
					string extra2 = Py.Strip(Seek(text3, "(?<=Failure message: )[\\w\\W]+?(?=\\tMod)").Replace("\t", " "));
					Append("loader_error", extra2);
				}
			}
			if (text3.Contains("Multiple entries with same key: "))
			{
				Append("mod_certain", TryModName(Py.Strip(Seek(text3, "(?<=Multiple entries with same key: )[^=]+")), text3, log_debug));
			}
			if (text3.Contains("LoaderExceptionModCrash: Caught exception from "))
			{
				Append("mod_certain", TryModName(Py.Strip(Seek(text3, "(?<=LoaderExceptionModCrash: Caught exception from )[^\\n]+")), text3, log_debug));
			}
		}

		private bool MixinAnalyze(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			if (!text.Contains("Mixin prepare failed ") && !text.Contains("Mixin apply failed ") && !text.Contains("MixinApplyError") && !text.Contains("MixinTransformerError") && !text.Contains("mixin.injection.throwables.") && !text.Contains(".json] FAILED during )"))
			{
				return false;
			}
			string text2 = Seek(text, "(?<=from mod )[^./ ]+(?=] from)");
			if (text2.Length == 0)
			{
				text2 = Seek(text, "(?<=for mod )[^./ ]+(?= failed)");
			}
			if (text2.Length > 0)
			{
				Append("mixin", TryModName(Py.Strip(text2), log_crash, log_debug));
				return true;
			}
			BadPattern(text);
			Append("mixin");
			return true;
		}

		private void Crit2()
		{
			string text = log_mc;
			string text2 = log_crash;
			bool flag = false;
			if (text.Length > 0)
			{
				flag = MixinAnalyze(text);
				if (text.Contains("An exception was thrown, the game will display an error screen and halt."))
				{
					BadPattern(text);
				}
				(string, string)[] array = new(string, string)[3]
				{
					("A potential solution has been determined:\n", "(?<=A potential solution has been determined:\\n)(?:[ \\t]+-[ \\t]+[^\\n]+\\n?)+"),
					("A potential solution has been determined, this may resolve your problem:\n", "(?<=A potential solution has been determined, this may resolve your problem:\\n)(?:[ \\t]+-[ \\t]+[^\\n]+\\n?)+"),
					("确定了一种可能的解决方法，这样做可能会解决你的问题：\n", "(?<=确定了一种可能的解决方法，这样做可能会解决你的问题：\\n)(?:[ \\t]+-[ \\t]+[^\\n]+\\n?)+")
				};
				for (int i = 0; i < array.Length; i++)
				{
					var (s, pat) = array[i];
					if (text.Contains(Py.Strip(s)))
					{
						string s2 = Seek(text, pat);
						List<string> list = (from ln in Py.SplitLines(s2)
							where Py.Strip(ln).Length > 0
							select Py.Strip(ln, " \t-")).ToList();
						Append("fabric_solution", (list.Count > 0) ? string.Join("\n", list) : Py.Strip(s2));
					}
				}
				if (!flag && text.Contains("due to errors, provided by "))
				{
					Append("mod_certain", TryModName(Py.Strip(Seek(text, "(?<=due to errors, provided by ')[^']+")), text2, log_debug));
				}
			}
			if (text2.Length <= 0)
			{
				return;
			}
			MixinAnalyze(text2);
			if (text2.Contains("Suspected Mod"))
			{
				string text3 = Between(text2, "Suspected Mod", "Stacktrace");
				if (!text3.StartsWith("s: None"))
				{
					BadPattern(text3);
				}
			}
		}

		private void Crit3()
		{
			string text = log_mc;
			string text2 = log_crash;
			if (text.Length > 0)
			{
				if (!text.Contains("at net.") && !text.Contains("INFO]") && log_hs.Length == 0 && log_crash.Length == 0 && text.Length < 100)
				{
					Append("tiny_output", text);
				}
				if (text.Contains("Mod resolution failed"))
				{
					Append("loader_error");
				}
				if (text.Contains("Failed to create mod instance."))
				{
					string text3 = Seek(text, "(?<=Failed to create mod instance. ModID: )[^,]+");
					if (text3.Length == 0)
					{
						text3 = Seek(text, "(?<=Failed to create mod instance. ModId )[^\\n]+(?= for )");
					}
					Append("mod_init", TryModName(Py.Strip(text3), text2, log_debug));
				}
			}
			if (text2.Length > 0)
			{
				if (text2.Contains("\tBlock location: World: "))
				{
					string text4 = Seek(text2, "(?<=\\tBlock: Block\\{)[^}]+");
					string text5 = Seek(text2, "(?<=\\tBlock location: World: )\\([^)]+\\)");
					Append("bad_block", Py.Strip(text4 + " " + text5));
				}
				if (text2.Contains("\tEntity's Exact location: "))
				{
					string text6 = Seek(text2, "(?<=\\tEntity Type: )[^\\n]+(?= \\()");
					string text7 = Py.Strip(Seek(text2, "(?<=\\tEntity's Exact location: )[^\\n]+"));
					Append("bad_entity", (text6.Length > 0) ? (text6 + " (" + text7 + ")") : text7);
				}
			}
		}
	}

	public const string HELP_FOOTER = "如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。";

	private static readonly string[] _STACK_IGNORE_PREFIX = new string[20]
	{
		"java", "sun", "javax", "jdk", "oolloo", "org.lwjgl", "com.sun", "net.minecraftforge", "paulscode.sound", "com.mojang",
		"net.minecraft", "cpw.mods", "com.google", "org.apache", "org.spongepowered", "net.fabricmc", "com.mumfrey", "com.electronwill.nightconfig", "it.unimi.dsi", "MojangTricksIntelDriversForPerformance_javaw"
	};

	private static readonly Regex _TOKEN_RE = new Regex("(?i)((?:access[_-]?token|session[_-]?token|refresh[_-]?token|client[_-]?secret|authorization)\\s*[:=]\\s*)([^\\s\"']+)", RegexOptions.CultureInvariant);

	private static readonly Regex _UUID_AUTH_RE = new Regex("(?i)(--(?:accessToken|uuid|xuid|clientId)\\s+)\\S+", RegexOptions.CultureInvariant);

	private const string LOOKBEHIND = "look-behind requires fixed-width pattern";

	private static readonly Dictionary<long, string> _NT = new Dictionary<long, string>
	{
		{ 3221225477L, "内存访问冲突，常见于显卡驱动或原生库" },
		{ 3221226505L, "栈缓冲溢出，常见于 NVIDIA/显卡驱动" },
		{ 3221225781L, "找不到 DLL，缺运行库" },
		{ 3221225786L, "进程被关闭（Ctrl+C 或点了控制台关闭）" },
		{ 3221225794L, "DLL 初始化失败" },
		{ 3221226356L, "堆损坏" },
		{ 3221225725L, "栈溢出" }
	};

	private static readonly Dictionary<string, KeyValuePair<string, bool>> _TABLE = new Dictionary<string, KeyValuePair<string, bool>>
	{
		{
			"jvm_args",
			R("由于 Java 参数有误，导致游戏无法继续运行。\n请检查启动设置里的 Java 虚拟机参数和游戏参数是否有误。", h: false)
		},
		{
			"mod_unzipped",
			R("由于 Mod 文件被解压了，导致游戏无法继续运行。\n直接把整个 Mod 文件放进 mods 文件夹即可，解压就会出错。\n\n请删除 mods 里已被解压的文件夹，然后再启动游戏。", h: false)
		},
		{
			"oom",
			R("Minecraft 内存不足，导致其无法继续运行。\n这很可能是电脑内存不足、游戏分配的内存不足，或配置要求过高。\n\n请在启动设置中增加为游戏分配的内存，并删掉过高的材质、Mod、光影。\n如果还不行，启动前尽量关掉其它软件。", h: true)
		},
		{
			"openj9",
			R("游戏因为使用 OpenJ9 而崩溃了。\n请在本实例的 Java 选项中改用非 OpenJ9 的 Java，然后再启动。", h: false)
		},
		{
			"jdk",
			R("游戏似乎因为使用 JDK，或 Java 版本过高而崩溃了。\n请在本实例的 Java 选项中改用 Java 8，然后再启动。", h: false)
		},
		{
			"java_too_new",
			R("游戏似乎因为你使用的 Java 版本过高而崩溃了。\n请在本实例的 Java 选项中改用较低版本的 Java，然后再启动。", h: false)
		},
		{
			"java_mismatch",
			R("游戏不兼容你当前使用的 Java。\n请在本实例的 Java 选项中改用合适版本的 Java，然后再启动。", h: false)
		},
		{
			"mod_name_chars",
			R("由于有 Mod 的名称包含特殊字符，导致游戏崩溃。\n请把该 Mod 文件名改成只含英文、数字、减号、下划线和小数点，然后再启动。", h: false)
		},
		{
			"mixin_bootstrap",
			R("由于缺失 MixinBootstrap，导致游戏崩溃。\n请尝试安装 MixinBootstrap。", h: false)
		},
		{
			"need_java11",
			R("你安装的部分 Mod 需要使用 Java 11 启动。\n请在本实例的 Java 选项中改用 Java 11，然后再启动。", h: false)
		},
		{
			"optifine_world",
			R("你使用的 OptiFine 可能导致游戏出问题。\n该问题只在特定 OptiFine 版本出现，可以换一个 OptiFine 版本。", h: true)
		},
		{
			"hd_pack",
			R("材质分辨率过高，或显卡配置不足，导致游戏无法继续运行。\n如果正在用高清材质，先把它移除。否则请更新显卡驱动。", h: true)
		},
		{
			"nightconfig",
			R("由于 Night Config 存在问题，导致了游戏崩溃。\n可以尝试安装 Night Config Fixes 模组。", h: true)
		},
		{
			"opengl_1282",
			R("你使用的光影或材质导致游戏出现了问题。\n请尝试删除这些额外资源。", h: true)
		},
		{
			"mod_id_limit",
			R("安装的 Mod 过多，超出了游戏的 ID 限制。\n请尝试安装 JEID 等修复 Mod，或删除部分大型 Mod。", h: false)
		},
		{
			"verify_fail",
			R("部分文件或内容校验失败，导致游戏出问题。\n请尝试删除游戏（包括 Mod）并重新下载。", h: true)
		},
		{
			"forge_incomplete",
			R("由于安装的 Forge 文件丢失，游戏无法正常运行。\n请重新安装一次相同版本的 Forge。", h: true)
		},
		{
			"debug_crash",
			R("* 事实上，你的游戏没有任何问题，这是你自己触发的调试崩溃。", h: false)
		},
		{
			"optifine_forge",
			R("由于 OptiFine 与当前版本的 Forge 不兼容，导致游戏崩溃。\n请到 OptiFine 官网查看它所兼容的 Forge 版本，并按对应版本重装。", h: false)
		},
		{
			"shaders_optifine",
			R("无需同时安装 OptiFine 和 Shaders Mod，OptiFine 已经集成了光影功能。\n删掉 Shaders Mod 后再启动。", h: false)
		},
		{
			"old_forge_new_java",
			R("由于低版本 Forge 与当前 Java 不兼容，导致游戏崩溃。\n请尝试：把 Forge 更新到 36.2.26 或更高，或换用低于 1.8.0.320 的 Java。", h: false)
		},
		{
			"multi_forge_json",
			R("当前版本文件存在异常（版本 Json 里有多个 Forge）。\n请尝试重新全新安装 Forge。", h: false)
		},
		{
			"no_files",
			R("你的游戏出现了一些问题，但未能找到相关记录文件，因此无法继续分析。", h: true)
		}
	};

	private static readonly PyDict _TITLES = PyDict.Of("oom", "内存不足", "java_mismatch", "Java 版本不兼容", "java_too_new", "Java 版本过高", "need_java11", "需要 Java 11", "mod_certain", "Mod 导致崩溃", "mixin", "Mod 注入失败", "fabric_solution", "Fabric 报错", "fabric_remap", "Fabric 无法生成游戏 jar", "forge_error", "Forge 报错", "mod_dup", "Mod 重复安装", "mod_missing", "缺少前置 Mod", "mod_incompat", "Mod 不兼容", "nvidia_av", "显卡驱动崩溃", "intel_av", "显卡驱动崩溃", "amd_av", "显卡驱动崩溃", "pixel_format", "显卡无法设置像素格式", "debug_crash", "手动调试崩溃");

	public static Action<string> ShellOpenHook;

	private static void BadPattern(string text)
	{
		if (!string.IsNullOrEmpty(text))
		{
			throw new ReError("look-behind requires fixed-width pattern");
		}
	}

	public static long? NormalizeExit(object code)
	{
		if (code == null)
		{
			return null;
		}
		long num;
		try
		{
			num = Py.Int(code);
		}
		catch (TypeError)
		{
			return null;
		}
		catch (ValueError)
		{
			return null;
		}
		if (num > int.MaxValue)
		{
			num -= 4294967296L;
		}
		return num;
	}

	public static string ExitHint(object code)
	{
		long? num = NormalizeExit(code);
		if (!num.HasValue)
		{
			return "";
		}
		long value = num.Value;
		long key = value & 0xFFFFFFFFu;
		string text = "0x" + key.ToString("x8");
		if (_NT.TryGetValue(key, out string value2))
		{
			return "退出码 " + value + "（" + text + "）：" + value2;
		}
		if (value == 0L)
		{
			return "";
		}
		if (value == 1)
		{
			return "退出码 1：一般错误";
		}
		if (value < 0)
		{
			return "退出码 " + value + "（" + text + "）";
		}
		return "退出码 " + value;
	}

	private static Encoding Strict(int cp)
	{
		return Encoding.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
	}

	public static string ReadText(string path, long max_bytes = 0L)
	{
		byte[] array;
		try
		{
			array = File.ReadAllBytes(Utils.AbsPath(path));
		}
		catch (Exception)
		{
			return "";
		}
		if (max_bytes > 0 && array.Length > max_bytes)
		{
			array = array.Skip((int)(array.Length - max_bytes)).ToArray();
		}
		try
		{
			return Utils.DecodeUtf8Sig(array);
		}
		catch (Exception)
		{
		}
		int[] array2 = new int[2] { 54936, 936 };
		foreach (int cp in array2)
		{
			try
			{
				return Strict(cp).GetString(array);
			}
			catch (Exception)
			{
			}
		}
		return Encoding.UTF8.GetString(array);
	}

	public static string FilterSecrets(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		text = _TOKEN_RE.Replace(text, "$1***");
		text = _UUID_AUTH_RE.Replace(text, "$1***");
		return text;
	}

	private static Regex Rx(string pat, RegexOptions o = RegexOptions.None)
	{
		return new Regex(pat, o | RegexOptions.CultureInvariant);
	}

	private static string Seek(string text, string pat, RegexOptions o = RegexOptions.None)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		Match match = Rx(pat, o).Match(text);
		if (!match.Success)
		{
			return "";
		}
		return match.Value;
	}

	private static List<string> SearchAll(string text, string pat, RegexOptions o = RegexOptions.None)
	{
		if (string.IsNullOrEmpty(text))
		{
			return new List<string>();
		}
		return (from Match m in Rx(pat, o).Matches(text)
			select m.Value).ToList();
	}

	private static string Between(string text, string left, string right)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		int num = text.IndexOf(left, StringComparison.Ordinal);
		if (num < 0)
		{
			return "";
		}
		num += left.Length;
		int num2 = text.IndexOf(right, num, StringComparison.Ordinal);
		if (num2 >= 0)
		{
			return text.Substring(num, num2 - num);
		}
		return text.Substring(num);
	}

	private static string BeforeFirst(string text, string sep)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		int num = text.IndexOf(sep, StringComparison.Ordinal);
		if (num >= 0)
		{
			return text.Substring(0, num);
		}
		return text;
	}

	private static string AfterLast(string text, string sep)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		int num = text.LastIndexOf(sep, StringComparison.Ordinal);
		if (num >= 0)
		{
			return text.Substring(num + sep.Length);
		}
		return "";
	}

	private static List<string> Dedup(IEnumerable<string> xs)
	{
		HashSet<string> hashSet = new HashSet<string>();
		return xs.Where(hashSet.Add).ToList();
	}

	public static string HeadTailLines(List<string> raw, int head, int tail)
	{
		List<string> list = (from ln in raw
			where !string.IsNullOrEmpty(ln) && Py.Strip(ln).Length > 0
			select Py.RStrip(ln, "\r")).ToList();
		if (list.Count <= head + tail)
		{
			return string.Join("\n", Dedup(list));
		}
		HashSet<string> hashSet = new HashSet<string>();
		List<string> list2 = new List<string>();
		List<string> list3 = new List<string>();
		foreach (string item2 in list)
		{
			if (hashSet.Add(item2))
			{
				list2.Add(item2);
				if (list2.Count >= head)
				{
					break;
				}
			}
		}
		for (int num = list.Count - 1; num >= 0; num--)
		{
			string item = list[num];
			if (hashSet.Add(item))
			{
				list3.Add(item);
				if (list3.Count >= tail)
				{
					break;
				}
			}
		}
		list3.Reverse();
		return string.Join("\n", list2.Concat(list3));
	}

	private static double Mtime(string p)
	{
		try
		{
			string path = Utils.AbsPath(p);
			if (!File.Exists(path) && !Directory.Exists(path))
			{
				return 0.0;
			}
			return (double)(File.GetLastWriteTimeUtc(path) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10000000.0;
		}
		catch (Exception)
		{
			return 0.0;
		}
	}

	private static bool _fresh(string path, double? started_at)
	{
		if (!File.Exists(Utils.AbsPath(path)) && !Directory.Exists(Utils.AbsPath(path)))
		{
			return false;
		}
		double num = Mtime(path);
		if (Math.Abs(Py.Time() - num) < 180.0)
		{
			return true;
		}
		if (started_at.HasValue && num >= started_at.Value - 15.0)
		{
			return true;
		}
		return false;
	}

	private static IEnumerable<string> SortedEntries(string dir)
	{
		try
		{
			List<string> list = Directory.EnumerateFileSystemEntries(Utils.AbsPath(dir)).Select(Path.GetFileName).ToList();
			list.Sort(string.CompareOrdinal);
			return list.Select((string n) => PPath.Join(dir, n));
		}
		catch (Exception)
		{
			return new List<string>();
		}
	}

	private static IEnumerable<string> RGlob(string dir)
	{
		foreach (string p in SortedEntries(dir))
		{
			yield return p;
			if (!Directory.Exists(Utils.AbsPath(p)))
			{
				continue;
			}
			foreach (string item in RGlob(p))
			{
				yield return item;
			}
		}
	}

	private static bool Glob(string name, string prefix, string suffix)
	{
		if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal))
		{
			return name.Length >= prefix.Length + suffix.Length;
		}
		return false;
	}

	public static List<string> CollectFiles(string instance_path, double? started_at = null, bool loose = false, IEnumerable<object> extra_roots = null)
	{
		List<string> found = new List<string>();
		List<string> list = new List<string> { PPath.Norm(instance_path) };
		foreach (object item2 in extra_roots ?? new List<object>())
		{
			if (Py.Truthy(item2) && new PPath(Py.Str(item2)).Resolve().ToString() != new PPath(instance_path).Resolve().ToString())
			{
				list.Add(PPath.Norm(Py.Str(item2)));
			}
		}
		foreach (string item3 in list)
		{
			string text = PPath.Join(item3, "crash-reports");
			if (new PPath(text).IsDir())
			{
				foreach (string item4 in RGlob(text))
				{
					string text2 = Py.Lower(new PPath(item4).Suffix);
					if ((text2 == ".txt" || text2 == ".log") && want(item4))
					{
						add(item4);
					}
				}
			}
			string text3 = PPath.Join(item3, "logs");
			string[] array = new string[2] { "latest.log", "debug.log" };
			foreach (string text4 in array)
			{
				string text5 = PPath.Join(text3, text4);
				if (want(text5))
				{
					add(text5);
				}
			}
			List<string> list2 = SortedEntries(item3).ToList();
			foreach (string item5 in list2)
			{
				if (Glob(new PPath(item5).Name, "hs_err_pid", ".log") && want(item5))
				{
					add(item5);
				}
			}
			foreach (string item6 in list2)
			{
				if (Glob(new PPath(item6).Name, "replay_pid", ".log") && want(item6))
				{
					add(item6);
				}
			}
			foreach (string item7 in list2)
			{
				if (Glob(new PPath(item7).Name, "", ".log") && want(item7))
				{
					add(item7);
				}
			}
		}
		HashSet<string> hashSet = new HashSet<string>();
		List<string> list3 = new List<string>();
		foreach (string item8 in found)
		{
			string item = (new PPath(item8).Exists() ? Java.RealPath(item8) : item8);
			if (hashSet.Add(item))
			{
				list3.Add(item8);
			}
		}
		return list3;
		void add(string text6)
		{
			try
			{
				FileInfo fileInfo = new FileInfo(Utils.AbsPath(text6));
				if (fileInfo.Exists && fileInfo.Length > 0)
				{
					found.Add(text6);
				}
			}
			catch (Exception)
			{
			}
		}
		bool want(string path)
		{
			if (!loose)
			{
				return _fresh(path, started_at);
			}
			return true;
		}
	}

	public static string Classify(string path)
	{
		string text = Py.Lower(new PPath(path).Name);
		if (text.StartsWith("hs_err"))
		{
			return "hs";
		}
		if (text.StartsWith("crash-"))
		{
			return "crash";
		}
		if (new string[6] { "latest.log", "latest log.txt", "debug.log", "debug log.txt", "rawoutput.log", "游戏崩溃前的输出.txt" }.Contains(text))
		{
			return "mc";
		}
		if (text.EndsWith(".log"))
		{
			return "extra_log";
		}
		if (text.EndsWith(".txt"))
		{
			return "extra_txt";
		}
		return "ignore";
	}

	public static List<string> AnalyzeModName(List<string> keywords, string log_crash, string log_debug)
	{
		List<string> list = new List<string>();
		foreach (string keyword in keywords)
		{
			string[] array = keyword.Split('(');
			for (int i = 0; i < array.Length; i++)
			{
				string text = Py.Strip(array[i], " )");
				if (text.Length > 0)
				{
					list.Add(text);
				}
			}
		}
		List<string> list2 = new List<string>();
		if (!string.IsNullOrEmpty(log_crash) && log_crash.Contains("A detailed walkthrough of the error"))
		{
			string text2 = log_crash.Replace("A detailed walkthrough of the error", "\u0001");
			bool flag = text2.Contains("Fabric Mods");
			if (flag)
			{
				text2 = text2.Replace("Fabric Mods", "\u0001");
			}
			text2 = AfterLast(text2, "\u0001");
			List<string> list3 = new List<string>();
			Regex regex = Rx("\\t\\tfabric[\\w-]*: Fabric");
			foreach (string item in Py.SplitLines(text2))
			{
				if (CountOf(Py.Lower(item), ".jar") == 1)
				{
					list3.Add(item);
				}
				else if (flag && item.StartsWith("\t\t") && !regex.IsMatch(item))
				{
					list3.Add(item);
				}
			}
			List<string> list4 = new List<string>();
			foreach (string item2 in list)
			{
				string value = Py.Lower(item2).Replace("_", "");
				foreach (string item3 in list3)
				{
					string text3 = Py.Lower(item3).Replace("_", "");
					if (text3.Contains(value) && !text3.Contains("minecraft.jar") && !text3.Contains(" forge-") && !text3.Contains(" mixin-"))
					{
						list4.Add(Py.Strip(item3));
						break;
					}
				}
			}
			foreach (string item4 in Dedup(list4))
			{
				string text4 = (flag ? Seek(item4, "(?<=: )[^\\n]+(?= [^\\n]+)") : Seek(item4, "(?<=\\()[^\\t]+\\.jar(?=\\))|(?<=(\\t\\t)|(\\| ))[^\\t|]+\\.jar", RegexOptions.IgnoreCase));
				if (text4.Length > 0)
				{
					list2.Add(text4);
				}
			}
		}
		if (!string.IsNullOrEmpty(log_debug))
		{
			List<string> list5 = SearchAll(log_debug, "(?<=valid mod file ).*", RegexOptions.Multiline);
			List<string> list6 = new List<string>();
			foreach (string item5 in list)
			{
				foreach (string item6 in list5)
				{
					if (item6.Contains("{" + item5 + "}"))
					{
						list6.Add(item6);
					}
				}
			}
			foreach (string item7 in Dedup(list6))
			{
				string text5 = Seek(item7, ".*(?= with)");
				if (text5.Length > 0)
				{
					list2.Add(text5);
				}
			}
		}
		return Dedup(list2.Where((string n) => n.Length > 0));
	}

	private static int CountOf(string s, string sub)
	{
		int num = 0;
		int startIndex = 0;
		while ((startIndex = s.IndexOf(sub, startIndex, StringComparison.Ordinal)) >= 0)
		{
			num++;
			startIndex += sub.Length;
		}
		return num;
	}

	public static List<string> TryModName(object keyword, string log_crash, string log_debug)
	{
		if (keyword is IEnumerable enumerable && !(keyword is string))
		{
			List<string> list = new List<string>();
			foreach (object item in enumerable)
			{
				if (Py.Truthy(item))
				{
					list.Add(Py.Str(item));
				}
			}
			if (list.Count == 0)
			{
				return new List<string>();
			}
			List<string> list2 = AnalyzeModName(list, log_crash, log_debug);
			if (list2.Count <= 0)
			{
				return list;
			}
			return list2;
		}
		string text = Py.Strip(Py.S(keyword));
		if (text.Length == 0)
		{
			return new List<string>();
		}
		List<string> list3 = AnalyzeModName(new List<string> { text }, log_crash, log_debug);
		if (list3.Count <= 0)
		{
			return new List<string> { text };
		}
		return list3;
	}

	private static KeyValuePair<string, bool> R(string t, bool h)
	{
		return new KeyValuePair<string, bool>(t, h);
	}

	private static KeyValuePair<string, bool> _fmt_reason(string code, List<string> e, string log_all, bool manual)
	{
		string one = ((e.Count > 0) ? e[0] : "");
		string many = string.Join("\n - ", e);
		if (code == "tiny_output")
		{
			return R("程序返回了以下信息：\n" + one, h: true);
		}
		if (code == "fabric_remap")
		{
			string text = ((one.Length > 0) ? ("\n写不进去的文件：" + one + "\n") : "\n");
			return R("Fabric / Quilt 首次启动要在版本目录下生成重映射后的游戏 jar，这一步写文件失败了。" + text + "\n常见原因：\n - 杀毒软件（火绒、360 等）拦截了 javaw 写入；\n - 游戏目录没有写入权限，或磁盘空间不足；\n - 系统卷信息异常：Java 查不到目录属于哪个盘，于是把所有目录都当成只读。\n   命令行执行 mountvol，如果列不出任何装入点，就是这种情况，重启电脑一般能恢复。\n\n可以依次尝试：把启动器和游戏目录加入杀毒软件白名单、删掉版本目录下的 .fabric 文件夹后重启电脑再启动、把游戏目录换到别的位置。", h: true);
		}
		if (_TABLE.TryGetValue(code, out KeyValuePair<string, bool> value))
		{
			return value;
		}
		switch (code)
		{
		case "java32":
			if (Environment.Is64BitOperatingSystem)
			{
				return R("你似乎正在使用 32 位 Java，Minecraft 无法使用所需内存。\n请在本实例的 Java 选项中改用 64 位 Java，然后再启动。", h: false);
			}
			return R("你正在使用 32 位操作系统，Minecraft 无法使用所需内存。\n需要重装 64 位系统，或换一台电脑。", h: true);
		case "mod_missing":
			if (e.Count > 0)
			{
				return R("由于未安装正确的前置 Mod，导致游戏退出。\n缺失的依赖项：\n - " + many + "\n\n请按上面的信息处理；看不懂英文可以用翻译。", h: false);
			}
			return R("由于未安装正确的前置 Mod，导致游戏退出。\n请根据错误报告中的日志处理。", h: true);
		case "stack_keyword":
			if (e.Count <= 1)
			{
				return R("游戏遇到了问题，分析到一个可疑关键词：" + ((one.Length > 0) ? one : "未知") + "。\n如果你知道它对应哪个 Mod，就先禁用它，也可以查看错误报告。", h: true);
			}
			return R("游戏遇到了问题，分析到这些可疑关键词：\n - " + string.Join(", ", e) + "\n\n知道某个词对应哪个 Mod，就先禁用它。", h: true);
		case "mod_suspect":
		case "stack_mod":
			if (e.Count <= 1)
			{
				return R("怀疑名为 " + ((one.Length > 0) ? one : "未知") + " 的 Mod 导致了游戏出错，但不能完全确定。\n可以先禁用它，看还会不会崩。" + View(), h: true);
			}
			return R("怀疑以下 Mod 导致了游戏出错，但不能完全确定：\n - " + many + "\n\n可以依次禁用，看还会不会崩。" + View(), h: true);
		case "mod_certain":
			return R(sureMod(""), h: true);
		case "mixin":
			if (e.Count == 0)
			{
				return R("部分 Mod 注入失败，导致游戏出错。\n一般是该 Mod 和其它 Mod 或当前环境不兼容，或它自己有 Bug。\n可以逐步禁用 Mod 来定位。" + View(), h: true);
			}
			if (e.Count == 1)
			{
				return R("名为 " + one + " 的 Mod 注入失败，导致游戏出错。\n一般是它和其它 Mod 或当前环境不兼容，或它自己有 Bug。\n可以先禁用它。" + View(), h: true);
			}
			return R("以下 Mod 导致了游戏出错：\n - " + many + "\n一般是它们和其它 Mod 或当前环境不兼容。" + View(), h: true);
		case "mod_config":
			if (e.Count >= 2)
			{
				return R("名为 " + e[0] + " 的 Mod 导致了游戏出错：\n其配置文件 " + e[1] + " 存在异常，无法读取。", h: false);
			}
			return R(sureMod(""), h: true);
		case "mod_init":
			if (e.Count <= 1)
			{
				return R("名为 " + ((one.Length > 0) ? one : "未知") + " 的 Mod 初始化失败，游戏无法继续加载。\n可以先禁用它。" + View(), h: true);
			}
			return R("以下 Mod 初始化失败：\n - " + many + "\n可以依次禁用。" + View(), h: true);
		case "bad_block":
			if (one.Length > 0)
			{
				return R("游戏似乎因为方块 " + one + " 出了问题。\n\n可以新建一个世界看还能不能玩：\n - 若正常，多半是这个方块；需要想办法删掉它。\n - 若仍然出错，问题可能来自别处。", h: true);
			}
			return R("游戏似乎因为世界中的某些方块出了问题。\n可以新建世界试一次。", h: true);
		case "mod_dup":
			if (e.Count >= 2)
			{
				return R("你重复安装了多个相同的 Mod：\n - " + many + "\n\n每个 Mod 只能出现一次，删掉重复的再启动。", h: false);
			}
			return R("你可能重复安装了多个相同的 Mod。\n每个 Mod 只能出现一次，请删掉重复的再启动。", h: true);
		case "bad_entity":
			if (one.Contains("minecraft:player"))
			{
				if (one.Contains(" "))
				{
					return R("游戏因为位于 " + Py.Split(one, " ", 1).Last() + " 的玩家实体导致了崩溃。", h: true);
				}
				return R("游戏因为玩家实体导致了崩溃。", h: true);
			}
			if (one.Length > 0)
			{
				return R("游戏因为实体 " + one + " 导致了崩溃。", h: true);
			}
			return R("游戏因为世界中的某个实体导致了崩溃。", h: true);
		default:
			if (new string[5] { "pixel_format", "intel_av", "amd_av", "nvidia_av", "no_opengl" }.Contains<string>(code))
			{
				if (Py.Lower(log_all ?? "").Contains("hd graphics "))
				{
					return R("显卡驱动有问题，或没有用独立显卡，游戏无法正常运行。\n\n如果电脑有独显，请用独显运行启动器和 Minecraft。\n仍不行就升级或回退出厂显卡驱动，或换用 8.0.51 及更低的 Java。", h: true);
				}
				return R("显卡驱动存在问题，导致游戏无法正常运行。\n\n请升级显卡驱动到最新，或回退出厂版本后再启动。\n还不行可以试试 8.0.51 及更低的 Java。", h: true);
			}
			switch (code)
			{
			case "fabric_solution":
				if (one.Length > 0)
				{
					return R("Fabric 提供了以下解决方案：\n" + string.Join("\n", e) + "\n\n请按上面的信息处理；看不懂英文可以用翻译。", h: false);
				}
				return R("Fabric 可能已经给出解决方案，请根据错误报告中的日志处理。", h: true);
			case "forge_error":
				if (one.Length > 0)
				{
					return R("Forge 提供了以下错误信息：\n" + one + "\n\n请按上面的信息处理；看不懂英文可以用翻译。", h: false);
				}
				return R("Forge 可能已经给出错误信息，请根据错误报告中的日志处理。", h: true);
			case "mod_incompat":
				if (one.Length > 0)
				{
					return R("你安装的 Mod 不兼容：\n" + one + "\n\n请按上面的信息处理；看不懂英文可以用翻译。", h: false);
				}
				return R("你安装的 Mod 不兼容。请根据错误报告中的日志处理。", h: true);
			case "loader_error":
				if (one.Length > 0)
				{
					return R("Mod 加载器提供了以下错误信息：\n" + one + "\n\n请按上面的信息处理；看不懂英文可以用翻译。", h: false);
				}
				return R("Mod 加载器可能已经给出错误信息，请根据错误报告中的日志处理。", h: true);
			default:
				return R("分析到错误原因（" + code + "），但没有更详细的说明。请把错误报告发给能帮你的人。", h: true);
			}
		}
		string View()
		{
			if (!manual)
			{
				return "\n你可以查看错误报告了解错误具体是如何发生的。";
			}
			return "";
		}
		string sureMod(string lead)
		{
			if (e.Count <= 1)
			{
				return lead + "名为 " + ((one.Length > 0) ? one : "未知") + " 的 Mod 导致了游戏出错。\n你可以尝试禁用此 Mod，然后观察游戏是否还会崩溃。" + View();
			}
			return lead + "以下 Mod 导致了游戏出错：\n - " + many + "\n\n你可以尝试依次禁用上述 Mod，然后观察游戏是否还会崩溃。" + View();
		}
	}

	private static List<string> ExtraOf(object v)
	{
		return ((List<object>)v).Select(Py.Str).ToList();
	}

	private static KeyValuePair<string, bool> FormatResult(Analyzer an, bool manual, object exit_code)
	{
		string text = ExitHint(exit_code);
		if (an.reasons.Count == 0)
		{
			string text2 = (manual ? "很抱歉，无法确定错误原因。" : "很抱歉，你的游戏出现了一些问题……\n如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。");
			if (text.Length > 0)
			{
				text2 = text + "\n\n" + text2;
			}
			return R(text2, h: true);
		}
		List<string> list = new List<string>();
		bool flag = false;
		foreach (KeyValuePair<string, object> reason in an.reasons)
		{
			KeyValuePair<string, bool> keyValuePair = _fmt_reason(reason.Key, ExtraOf(reason.Value), an.log_all, manual);
			list.Add(keyValuePair.Key);
			flag = flag || keyValuePair.Value;
		}
		string text3 = string.Join("\n\n此外，", list);
		long? num = NormalizeExit(exit_code);
		if (text.Length > 0 && num.HasValue && num.Value != 0L)
		{
			text3 = text + "\n\n" + text3;
		}
		if (flag && !manual)
		{
			text3 = Py.RStrip(text3) + "\n如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。";
		}
		return R(Py.Strip(text3), flag);
	}

	private static bool LooksLikeCrash(object exit_code, Analyzer an, bool cancelled)
	{
		if (cancelled)
		{
			return false;
		}
		long? num = NormalizeExit(exit_code);
		if (num.HasValue && num.Value != 0L)
		{
			return true;
		}
		string input = an.log_all + "\n" + an.raw_output;
		if (Rx("Game crashed!|---- Minecraft Crash Report ----").IsMatch(input))
		{
			return true;
		}
		if (an.log_hs.Length > 0)
		{
			return true;
		}
		return false;
	}

	private static string _title_of(IEnumerable<string> codes)
	{
		string text = codes.FirstOrDefault();
		if (text == null)
		{
			return "Minecraft 出现错误";
		}
		if (!_TITLES.ContainsKey(text))
		{
			return "Minecraft 出现错误";
		}
		return (string)_TITLES[text];
	}

	private static string Tail(string s, int n)
	{
		if (s.Length <= n)
		{
			return s;
		}
		return s.Substring(s.Length - n);
	}

	private static PyDict _run(Instance instance, IEnumerable<string> output_lines, object exit_code, double? started_at, bool cancelled, bool manual, string version, IEnumerable<object> extra_roots)
	{
		return _run(instance.path, instance.name, output_lines, exit_code, started_at, cancelled, manual, version, extra_roots);
	}

	public static PyDict AnalyzeInstancePath(string path, string extra_log = "", bool manual = true)
	{
		List<string> output_lines = ((!string.IsNullOrEmpty(extra_log)) ? Py.SplitLines(extra_log) : null);
		return _run(PPath.Norm(path), new PPath(path).Name, output_lines, null, null, cancelled: false, manual, "", null);
	}

	private static PyDict _run(string path, string instName, IEnumerable<string> output_lines, object exit_code, double? started_at, bool cancelled, bool manual, string version, IEnumerable<object> extra_roots)
	{
		Analyzer analyzer = new Analyzer();
		List<string> fileList = CollectFiles(path, started_at, manual, extra_roots);
		analyzer.Prepare(fileList, (output_lines ?? new List<string>()).ToList());
		analyzer.Analyze();
		bool flag = manual || LooksLikeCrash(exit_code, analyzer, cancelled);
		string key = FormatResult(analyzer, manual, exit_code).Key;
		string text = Py.Slice(Py.Split(key, "\n", 1)[0], null, 220);
		List<object> list = ((IEnumerable<KeyValuePair<string, object>>)analyzer.reasons).Select((Func<KeyValuePair<string, object>, object>)((KeyValuePair<string, object> kv) => PyDict.Of("code", kv.Key, "title", _title_of(new string[1] { kv.Key }), "extra", kv.Value))).ToList();
		long? num = NormalizeExit(exit_code);
		PyDict pyDict = PyDict.Of("is_crash", flag, "title", flag ? "Minecraft 出现错误" : "游戏已退出", "headline", _title_of(analyzer.reasons.Keys), "summary", text, "detail", key, "help", "如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。", "reasons", list, "exit_code", num.HasValue ? ((object)num.Value) : null, "exit_hint", ExitHint(exit_code), "direct_file", analyzer.direct_file, "files", Dedup(analyzer.files).Cast<object>().ToList(), "instance", instName, "version", version ?? "", "output_tail", Tail(analyzer.raw_output ?? "", 8000), "log_mc", Tail(analyzer.log_mc ?? "", 4000), "log_crash", Tail(analyzer.log_crash ?? "", 4000), "log_hs", Tail(analyzer.log_hs ?? "", 2000), "has_latest", analyzer.log_mc.Length > 0, "has_crash", analyzer.log_crash.Length > 0, "has_hs_err", analyzer.log_hs.Length > 0);
		pyDict["actions"] = BuildActions(pyDict, path);
		return pyDict;
	}

	private static List<string> _match_mod_files(string mods_dir, List<string> names)
	{
		if (mods_dir == null || !new PPath(mods_dir).IsDir() || names == null || names.Count == 0)
		{
			return new List<string>();
		}
		List<PPath> list = (from p in SortedEntries(mods_dir)
			where File.Exists(Utils.AbsPath(p)) && (Py.Lower(new PPath(p).Suffix) == ".jar" || Py.Lower(new PPath(p).Name).EndsWith(".jar.disabled"))
			select new PPath(p)).ToList();
		List<string> list2 = new List<string>();
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		foreach (PPath item in list)
		{
			dictionary[Py.Lower(item.Name)] = item.Name;
		}
		Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
		foreach (PPath item2 in list)
		{
			dictionary2[Py.Lower(Py.Lower(item2.Name).EndsWith(".disabled") ? item2.Name.Substring(0, item2.Name.Length - 9) : item2.Stem)] = item2.Name;
		}
		foreach (string name in names)
		{
			string text = Py.Strip(name ?? "");
			if (text.Length == 0)
			{
				continue;
			}
			string text2 = Py.Lower(text);
			if (dictionary.TryGetValue(text2, out var value))
			{
				list2.Add(value);
				continue;
			}
			string text3 = Py.Lower(new PPath(text).Stem);
			if (dictionary2.TryGetValue(text3, out var value2))
			{
				list2.Add(value2);
				continue;
			}
			foreach (PPath item3 in list)
			{
				string text4 = Py.Lower(item3.Name);
				if (text4.Contains(text2) || text4.Contains(text3) || text4.StartsWith(text3))
				{
					list2.Add(item3.Name);
					break;
				}
			}
		}
		return Dedup(list2);
	}

	public static List<object> BuildActions(PyDict report, string instance_path = null)
	{
		report = report ?? new PyDict();
		List<object> list = Py.ListOr(Py.Or(report.Get("reasons"), new List<object>()));
		string text = ((!string.IsNullOrEmpty(instance_path)) ? PPath.Norm(instance_path) : null);
		if (text == null && Py.Truthy(report.Get("instance")))
		{
			try
			{
				text = new Instance(Py.Str(report["instance"])).path;
			}
			catch (Exception)
			{
				text = null;
			}
		}
		string text2 = ((text != null) ? PPath.Join(text, "mods") : null);
		List<object> actions = new List<object>();
		HashSet<string> seen = new HashSet<string>();
		string[] source = new string[12]
		{
			"mod_suspect", "stack_mod", "mod_name_chars", "shaders_optifine", "mod_certain", "mixin", "mod_config", "mod_init", "mod_incompat", "optifine_forge",
			"mixin_bootstrap", "nightconfig"
		};
		string[] source2 = new string[8] { "openj9", "jdk", "java_too_new", "java_mismatch", "java32", "need_java11", "old_forge_new_java", "java_too_old" };
		string[] source3 = new string[9] { "forge_incomplete", "verify_fail", "multi_forge_json", "libs_missing", "assets_index_missing", "assets_missing", "natives_missing", "loader_error", "forge_error" };
		string[] source4 = new string[7] { "mod_unzipped", "mod_duplicate", "mod_breaks", "mod_dup", "mod_missing", "mod_id_limit", "hd_pack" };
		foreach (object item2 in list)
		{
			PyDict pyDict = Py.DictOr(item2);
			string text3 = Py.Strip(Py.Str(Py.Or(pyDict.Get("code"), "")));
			List<string> list2 = Py.ListOr(Py.Or(pyDict.Get("extra"), new List<object>())).Select(Py.Str).ToList();
			if (source.Contains(text3))
			{
				List<string> list3 = ((text2 != null) ? _match_mod_files(text2, list2) : new List<string>());
				if (list3.Count == 0 && list2.Count > 0)
				{
					list3 = list2.Where((string x) => x.Length > 0).ToList();
				}
				if (list3.Count > 0)
				{
					add(PyDict.Of("id", "disable_mods", "label", "禁用嫌疑 Mod", "mods", list3.Cast<object>().ToList(), "codes", Py.L(text3)));
				}
				else if (text2 != null && new PPath(text2).IsDir())
				{
					add(PyDict.Of("id", "open_mods_folder", "label", "打开 Mods 文件夹排查", "codes", Py.L(text3)));
				}
			}
			else if (text3 == "oom")
			{
				long num = Py.Int(Py.Or(Config.CONFIG.Get("memory_mb"), 4096L));
				add(PyDict.Of("id", "bump_memory", "label", "提高默认内存", "memory_mb", Math.Min(16384L, num + 2048), "codes", Py.L(text3)));
			}
			else if (source2.Contains(text3))
			{
				int num2;
				switch (text3)
				{
				default:
					num2 = 17;
					break;
				case "jdk":
				case "java_too_old":
				case "old_forge_new_java":
					num2 = 8;
					break;
				case "need_java11":
					num2 = 11;
					break;
				}
				long num3 = num2;
				add(PyDict.Of("id", "need_java", "label", "下载 Java " + num3, "major", num3, "codes", Py.L(text3)));
			}
			else if (source3.Contains(text3))
			{
				if (Py.Truthy(report.Get("version")))
				{
					add(PyDict.Of("id", "repair_version", "label", "修复该版本文件", "version", Py.Or(report.Get("version"), ""), "instance", Py.Or(report.Get("instance"), ""), "codes", Py.L(text3)));
				}
			}
			else if (source4.Contains(text3))
			{
				add(PyDict.Of("id", "open_mods_folder", "label", "打开 Mods 文件夹", "codes", Py.L(text3)));
				if (text3 == "mod_dup" && list2.Count > 0)
				{
					List<string> list4 = ((text2 != null) ? _match_mod_files(text2, list2) : list2.Where((string x) => x.Length > 0).ToList());
					if (list4.Count > 0)
					{
						object[] array = new object[8] { "id", "disable_mods", "label", "禁用重复 Mod", "mods", null, null, null };
						IEnumerable<string> source5 = ((list4.Count > 1) ? list4.Skip(1) : list4);
						array[5] = source5.Cast<object>().ToList();
						array[6] = "codes";
						array[7] = Py.L(text3);
						add(PyDict.Of(array));
					}
				}
			}
			else if (new string[6] { "nvidia_av", "intel_av", "amd_av", "pixel_format", "no_opengl", "opengl_1282" }.Contains(text3))
			{
				add(PyDict.Of("id", "open_gpu_hint", "label", "查看显卡驱动提示", "codes", Py.L(text3)));
			}
			else if (text3 == "jvm_args")
			{
				add(PyDict.Of("id", "reset_jvm_args", "label", "清空自定义 JVM 参数", "codes", Py.L(text3)));
			}
		}
		string text4 = Py.Strip(Py.Str(Py.Or(report.Get("direct_file"), "")));
		if (text4.Length > 0 && new PPath(text4).IsFile())
		{
			add(PyDict.Of("id", "open_crash_file", "label", "打开崩溃报告", "path", text4, "codes", Py.L("_file")));
		}
		if (Py.Truthy(report.Get("has_hs_err")) && text != null)
		{
			List<string> list5 = (from p in SortedEntries(text)
				where Glob(new PPath(p).Name, "hs_err_pid", ".log")
				select p).Take(1).ToList();
			if (list5.Count > 0)
			{
				add(PyDict.Of("id", "open_crash_file", "label", "打开 hs_err 日志", "path", list5[0], "codes", Py.L("_hs")));
			}
		}
		return actions;
		void add(PyDict action)
		{
			string text5 = Py.Str(Py.Or(action.Get("id"), ""));
			string item = text5 + "|" + string.Join(",", Py.ListOr(Py.Or(action.Get("mods"), new List<object>())).Select(Py.Str)) + "|" + Py.Str(Py.Or(action.Get("major"), "")) + "|" + Py.Str(Py.Or(action.Get("path"), ""));
			if (seen.Add(item))
			{
				actions.Add(action);
			}
		}
	}

	public static PyDict AnalyzeLaunch(Instance instance, object exit_code, IEnumerable<string> output_lines = null, double? started_at = null, bool cancelled = false, string version = "", IEnumerable<object> extra_roots = null)
	{
		return _run(instance, output_lines, exit_code, started_at, cancelled, manual: false, version, extra_roots);
	}

	public static PyDict AnalyzeInstance(Instance instance, string extra_log = "", bool manual = true)
	{
		List<string> output_lines = ((!string.IsNullOrEmpty(extra_log)) ? Py.SplitLines(extra_log) : null);
		return _run(instance, output_lines, null, null, cancelled: false, manual, "", null);
	}

	private static string WriteText(string s)
	{
		if (!Utils.IS_WINDOWS)
		{
			return s;
		}
		return s.Replace("\n", "\r\n");
	}

	public static string ExportReport(PyDict report, string dest = null)
	{
		report = report ?? new PyDict();
		string text = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
		string text2 = ((!string.IsNullOrEmpty(dest)) ? PPath.Norm(dest) : PPath.Join(Utils.ROOT, "错误报告-" + text + ".zip"));
		Directory.CreateDirectory(Utils.AbsPath(new PPath(text2).Parent.ToString()));
		string text3 = new PPath(text2).WithSuffix(".tmpdir").ToString();
		string text4 = Utils.AbsPath(text3);
		if (Directory.Exists(text4))
		{
			try
			{
				Directory.Delete(text4, recursive: true);
			}
			catch (Exception)
			{
			}
		}
		Directory.CreateDirectory(text4);
		UTF8Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		string text5 = "实例: " + Py.Str(report.Get("instance")) + "\n版本: " + Py.Str(report.Get("version")) + "\n退出码: " + Py.Str(report.Get("exit_code")) + " " + Py.Str(Py.Or(report.Get("exit_hint"), "")) + "\n\n" + Py.Str(Py.Or(report.Get("detail"), "")) + "\n";
		File.WriteAllText(Path.Combine(text4, "分析结论.txt"), WriteText(FilterSecrets(text5)), encoding);
		string text6 = Py.Str(Py.Or(report.Get("output_tail"), ""));
		if (text6.Length > 0)
		{
			File.WriteAllText(Path.Combine(text4, "游戏崩溃前的输出.txt"), WriteText(FilterSecrets(text6)), encoding);
		}
		HashSet<string> hashSet = new HashSet<string>();
		foreach (object item in Py.ListOr(Py.Or(report.Get("files"), new List<object>())))
		{
			PPath pPath = new PPath(Py.Str(item));
			if (pPath.IsFile())
			{
				string text7 = pPath.Name;
				if (Py.Lower(text7) == "latest.log")
				{
					text7 = "latest.log";
				}
				string text8 = Path.Combine(text4, text7);
				int num = 1;
				while (hashSet.Contains(text8) || File.Exists(text8) || Directory.Exists(text8))
				{
					text8 = Path.Combine(text4, pPath.Stem + "_" + num + pPath.Suffix);
					num++;
				}
				try
				{
					File.WriteAllText(text8, WriteText(FilterSecrets(ReadText(pPath.ToString(), 0L))), encoding);
					hashSet.Add(text8);
				}
				catch (IOException)
				{
				}
			}
		}
		string path = Utils.AbsPath(text2);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		using (FileStream stream = new FileStream(path, FileMode.CreateNew))
		{
			using ZipArchive zipArchive = new ZipArchive(stream, ZipArchiveMode.Create);
			foreach (string item2 in RGlob(text3))
			{
				if (!File.Exists(Utils.AbsPath(item2)))
				{
					continue;
				}
				string entryName = Utils.AbsPath(item2).Substring(text4.Length).TrimStart(Path.DirectorySeparatorChar)
					.Replace('\\', '/');
				using Stream destination = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
				using FileStream fileStream = File.OpenRead(Utils.AbsPath(item2));
				fileStream.CopyTo(destination);
			}
		}
		try
		{
			Directory.Delete(text4, recursive: true);
		}
		catch (Exception)
		{
		}
		return text2;
	}

	public static void ShellOpen(string path)
	{
		Action<string> shellOpenHook = ShellOpenHook;
		if (shellOpenHook != null)
		{
			shellOpenHook(path);
			return;
		}
		try
		{
			ProcessStartInfo processStartInfo;
			if (Utils.IS_WINDOWS)
			{
				processStartInfo = new ProcessStartInfo(PPath.Abs(path))
				{
					UseShellExecute = true
				};
			}
			else
			{
				processStartInfo = new ProcessStartInfo(Utils.IS_MAC ? "open" : "xdg-open")
				{
					UseShellExecute = false
				};
				processStartInfo.Arguments = Utils.List2CmdLine(new string[1] { path });
				processStartInfo.WorkingDirectory = PPath.Abs(Utils.ROOT);
			}
			using (Process.Start(processStartInfo))
			{
			}
		}
		catch (Win32Exception ex)
		{
			throw new OSError(ex.Message, ex);
		}
		catch (InvalidOperationException ex2)
		{
			throw new OSError(ex2.Message, ex2);
		}
	}

	public static bool OpenPath(object path)
	{
		PPath pPath = new PPath(Py.Str(path));
		try
		{
			if (pPath.IsFile() || pPath.IsDir())
			{
				ShellOpen(pPath.ToString());
				return true;
			}
		}
		catch (OSError)
		{
			return false;
		}
		return false;
	}
}
