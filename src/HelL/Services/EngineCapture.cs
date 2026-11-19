using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HelL.Services;

// WinRT is loaded only for the optional capture mode; core logic remains usable by net8 tests.
public sealed class EngineCapture : IDisposable
{
    private static readonly Lazy<Type> Runtime = new(() =>
    {
        using var stream = typeof(EngineCapture).Assembly.GetManifestResourceStream("HelL.WallpaperCapture.dll") ?? throw new IOException("动态壁纸组件缺失，请更新启动器。");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return Assembly.Load(bytes.ToArray()).GetType("HelL.WallpaperRuntime.EngineCapture", true)!;
    });
    private readonly object _capture;
    public static Task<string> RequestAccess() => (Task<string>)Runtime.Value.GetMethod("RequestAccess")!.Invoke(null, null)!;
    public EngineCapture(IntPtr window)
    {
        try { _capture = Activator.CreateInstance(Runtime.Value, window)!; }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw(); throw; }
        Attach();
    }
    private EngineCapture(object capture) { _capture = capture; Attach(); }
    private void Attach()
    {
        Runtime.Value.GetEvent("Frame")!.AddEventHandler(_capture, new Action<BitmapSource>(frame => Frame?.Invoke(frame)));
        Runtime.Value.GetEvent("Failed")!.AddEventHandler(_capture, new Action<string>(error => Failed?.Invoke(error)));
    }
    public static async Task<EngineCapture?> Pick(IntPtr owner, string title)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var pending = (Task)Runtime.Value.GetMethod("Pick")!.Invoke(null, new object[] { owner, title })!; await pending;
        var capture = pending.GetType().GetProperty("Result")!.GetValue(pending);
        if (capture == null && elapsed.ElapsedMilliseconds < 500) throw new UnauthorizedAccessException("未取得窗口捕获授权，选择器立即返回。如果没有看到选择器，请检查 Windows 的屏幕截图与录制权限，或在本机桌面会话重试。原壁纸预览已保留。");
        return capture == null ? null : new EngineCapture(capture);
    }
    public event Action<BitmapSource>? Frame;
    public event Action<string>? Failed;
    public bool Active { set => Runtime.Value.GetProperty("Active")!.SetValue(_capture, value); }
    public Task<BitmapSource> FirstFrame() => (Task<BitmapSource>)Runtime.Value.GetMethod("FirstFrame")!.Invoke(_capture, null)!;
    public void Dispose() => ((IDisposable)_capture).Dispose();
}
