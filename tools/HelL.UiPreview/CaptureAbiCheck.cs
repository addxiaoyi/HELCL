using System;
using System.Runtime.InteropServices;
using System.Threading;

internal static class CaptureAbiCheck
{
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr text);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr text);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface CaptureFactory { [PreserveSig] int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr item); }

    internal static void Run(IntPtr window)
    {
        uint length=0; int identity=GetCurrentPackageFullName(ref length,IntPtr.Zero);
        Console.WriteLine("[CAPTURE ABI] packageIdentityStatus="+identity+" bitness="+(IntPtr.Size*8)+" apartment="+Thread.CurrentThread.GetApartmentState());
        string name="Windows.Graphics.Capture.GraphicsCaptureItem"; IntPtr text=IntPtr.Zero, pointer=IntPtr.Zero, item=IntPtr.Zero; object? factory=null;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(name,(uint)name.Length,out text));
            Guid interop=new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"); int activation=RoGetActivationFactory(text,ref interop,out pointer);
            Console.WriteLine("[CAPTURE ABI] activation=0x"+activation.ToString("X8")); Marshal.ThrowExceptionForHR(activation);
            factory=Marshal.GetObjectForIUnknown(pointer); Guid iid=new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            int status=((CaptureFactory)factory).CreateForWindow(window,ref iid,out item);
            Console.WriteLine("[CAPTURE ABI] raw CreateForWindow=0x"+status.ToString("X8"));
        }
        finally { if(item!=IntPtr.Zero)Marshal.Release(item);if(factory!=null)Marshal.ReleaseComObject(factory);if(pointer!=IntPtr.Zero)Marshal.Release(pointer);if(text!=IntPtr.Zero)WindowsDeleteString(text); }
    }
}
