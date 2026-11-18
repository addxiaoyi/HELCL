using System;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftProcessError
{
    public static string Describe(string exitCode, string diagnostic)
    {
        if (diagnostic.IndexOf("ERR_MODULE_NOT_FOUND", StringComparison.Ordinal) >= 0 || diagnostic.IndexOf("Cannot find module", StringComparison.Ordinal) >= 0)
            return "AI 玩家组件文件缺失。请打开“高级”，点击“修复游戏组件”后重试。";
        if (diagnostic.IndexOf("SyntaxError", StringComparison.Ordinal) >= 0 || diagnostic.IndexOf("ERR_REQUIRE_ESM", StringComparison.Ordinal) >= 0)
            return "AI 玩家组件无法加载。请确认使用 Node.js 22 或更新版本，再修复游戏组件。";
        return "AI 玩家连接组件意外退出（代码 " + exitCode + "）。可在“高级”中修复游戏组件后重试。";
    }
}
