using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using HelL.Mobile;

namespace HelL.Android;

[Application]
public sealed class Application(nint reference, JniHandleOwnership transfer) : AvaloniaAndroidApplication<MobileApp>(reference, transfer)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        MobileRuntime.Root = Path.Combine(FilesDir!.AbsolutePath, "HelL");
        return base.CustomizeAppBuilder(builder);
    }
}
