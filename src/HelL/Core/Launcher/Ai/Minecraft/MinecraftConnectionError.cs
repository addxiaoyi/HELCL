using System;

namespace HelL.Core.Launcher.Ai.Minecraft;

public sealed class MinecraftConnectionError : InvalidOperationException
{
    public string Code { get; }
    public string Recovery { get; }
    public MinecraftConnectionError(string code, string recovery, string message) : base(message) { Code = code; Recovery = recovery; }
}
