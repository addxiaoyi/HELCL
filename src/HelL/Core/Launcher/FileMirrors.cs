using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class FileMirrors
{
    public const string Main = "https://mc-mirror.com";
    public const string West = "https://us-west-01.mc-mirror.com";
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, string> Registered = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly Regex SafeName = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,100}\z", RegexOptions.CultureInvariant);

    public static void RegisterVersion(PyDict version)
    {
        string id = Py.S(version.Get("id"));
        if (!SafeName.IsMatch(id) || id.Contains("..")) return;
        if (version.Get("downloads") is PyDict downloads)
            foreach (string kind in new[] { "client", "server" })
                Register(downloads.Get(kind) as PyDict, "/versions/" + id + "/" + kind + ".jar");
        if (version.Get("assetIndex") is PyDict index)
        {
            string name = Py.S(index.Get("id"));
            if (SafeName.IsMatch(name) && !name.Contains("..")) Register(index, "/assets/indexes/" + name + ".json");
        }
        if ((version.Get("logging") as PyDict)?.Get("client") is PyDict logging && logging.Get("file") is PyDict file)
        {
            string name = Py.S(file.Get("id"));
            if (SafeName.IsMatch(name) && !name.Contains("..")) Register(file, "/logging/" + name);
        }
    }

    private static void Register(PyDict entry, string path)
    {
        if (entry == null || !Regex.IsMatch(Py.S(entry.Get("sha1")), @"\A[a-fA-F0-9]{40}\z")) return;
        string url = Py.S(entry.Get("url"));
        if (!TryOfficial(url, out var uri) || !(uri.Host is "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com")) return;
        lock (Gate)
        {
            if (Registered.Count >= 4096 && !Registered.ContainsKey(url)) Registered.Clear();
            Registered[url] = path;
        }
    }

    public static string RelativePath(string url)
    {
        if (!TryOfficial(url, out var uri)) return null;
        lock (Gate) { if (Registered.TryGetValue(url, out string registered)) return registered; }
        string path = uri.AbsolutePath;
        return uri.Host switch
        {
            "libraries.minecraft.net" => "/libraries" + path,
            "resources.download.minecraft.net" when Regex.IsMatch(path, @"\A/[a-f0-9]{2}/[a-f0-9]{40}\z") => "/assets/objects" + path,
            "maven.fabricmc.net" => "/fabric/maven" + path,
            "maven.quiltmc.org" when path.StartsWith("/repository/release/", StringComparison.Ordinal) => "/quilt/maven/" + path.Substring("/repository/release/".Length),
            _ => null
        };
    }

    public static List<string> Candidates(string upstream)
    {
        string path = RelativePath(upstream);
        return path == null ? new List<string>() : new List<string> { Main + path, West + path };
    }

    public static string Provider(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return "";
        return uri.Host switch
        {
            "mc-mirror.com" => "mc_mirror", "us-west-01.mc-mirror.com" => "mc_mirror_west", "bmclapi2.bangbang93.com" => "bmclapi",
            "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" or "launcher.mojang.com" or "resources.download.minecraft.net" or "libraries.minecraft.net" or "maven.fabricmc.net" or "maven.quiltmc.org" or "meta.fabricmc.net" or "meta.quiltmc.org" or "maven.minecraftforge.net" or "files.minecraftforge.net" or "maven.neoforged.net" => "official",
            _ => ""
        };
    }

    public static string Group(string upstream)
    {
        if (!Uri.TryCreate(upstream, UriKind.Absolute, out var uri)) return "";
        if (uri.Host is "mc-mirror.com" or "us-west-01.mc-mirror.com")
            return uri.AbsolutePath.StartsWith("/assets/objects/", StringComparison.Ordinal) ? "assets" : uri.AbsolutePath.StartsWith("/libraries/", StringComparison.Ordinal) ? "libraries"
                : uri.AbsolutePath.StartsWith("/fabric/", StringComparison.Ordinal) ? "fabric" : uri.AbsolutePath.StartsWith("/quilt/", StringComparison.Ordinal) ? "quilt" : "game";
        if (uri.Host == "bmclapi2.bangbang93.com")
            return uri.AbsolutePath.StartsWith("/assets/", StringComparison.Ordinal) ? "assets" : uri.AbsolutePath.StartsWith("/maven/", StringComparison.Ordinal) ? "libraries" : "game";
        return uri.Host switch
        {
            "resources.download.minecraft.net" => "assets",
            "libraries.minecraft.net" => "libraries",
            "maven.fabricmc.net" or "meta.fabricmc.net" => "fabric",
            "maven.quiltmc.org" or "meta.quiltmc.org" => "quilt",
            "maven.minecraftforge.net" or "files.minecraftforge.net" or "maven.neoforged.net" => "loaders",
            _ => "game"
        };
    }

    public static string Label(string provider) => provider switch
    {
        "official" => "官方", "bmclapi" => "BMCLAPI", "mc_mirror" => "MC Mirror", "mc_mirror_west" => "MC Mirror · 美西", _ => provider
    };

    private static bool TryOfficial(string url, out Uri uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        string decoded = Uri.UnescapeDataString(uri.AbsolutePath);
        return !decoded.Contains('\\') && !decoded.Split('/').Any(segment => segment == ".." || segment == ".");
    }
}
