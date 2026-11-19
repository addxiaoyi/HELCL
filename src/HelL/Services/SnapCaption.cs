using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace HelL.Services;

public static class SnapCaption
{
    public static readonly DependencyProperty HoveredProperty = DependencyProperty.RegisterAttached("Hovered", typeof(bool), typeof(SnapCaption), new PropertyMetadata(false));
    public static bool GetHovered(DependencyObject element) => (bool)element.GetValue(HoveredProperty);
    public static void SetHovered(DependencyObject element, bool value) => element.SetValue(HoveredProperty, value);
    private const int HitMaximize = 9;
    private const uint TrackLeaveNonClient = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseTracking
    {
        public uint Size;
        public uint Flags;
        public IntPtr Window;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref MouseTracking tracking);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hwnd, ref ScreenPoint point);

    internal static void Attach(Window window, Button maximize)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source == null) throw new InvalidOperationException("Snap caption requires an initialized window.");
        bool hovered = false;
        bool pressed = false;
        source.AddHook(WindowMessage);
        window.StateChanged += ClearHover;
        window.Closed += (_, _) =>
        {
            source.RemoveHook(WindowMessage);
            window.StateChanged -= ClearHover;
        };

        IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0084 && HitButton(lParam))
            {
                // DWM uses this hit target to open its snap flyout.
                handled = true;
                return new IntPtr(HitMaximize);
            }
            if (message == 0x00A0)
            {
                bool hot = wParam.ToInt32() == HitMaximize;
                if (hot && !hovered)
                {
                    var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = TrackLeaveNonClient, Window = hwnd };
                    if (!TrackMouseEvent(ref tracking)) Trace.WriteLine("[SnapCaption] Cannot track mouse leave: " + Marshal.GetLastWin32Error());
                }
                SetHover(hot);
            }
            if (message == 0x00A1 && wParam.ToInt32() == HitMaximize && HitButton(lParam))
            {
                pressed = true;
                SetCapture(hwnd);
                SetHover(true);
                handled = true;
            }
            // Mouse capture turns subsequent movement and release into client messages.
            if (message == 0x0200 && pressed) SetHover(HitButton(lParam, client: true));
            if ((message == 0x0202 || message == 0x00A2) && pressed)
            {
                bool activate = HitButton(lParam, client: message == 0x0202);
                CancelPress();
                handled = true;
                if (activate) maximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            if (message == 0x02A2 && !pressed) SetHover(false);
            if (message == 0x0215 || message == 0x001F || (message == 0x0086 && wParam == IntPtr.Zero)) CancelPress();
            return IntPtr.Zero;
        }

        bool HitButton(IntPtr coordinates, bool client = false)
        {
            if (!maximize.IsVisible || !maximize.IsEnabled) return false;
            long packed = coordinates.ToInt64();
            var point = new ScreenPoint { X = (short)(packed & 0xffff), Y = (short)((packed >> 16) & 0xffff) };
            if (client && !ClientToScreen(source.Handle, ref point)) return false;
            Point pointer = maximize.PointFromScreen(new Point(point.X, point.Y));
            return new Rect(maximize.RenderSize).Contains(pointer);
        }

        void ClearHover(object? sender, EventArgs args) => CancelPress();
        void CancelPress()
        {
            bool wasPressed = pressed;
            pressed = false;
            if (wasPressed && GetCapture() == source.Handle) ReleaseCapture();
            SetHover(false);
        }

        void SetHover(bool hot)
        {
            hovered = hot;
            SetHovered(maximize, hot);
            if (hot && !Glass.IsMac) maximize.SetResourceReference(Control.BackgroundProperty, pressed ? "B.Sel" : "B.Hover");
            else maximize.ClearValue(Control.BackgroundProperty);
        }
    }
}
