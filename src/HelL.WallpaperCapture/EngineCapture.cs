using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace HelL.WallpaperRuntime;

public sealed class EngineCapture : IDisposable
{
    public static async Task<string> RequestAccess()
    {
        if (!Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.Graphics.Capture.GraphicsCaptureAccess")) return "此 Windows 版本没有程序化捕获权限接口。";
        return (await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Programmatic)).ToString();
    }
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr item);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr item);
    }
    [ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow { void Initialize(IntPtr window); }
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out uint level, out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr runtimeDevice);
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly IDirect3DDevice _device;
    private readonly GraphicsCaptureItem _item;
    private readonly TaskCompletionSource<BitmapSource> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _processing, _disposed;
    private long _lastFrame;
    private int _active = 1;
    public bool Active { get => Volatile.Read(ref _active) != 0; set => Volatile.Write(ref _active, value ? 1 : 0); }
    public event Action<BitmapSource>? Frame;
    public event Action<string>? Failed;

    private static GraphicsCaptureItem WindowItem(IntPtr window)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("此系统不支持读取动态壁纸画面。");
        var factory = (ICaptureInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        Guid iid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        int status = factory.CreateForWindow(window, ref iid, out IntPtr pointer);
        if (status == unchecked((int)0x80070005)) throw new UnauthorizedAccessException("Windows 在创建窗口捕获目标时拒绝访问（CreateForWindow）。");
        Marshal.ThrowExceptionForHR(status);
        GraphicsCaptureItem item;
        try { item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
        return item;
    }
    public static async Task<EngineCapture?> Pick(IntPtr owner, string expectedTitle)
    {
        var picker = new GraphicsCapturePicker(); ((IInitializeWithWindow)(object)picker).Initialize(owner);
        var item = await picker.PickSingleItemAsync(); if (item == null) return null;
        if (!string.Equals(item.DisplayName, expectedTitle, StringComparison.Ordinal)) throw new InvalidOperationException("请选择名为「" + expectedTitle + "」的启动器壁纸窗口。");
        return new EngineCapture(item);
    }
    public EngineCapture(IntPtr window) : this(WindowItem(window)) { }
    private EngineCapture(GraphicsCaptureItem item)
    {
        _item = item;
        IntPtr d3d = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, runtime = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3d, out _, out context));
            Guid dxgiId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C"); Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref dxgiId, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out runtime)); _device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(runtime);
        }
        finally { foreach (IntPtr handle in new[] { runtime, dxgi, context, d3d }) if (handle != IntPtr.Zero) Marshal.Release(handle); }
        try
        {
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = false;
            _pool.FrameArrived += FrameArrived; item.Closed += ItemClosed;
            _session.StartCapture();
        }
        catch { _session?.Dispose(); _pool?.Dispose(); _device.Dispose(); throw; }
    }
    public async Task<BitmapSource> FirstFrame()
    {
        if (await Task.WhenAny(_first.Task, Task.Delay(4000)).ConfigureAwait(false) != _first.Task) throw new IOException("未收到动态壁纸画面，暂用项目预览图。");
        return await _first.Task.ConfigureAwait(false);
    }
    private async void FrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _processing, 1) != 0) return;
        try
        {
            using var frame = pool.TryGetNextFrame();
            long now = Stopwatch.GetTimestamp();
            if (frame == null || !Active || (now - _lastFrame) * 1000 / Stopwatch.Frequency < 100) return;
            _lastFrame = now;
            using var software = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            using var bitmap = SoftwareBitmap.Convert(software, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 34000000) throw new IOException("动态壁纸尺寸过大。");
            byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyToBuffer(bytes.AsBuffer());
            var image = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null, bytes, bitmap.PixelWidth * 4);
            image.Freeze(); _first.TrySetResult(image);
            if (Active && Volatile.Read(ref _disposed) == 0) Frame?.Invoke(image);
        }
        catch (Exception ex) { if (Volatile.Read(ref _disposed) == 0) { _first.TrySetException(ex); Failed?.Invoke(ex.Message); } }
        finally { Interlocked.Exchange(ref _processing, 0); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _item.Closed -= ItemClosed; _pool.FrameArrived -= FrameArrived; _session.Dispose(); _pool.Dispose(); _device.Dispose();
    }
    private void ItemClosed(GraphicsCaptureItem sender, object args) => Failed?.Invoke("Wallpaper Engine 渲染窗口已关闭。");
}
