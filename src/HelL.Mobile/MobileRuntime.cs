namespace HelL.Mobile;

public static class MobileRuntime
{
    public static string Root { get; set; } = "";
    public static bool Smoke { get; set; }
    public static bool RemotePreview { get; set; }
    public static Action? BeginRemotePreview { get; set; }
    public static Action? FailNextRemoteSnapshot { get; set; }
    public static Action? FailNextRemoteGameStatus { get; set; }
    public static Func<string, HelL.Core.Platform.ISecretStore, Remote.MobileRemoteController>? CreateRemotePreview { get; set; }
    public static int PreviewWidth { get; set; } = 390;
    public static int PreviewHeight { get; set; } = 844;
    public static bool LiveCatalogPreview { get; set; }
    public static bool KeepPreviewOpen { get; set; }
    public static bool VerifyRestart { get; set; }
    public static bool LiveSmoke { get; set; }
    public static string ReportRoot { get; set; } = "";
    public static Func<IDisposable>? BeginAiSmoke { get; set; }
}
