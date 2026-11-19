using System.Diagnostics;
using System.Runtime.InteropServices;
using HelL.Contracts;
using HelL.Core;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class HostGameWindowTests(GameWindowFixture windows) : IClassFixture<GameWindowFixture>
{
    private static readonly HostPermission[] Control = [HostPermission.Read, HostPermission.ControlGame];

    [WindowsGameFact]
    public async Task GracefulCloseUsesTheCurrentWindowAndOldRequestsCannotCloseANewSession()
    {
        using var owner = new BackendFixture();
        var commands = new HostCommandService(owner.Api, "isolated-window-host");
        using var first = await windows.StartAsync(window: true);
        string firstSession = Bind(owner.Api, first.Process);
        HostRequest close = StopRequest(firstSession);

        Assert.Equal("permission_denied", commands.Execute(close, [HostPermission.Read]).ErrorCode);
        await first.ProbeAliveAsync();
        HostReply status = commands.Execute(new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.GameStatus
        }, [HostPermission.Read]);
        Assert.True(status.Game!.Running);
        Assert.Equal(firstSession, status.Game.SessionId);

        Assert.True(commands.Execute(close, Control).Ok);
        await first.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, first.Process.ExitCode);
        Assert.Equal("WM_CLOSE", await File.ReadAllTextAsync(first.Receipt));

        using var second = await windows.StartAsync(window: true);
        string secondSession = Bind(owner.Api, second.Process);
        Assert.True(commands.Execute(close, Control).Ok);
        await second.ProbeAliveAsync();
        Assert.False(File.Exists(second.Receipt));

        HostReply stale = commands.Execute(StopRequest(firstSession), Control);
        Assert.Equal("game_session_changed", stale.ErrorCode);
        await second.ProbeAliveAsync();
        Assert.False(File.Exists(second.Receipt));

        Assert.True(commands.Execute(StopRequest(secondSession), Control).Ok);
        await second.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, second.Process.ExitCode);
        Assert.Equal("WM_CLOSE", await File.ReadAllTextAsync(second.Receipt));
    }

    [WindowsGameFact]
    public async Task MissingWindowReturnsARefusalAndLeavesTheProcessAlive()
    {
        using var owner = new BackendFixture();
        using var headless = await windows.StartAsync(window: false);
        string session = Bind(owner.Api, headless.Process);
        var commands = new HostCommandService(owner.Api, "isolated-headless-host");

        HostReply refused = commands.Execute(StopRequest(session), Control);

        Assert.False(refused.Ok);
        Assert.Equal("graceful_stop_unavailable", refused.ErrorCode);
        await headless.ProbeAliveAsync();
        Assert.False(File.Exists(headless.Receipt));
    }

    [WindowsGameFact]
    public async Task WorkerCancellationDoesNotCloseOrKillTheCurrentGameWindow()
    {
        using var owner = new BackendFixture();
        using var game = await windows.StartAsync(window: true);
        Bind(owner.Api, game.Process);
        string taskId = owner.Api.StartTask("isolated cancellable worker", context =>
        {
            context.Token.WaitHandle.WaitOne();
            context.Token.ThrowIfCancellationRequested();
            return "unreachable";
        });

        // The owner's second check rejects a task that became the launch task after dispatch.
        owner.Api._launch_task_id = taskId;
        Assert.False(owner.Api.remote_cancel_task(taskId));
        lock (owner.Api._lock) Assert.False(owner.Api._workers[taskId].IsCancelled);
        await game.ProbeAliveAsync();
        Assert.False(File.Exists(game.Receipt));

        owner.Api._launch_task_id = null!;
        Assert.True(owner.Api.remote_cancel_task(taskId));
        Assert.False(Py.Truthy(owner.Api.wait_task(taskId, 5L)["ok"]));
        await game.ProbeAliveAsync();
        Assert.False(File.Exists(game.Receipt));

        owner.Api._launch_task_id = null!;
        string unrelatedTask = owner.Api.StartTask("remote cancellation fixture", context =>
        {
            context.Token.WaitHandle.WaitOne();
            context.Token.ThrowIfCancellationRequested();
            return "unreachable";
        });
        var commands = new HostCommandService(owner.Api, "isolated-cancel-host");
        HostReply cancelled = commands.Execute(new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = unrelatedTask
        }, [HostPermission.Read, HostPermission.CancelTasks]);
        Assert.True(cancelled.Ok);
        Assert.False(Py.Truthy(owner.Api.wait_task(unrelatedTask, 5L)["ok"]));
        await game.ProbeAliveAsync();
        Assert.False(File.Exists(game.Receipt));
    }

    private static string Bind(BackendApi api, Process process)
    {
        lock (api._game_lock)
        {
            api._game_proc = process;
            api._game_session_id = Guid.NewGuid().ToString("N");
            api._game_stop_requested = false;
            api._game_stop_requested_at = 0;
            return api._game_session_id;
        }
    }

    private static HostRequest StopRequest(string session) => new()
    {
        RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.StopGame, SessionId = session
    };

    private sealed class WindowsGameFactAttribute : FactAttribute
    {
        public WindowsGameFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Native game window close verification requires Windows.";
        }
    }
}

public sealed class GameWindowFixture : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-game-window-" + Guid.NewGuid().ToString("N"));
    private readonly string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private readonly string assembly;

    public GameWindowFixture()
    {
        assembly = Path.Combine(root, "bin", "HelL.TestGameWindow.dll");
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "window.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>HelL.TestGameWindow</AssemblyName>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.cs"), WindowSource);
        using var build = new Process { StartInfo = StartInfo(dotnet) };
        foreach (string argument in new[] { "build", Path.Combine(root, "window.csproj"), "-c", "Release", "-o", Path.Combine(root, "bin"), "--nologo", "--verbosity", "quiet", "--ignore-failed-sources" })
            build.StartInfo.ArgumentList.Add(argument);
        if (!build.Start()) throw new IOException("The isolated game window fixture build did not start.");
        Task<string> output = build.StandardOutput.ReadToEndAsync();
        Task<string> errors = build.StandardError.ReadToEndAsync();
        if (!build.WaitForExit(60_000))
        {
            build.Kill(entireProcessTree: true);
            throw new TimeoutException("The isolated game window fixture build timed out.");
        }
        string messages = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
        if (build.ExitCode != 0) throw new IOException("The isolated game window fixture build failed: " + messages);
    }

    public async Task<OwnedGameWindow> StartAsync(bool window)
    {
        string receipt = Path.Combine(root, Guid.NewGuid().ToString("N") + ".closed");
        var process = new Process { StartInfo = StartInfo(dotnet) };
        process.StartInfo.RedirectStandardInput = true;
        foreach (string argument in new[] { assembly, window ? "window" : "headless", receipt })
            process.StartInfo.ArgumentList.Add(argument);
        var owned = new OwnedGameWindow(process, receipt, window);
        try
        {
            if (!process.Start()) throw new IOException("The isolated game process did not start.");
            string? ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (ready != "ready")
                throw new IOException("The isolated game process did not publish readiness: " + await process.StandardError.ReadToEndAsync());
            if (window)
                await UntilAsync(() => { process.Refresh(); return process.MainWindowHandle != IntPtr.Zero; }, "The isolated window was not discoverable.");
            return owned;
        }
        catch { owned.Dispose(); throw; }
    }

    internal static async Task UntilAsync(Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException(message);
            await Task.Delay(20);
        }
    }

    private static ProcessStartInfo StartInfo(string executable) => new(executable)
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    // The native window is visible to Win32 enumeration but stays off-screen and out of the taskbar.
    private const string WindowSource = """
        using System;
        using System.IO;
        using System.Runtime.InteropServices;

        internal static class Program
        {
            private static string receipt = "";
            private static readonly WindowProcedure procedure = ProcessMessage;

            private static int Main(string[] args)
            {
                receipt = args[1];
                if (args[0] == "headless")
                {
                    Console.WriteLine("ready");
                    while (Console.ReadLine() is not null) Console.WriteLine("alive");
                    return 0;
                }
                string name = "HelL isolated game fixture " + Guid.NewGuid().ToString("N");
                IntPtr module = GetModuleHandleW(null);
                var type = new WindowClass { Procedure = procedure, Instance = module, ClassName = name };
                if (RegisterClassW(ref type) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                IntPtr window = CreateWindowExW(0x80, name, name, 0x10CF0000, -32000, -32000, 64, 64, IntPtr.Zero, IntPtr.Zero, module, IntPtr.Zero);
                if (window == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Console.WriteLine("ready");
                int code;
                while ((code = GetMessageW(out Message message, IntPtr.Zero, 0, 0)) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }
                return code == 0 ? 0 : 1;
            }

            private static IntPtr ProcessMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
            {
                if (message == 0x0010)
                {
                    File.WriteAllText(receipt, "WM_CLOSE");
                    DestroyWindow(window);
                    return IntPtr.Zero;
                }
                if (message == 0x0002) { PostQuitMessage(0); return IntPtr.Zero; }
                if (message == 0x8001) { File.WriteAllText(receipt + ".probe", "alive"); return IntPtr.Zero; }
                return DefWindowProcW(window, message, wParam, lParam);
            }

            private delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WindowClass
            {
                public uint Style;
                public WindowProcedure Procedure;
                public int ClassExtra;
                public int WindowExtra;
                public IntPtr Instance;
                public IntPtr Icon;
                public IntPtr Cursor;
                public IntPtr Background;
                public string? MenuName;
                public string ClassName;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct Message
            {
                public IntPtr Window;
                public uint Id;
                public UIntPtr WParam;
                public IntPtr LParam;
                public uint Time;
                public int X;
                public int Y;
                public uint Private;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr GetModuleHandleW(string? module);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern ushort RegisterClassW(ref WindowClass type);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWindowExW(uint extra, string type, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr DefWindowProcW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetMessageW(out Message message, IntPtr window, uint minimum, uint maximum);
            [DllImport("user32.dll")]
            private static extern bool TranslateMessage(ref Message message);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr DispatchMessageW(ref Message message);
            [DllImport("user32.dll")]
            private static extern bool DestroyWindow(IntPtr window);
            [DllImport("user32.dll")]
            private static extern void PostQuitMessage(int code);
        }
        """;
}

public sealed class OwnedGameWindow(Process process, string receipt, bool hasWindow) : IDisposable
{
    public Process Process { get; } = process;
    public string Receipt { get; } = receipt;

    public async Task ProbeAliveAsync()
    {
        Assert.False(Process.HasExited);
        if (hasWindow)
        {
            string probe = Receipt + ".probe";
            File.Delete(probe);
            Process.Refresh();
            Assert.True(PostMessageW(Process.MainWindowHandle, 0x8001, UIntPtr.Zero, IntPtr.Zero));
            await GameWindowFixture.UntilAsync(() => File.Exists(probe), "The isolated game window stopped processing messages.");
        }
        else
        {
            await Process.StandardInput.WriteLineAsync("probe");
            await Process.StandardInput.FlushAsync();
            Assert.Equal("alive", await Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.False(Process.HasExited);
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                if (!Process.WaitForExit(10_000)) throw new TimeoutException("The owned game fixture did not stop during cleanup.");
            }
        }
        catch (InvalidOperationException) { }
        finally { Process.Dispose(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
}
