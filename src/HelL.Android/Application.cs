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
        // Android's /data/user/0 alias is a system link; resolve it before path guards inspect instance files.
        MobileRuntime.Root = Path.Combine(FilesDir!.CanonicalPath, "HelL");
        return base.CustomizeAppBuilder(builder);
    }
}
