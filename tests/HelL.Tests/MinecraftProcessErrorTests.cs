using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class MinecraftProcessErrorTests
{
    [Theory]
    [InlineData("Error [ERR_MODULE_NOT_FOUND]: private-test-token", "修复游戏组件")]
    [InlineData("Error: Cannot find module C:/private-test-token", "组件文件缺失")]
    [InlineData("SyntaxError private-test-token", "Node.js 22")]
    [InlineData("unknown crash private-test-token", "代码 1")]
    public void ExitMessageExplainsRecoveryWithoutEchoingProcessOutput(string diagnostic, string expected)
    {
        string message = MinecraftProcessError.Describe("1", diagnostic);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("private-test-token", message);
    }
}
