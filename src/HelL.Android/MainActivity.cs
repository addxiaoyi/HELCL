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
        bool smoke = Intent?.GetBooleanExtra("hell_ui_smoke", false) == true;
        bool restart = Intent?.GetBooleanExtra("hell_verify_restart", false) == true;
        if (smoke || restart)
        {
            MobileRuntime.Smoke = smoke;
            MobileRuntime.LiveSmoke = smoke && Intent?.GetBooleanExtra("hell_catalog_live", false) == true;
            MobileRuntime.VerifyRestart = restart;
            MobileRuntime.Root = Path.Combine(FilesDir!.CanonicalPath, "ui-smoke");
            MobileRuntime.ReportRoot = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "ui-evidence");
        }
        base.OnCreate(savedInstanceState);
    }
}
