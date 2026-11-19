using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HelL.Services;

public sealed class EngineWallpaperWindow : IDisposable
{
    private delegate bool WindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    private readonly string _executable;
    private readonly string _name = "HelL-Wallpaper-" + Guid.NewGuid().ToString("N");
    private bool _disposed;
    private uint _processId;
    private Process? _ownedProcess;
    public string Title => _name;
    public void Park() { if (Handle != IntPtr.Zero) SetWindowPos(Handle, IntPtr.Zero, -30000, -30000, 0, 0, 0x15); }
    public IntPtr Handle { get; private set; }
    private EngineWallpaperWindow(string executable) { _executable = executable; }
    private static string Quote(string value)
    {
        if (value.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0) throw new ArgumentException("壁纸路径包含无效字符。");
        return "\"" + value + "\"";
    }
    private Process Command(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo(_executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(_executable)! });
        if (process == null) throw new IOException("无法连接 Wallpaper Engine。");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }
    public static async Task<EngineWallpaperWindow> Open(string config, string wallpaper, bool visible = false)
    {
        string folder = Path.GetDirectoryName(config)!;
        string executable = Path.Combine(folder, "wallpaper64.exe"); if (!File.Exists(executable)) executable = Path.Combine(folder, "wallpaper32.exe");
        if (!File.Exists(executable) || !File.Exists(wallpaper)) throw new IOException("Wallpaper Engine 或壁纸文件已不存在。");
        var window = new EngineWallpaperWindow(executable);
        try
        {
            window._ownedProcess = window.Command("-control openWallpaper -file " + Quote(wallpaper) + " -playInWindow " + Quote(window._name) + " -width 960 -height 540" + (visible ? " -x 80 -y 80" : " -x -30000 -y -30000 -borderless"));
            for (int attempt = 0; attempt < 50; attempt++)
            {
                EnumWindows((handle, _) =>
                {
                    var title = new StringBuilder(256); GetWindowText(handle, title, title.Capacity);
                    if (title.ToString() == window._name) window.Handle = handle;
                    return true;
                }, IntPtr.Zero);
                if (window.Handle != IntPtr.Zero) { GetWindowThreadProcessId(window.Handle, out window._processId); return window; }
                await Task.Delay(100).ConfigureAwait(false);
            }
            throw new IOException("Wallpaper Engine 未创建启动器专用壁纸窗口。");
        }
        catch { window.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try
        {
            if (Handle != IntPtr.Zero)
            {
                GetWindowThreadProcessId(Handle, out uint process);
                var title = new StringBuilder(256); GetWindowText(Handle, title, title.Capacity);
                if (process == _processId && title.ToString() == _name) PostMessage(Handle, 0x10, IntPtr.Zero, IntPtr.Zero);
            }
            // Some installations keep the command process alive as a separate engine instance.
            // Only the process we started belongs to this wallpaper session.
            if (_ownedProcess != null && !_ownedProcess.HasExited && !_ownedProcess.WaitForExit(400)) _ownedProcess.Kill();
        }
        catch (Exception ex) { Smoke.Note("wallpaper-window-close", ex.ToString()); }
        finally { _ownedProcess?.Dispose(); _ownedProcess = null; }
    }
}
