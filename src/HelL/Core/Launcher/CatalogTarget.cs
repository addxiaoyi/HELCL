using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public sealed class CatalogTarget
{
    public string Version { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public string Error { get; set; } = "";

    public static List<CatalogTarget> Read(Instance game) => game.InstalledIds().Select(Py.S).Select(id => Inspect(game, id)).ToList();

    public static CatalogTarget Inspect(Instance game, string id)
    {
        var target = new CatalogTarget { Version = id };
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = Utils.SafeVersionId(id);
        try
        {
            while (current.Length > 0)
            {
                if (!visited.Add(current) || visited.Count > 16) throw new InvalidDataException("版本继承关系循环或过深。");
                string path = Path.Combine(game.VersionsDir(), Utils.SafeVersionId(current), current + ".json");
                if (!File.Exists(path))
                {
                    if (current != id && IsMinecraft(current)) { target.GameVersion = current; break; }
                    throw new InvalidDataException("版本配置不存在。");
                }
                if (Utils.ReadJson(path) is not PyDict json) throw new InvalidDataException("版本配置已损坏。");
                string libraries = string.Join(" ", Py.ListOr(json.Get("libraries")).OfType<PyDict>().Select(library => Py.S(library.Get("name"))));
                string signature = libraries + " " + Py.S(json.Get("mainClass"));
                string detected = DetectLoader(signature);
                if (target.Loader.Length == 0) target.Loader = detected;
                string explicitVersion = Py.S(json.Get("minecraftVersion"));
                if (IsMinecraft(explicitVersion)) target.GameVersion = explicitVersion;
                string parent = Py.S(json.Get("inheritsFrom"));
                if (parent.Length == 0)
                {
                    string vanilla = Py.S(json.Get("id"));
                    if (target.GameVersion.Length == 0 && IsMinecraft(vanilla)) target.GameVersion = vanilla;
                    if (target.GameVersion.Length == 0 && IsMinecraft(current)) target.GameVersion = current;
                    break;
                }
                current = parent;
            }
            if (target.Loader.Length == 0) target.Loader = "vanilla";
            if (target.GameVersion.Length == 0) target.Error = "未识别 Minecraft 版本，请手动筛选。";
        }
        catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException || error is ValueError)
        {
            target.GameVersion = target.Loader = ""; target.Error = "无法读取版本信息：" + error.Message;
        }
        return target;
    }

    public static CatalogTarget Common(IEnumerable<CatalogTarget> targets)
    {
        var rows = targets.ToArray();
        if (rows.Length == 0) return new CatalogTarget { Error = "此实例尚未安装版本，可手动筛选。" };
        if (rows.Any(row => row.Error.Length > 0)) return new CatalogTarget { Error = "部分版本信息不可用，请选择具体版本或手动筛选。" };
        var versions = rows.Select(row => row.GameVersion).Distinct().ToArray();
        var loaders = rows.Select(row => row.Loader).Distinct().ToArray();
        return new CatalogTarget { GameVersion = versions.Length == 1 ? versions[0] : "", Loader = loaders.Length == 1 ? loaders[0] : "",
            Error = versions.Length != 1 || loaders.Length != 1 ? "实例含不同版本或加载器，请选择具体版本以精确匹配。" : "" };
    }

    private static bool IsMinecraft(string id) => Regex.IsMatch(id, @"^(?:1\.\d+(?:\.\d+)?|2\d\.\d+(?:\.\d+)?|\d{2}w\d{2}[a-z])(?:[- ](?:pre|rc|snapshot)[- ]?\d+)?$", RegexOptions.IgnoreCase);
    private static string DetectLoader(string signature)
    {
        signature = signature.ToLowerInvariant();
        if (signature.Contains("net.neoforged:") || signature.Contains("net.neoforged.")) return "neoforge";
        if (signature.Contains("org.quiltmc:") || signature.Contains("org.quiltmc.")) return "quilt";
        if (signature.Contains("net.fabricmc:") || signature.Contains("net.fabricmc.")) return "fabric";
        if (signature.Contains("net.minecraftforge:") || signature.Contains("net.minecraftforge.")) return "forge";
        return "";
    }
}
