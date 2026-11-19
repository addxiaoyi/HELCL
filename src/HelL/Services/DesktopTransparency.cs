using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace HelL.Services;

internal static class DesktopTransparency
{
    internal const double InputOpacity = 1d / byte.MaxValue;

    public static void Prepare(Window window)
    {
        try
        {
            string config = Path.Combine(BridgeHost.FindRoot(), "config.json");
            if (!File.Exists(config)) return;
            using var settings = JsonDocument.Parse(File.ReadAllText(config));
            if (settings.RootElement.ValueKind == JsonValueKind.Object && settings.RootElement.TryGetProperty("ui_wallpaper_mode", out var mode) &&
                mode.ValueKind == JsonValueKind.String && mode.GetString() == "transparent")
                window.AllowsTransparency = true;
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
        {
            Smoke.Note("transparent-window", ex.ToString());
        }
    }
}
