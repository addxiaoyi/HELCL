using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HelL.Mobile.Controls;

/// <summary>Displays the supplied artwork regions; navigation and account state remain native controls.</summary>
public sealed class ReferenceArt : Control
{
    public static readonly StyledProperty<string> PartProperty = AvaloniaProperty.Register<ReferenceArt, string>(nameof(Part), "hero");
    public static readonly FuncValueConverter<object?, string> BlockConverter = new(value => BlockPartFor(value?.ToString()));
    private static readonly Lazy<Bitmap> Landscape = new(() => Load("ReferenceLandscape.png"));
    private static readonly Lazy<Bitmap> Portrait = new(() => Load("ReferencePortrait.png"));
    private static readonly Lazy<Bitmap> Character = new(() => Load("Character.png"));
    private static readonly Lazy<Bitmap> Mark = new(() => Load("Mark.png"));
    public string Part { get => GetValue(PartProperty); set => SetValue(PartProperty, value); }

    static ReferenceArt() => AffectsRender<ReferenceArt>(PartProperty);

    public static string BlockPartFor(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "grass";
        string lower = hint.ToLowerInvariant();
        if (lower.Contains("fabric") || lower.Contains("光影")) return "crafting";
        if (lower.Contains("forge") || lower.Contains("机械") || lower.Contains("暮色")) return "nether";
        if (lower.Contains("quilt") || lower.Contains("optifine") || lower.Contains("pack") || lower.Contains("整合")) return "wood";
        if (lower.Contains("尚未") || lower.Contains("未选择") || lower == "none") return "stone";
        return "grass";
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var (image, source) = Part switch
        {
            "logo" => (Mark.Value, new Rect(390, 240, 738, 502)),
            "avatar" => (Character.Value, new Rect(150, 70, 725, 1416)),
            "grass" => (Portrait.Value, new Rect(573, 330, 54, 54)),
            "crafting" => (Portrait.Value, new Rect(573, 420, 54, 54)),
            "stone" => (Portrait.Value, new Rect(573, 508, 54, 54)),
            "nether" => (Portrait.Value, new Rect(573, 597, 54, 54)),
            "wood" => (Portrait.Value, new Rect(573, 685, 54, 54)),
            "hero" when Bounds.Width / Bounds.Height < 2.2 => (Landscape.Value, new Rect(355, 180, 480, 158)),
            _ => (Landscape.Value, new Rect(220, 180, 885, 158))
        };
        if (Part is "logo" or "avatar")
        {
            double fit = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
            var destination = new Rect((Bounds.Width - source.Width * fit) / 2,
                Bounds.Height - source.Height * fit, source.Width * fit, source.Height * fit);
            context.DrawImage(image, source, destination);
            return;
        }
        double scale = Math.Max(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var crop = new Rect(source.Center.X - Bounds.Width / scale / 2, source.Center.Y - Bounds.Height / scale / 2,
            Bounds.Width / scale, Bounds.Height / scale);
        context.DrawImage(image, crop, new Rect(Bounds.Size));
    }

    private static Bitmap Load(string file)
    {
        string assembly = typeof(ReferenceArt).Assembly.GetName().Name ?? "HelL.Mobile";
        using var stream = AssetLoader.Open(new Uri($"avares://{assembly}/Assets/{file}"));
        return new Bitmap(stream);
    }
}
