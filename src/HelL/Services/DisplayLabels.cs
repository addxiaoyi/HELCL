using System;
using System.Collections.Generic;

namespace HelL.Services;

public static class DisplayLabels
{
    private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["release"] = "正式版", ["beta"] = "测试版", ["alpha"] = "早期测试版", ["snapshot"] = "快照",
        ["vanilla"] = "原版", ["pending"] = "等待中", ["queued"] = "排队中", ["running"] = "进行中",
        ["downloading"] = "下载中", ["completed"] = "已完成", ["success"] = "成功", ["failed"] = "失败",
        ["cancelled"] = "已取消", ["canceled"] = "已取消", ["paused"] = "已暂停", ["waiting"] = "等待中",
        ["open"] = "待处理", ["closed"] = "已关闭", ["resolved"] = "已解决", ["submitted"] = "已提交",
        ["in_progress"] = "处理中", ["bug"] = "问题反馈", ["feature"] = "功能建议", ["suggestion"] = "建议",
        ["survival"] = "生存", ["creative"] = "创造", ["adventure"] = "冒险", ["spectator"] = "旁观"
    };

    public static string Of(string value) => Labels.TryGetValue(value ?? "", out var label) ? I18n.L(label) : value ?? "";
    public static string Source(string value) => value.Equals("modrinth", StringComparison.OrdinalIgnoreCase) ? "Modrinth"
        : value.Equals("curseforge", StringComparison.OrdinalIgnoreCase) ? "CurseForge" : value;
    public static string Count(long value)
    {
        if (I18n.Current != "zh_CN") return ModernUiVisuals.FormatCompactMetric(value);
        return value >= 100000000 ? (value / 100000000d).ToString("0.0") + " 亿"
            : value >= 10000 ? (value / 10000d).ToString("0.0") + " 万" : value.ToString("N0");
    }
}
