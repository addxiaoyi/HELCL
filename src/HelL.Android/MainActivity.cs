using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using HelL.Mobile;

namespace HelL.Android;

[Activity(Name = "io.hell.launcher.preview.MainActivity", Label = "HelL Preview", Theme = "@style/HelLTheme", Icon = "@drawable/hell_icon",
    MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (Intent?.GetBooleanExtra("hell_ui_smoke", false) == true)
        {
            MobileRuntime.Smoke = true;
            MobileRuntime.Root = Path.Combine(FilesDir!.AbsolutePath, "ui-smoke");
            MobileRuntime.ReportRoot = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "ui-evidence");
        }
        base.OnCreate(savedInstanceState);
    }
}
