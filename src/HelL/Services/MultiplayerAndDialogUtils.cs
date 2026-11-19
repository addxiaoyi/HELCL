using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HelL.Core.Launcher.Sync;

namespace HelL.Services;

public enum ModVersionRelation
{
    Older,
    Same,
    Newer,
    Unknown
}

public enum SyncDirection
{
    Identical,
    Push,
    Pull,
    Conflict
}

public class LauncherCandidate
{
    public string Name { get; set; }
    public string Path { get; set; }
    public string Description { get; set; }

    public LauncherCandidate(string name, string path, string desc)
    {
        Name = name;
        Path = path;
        Description = desc;
    }
}

public static class MultiplayerAndDialogUtils
{
    public static List<string> FormatLanAddresses(IEnumerable<string> ips, int port)
    {
        var result = new List<string>();
        if (ips == null) return result;

        var list = ips.Where(ip => !string.IsNullOrWhiteSpace(ip)).ToList();
        // filter 169.254 if other valid ips exist
        var valid = list.Where(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal)).ToList();
        if (valid.Count > 0)
        {
            list = valid;
        }

        foreach (var ip in list)
        {
            string trimmed = ip.Trim();
            if (trimmed.Contains(':'))
            {
                // IPv6
                result.Add($"[{trimmed}]:{port}");
            }
            else
            {
                result.Add($"{trimmed}:{port}");
            }
        }
        return result;
    }

    public static string NormalizeRoomCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string trimmed = input.Trim();

        // Check if there is a URL or prefix containing U/XXXX...
        var match = Regex.Match(trimmed, @"(U/[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Value.ToUpperInvariant();
        }

        // Match XXXX-XXXX-XXXX-XXXX without U/
        var matchNoPrefix = Regex.Match(trimmed, @"([A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4})", RegexOptions.IgnoreCase);
        if (matchNoPrefix.Success)
        {
            return "U/" + matchNoPrefix.Value.ToUpperInvariant();
        }

        return trimmed;
    }

    public static ModVersionRelation CompareModVersions(string currentFile, string newFile)
    {
        if (string.Equals(currentFile, newFile, StringComparison.OrdinalIgnoreCase))
        {
            return ModVersionRelation.Same;
        }

        var curTokens = ExtractVersionNumbers(currentFile);
        var newTokens = ExtractVersionNumbers(newFile);

        if (curTokens.Count == 0 || newTokens.Count == 0)
        {
            return ModVersionRelation.Unknown;
        }

        int minLen = Math.Min(curTokens.Count, newTokens.Count);
        for (int i = 0; i < minLen; i++)
        {
            if (newTokens[i] > curTokens[i])
            {
                return ModVersionRelation.Newer;
            }
            if (newTokens[i] < curTokens[i])
            {
                return ModVersionRelation.Older;
            }
        }

        if (newTokens.Count > curTokens.Count) return ModVersionRelation.Newer;
        if (newTokens.Count < curTokens.Count) return ModVersionRelation.Older;
        return ModVersionRelation.Same;
    }

    public static string GetModPrefix(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "";
        string name = Path.GetFileNameWithoutExtension(fileName);
        int idx = -1;
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
            {
                idx = i;
                break;
            }
        }
        if (idx > 0)
        {
            return name.Substring(0, idx).TrimEnd('-', '_', '.');
        }
        return name;
    }

    public static (int Newer, int Same, int Older) BatchEvaluateImport(IEnumerable<string> existingFiles, IEnumerable<string> newFiles)
    {
        int newer = 0, same = 0, older = 0;
        if (existingFiles == null || newFiles == null) return (0, 0, 0);

        var existingList = existingFiles.ToList();
        foreach (var file in newFiles)
        {
            string fn = Path.GetFileName(file);
            string prefix = GetModPrefix(fn);

            var match = existingList.FirstOrDefault(e => 
                string.Equals(e, fn, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(prefix) && string.Equals(GetModPrefix(e), prefix, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                var rel = CompareModVersions(match, fn);
                if (rel == ModVersionRelation.Newer) newer++;
                else if (rel == ModVersionRelation.Same) same++;
                else if (rel == ModVersionRelation.Older) older++;
            }
        }
        return (newer, same, older);
    }

    private static List<int> ExtractVersionNumbers(string fileName)
    {
        var numbers = new List<int>();
        var matches = Regex.Matches(fileName, @"\d+");
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Value, out int v))
            {
                numbers.Add(v);
            }
        }
        return numbers;
    }

    public static SyncDirection EvaluateSyncAction(long localTime, long localSize, long remoteTime, long remoteSize)
    {
        if (localTime == remoteTime && localSize == remoteSize)
        {
            return SyncDirection.Identical;
        }
        if (localTime == remoteTime && localSize != remoteSize)
        {
            return SyncDirection.Conflict;
        }
        if (localTime > remoteTime)
        {
            return SyncDirection.Push;
        }
        return SyncDirection.Pull;
    }

    public static List<LauncherCandidate> GetKnownLauncherCandidatePaths()
    {
        var list = new List<LauncherCandidate>();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            string official = Path.Combine(appData, ".minecraft");
            list.Add(new LauncherCandidate("Official Launcher (.minecraft)", official, "Mojang 官方启动器默认游戏路径"));
            
            string pcl2 = Path.Combine(appData, "PCL");
            list.Add(new LauncherCandidate("PCL2 Launcher Profile", pcl2, "Plain Craft Launcher 2 默认配置文件与缓存"));
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            string hmcl = Path.Combine(userProfile, ".hmcl");
            list.Add(new LauncherCandidate("HMCL Launcher Profile", hmcl, "Hello Minecraft! Launcher 缓存目录"));
        }

        return list;
    }

    public static string FormatImportSummary(int newer, int same, int older, int fresh)
    {
        int total = newer + same + older + fresh;
        if (total == 0) return "未选择模组";
        if (fresh == total && newer == 0 && same == 0 && older == 0)
        {
            return $"发现 {fresh} 个全新模组";
        }
        if (same == total && newer == 0 && older == 0 && fresh == 0)
        {
            return $"所选 {same} 个模组均已安装且为相同版本";
        }

        var parts = new List<string>();
        if (newer > 0) parts.Add($"{newer} 个可更新");
        if (same > 0) parts.Add($"{same} 个已是最新");
        if (older > 0) parts.Add($"{older} 个较旧版本");
        if (fresh > 0) parts.Add($"{fresh} 个全新添加");

        return $"发现 {total} 个模组：" + string.Join("，", parts);
    }

    public static (string Text, bool IsAlert) GetSyncDecisionBadge(SyncDecision decision)
    {
        return decision switch
        {
            SyncDecision.Conflict => ("两边都改过", true),
            SyncDecision.Push => ("↑ 电脑较新", false),
            SyncDecision.Pull => ("↓ 手机较新", false),
            SyncDecision.Same => ("已一致", false),
            SyncDecision.OnlyLocal => ("仅电脑有", false),
            SyncDecision.OnlyRemote => ("仅手机有", false),
            _ => ("未知状态", false)
        };
    }

    public static List<LauncherCandidate> FilterAvailableLaunchers(IEnumerable<LauncherCandidate> candidates, Func<string, bool> pathExists)
    {
        if (candidates == null) return new List<LauncherCandidate>();
        pathExists ??= Directory.Exists;
        return candidates.Where(c => !string.IsNullOrEmpty(c.Path) && pathExists(c.Path)).ToList();
    }
}
