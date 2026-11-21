using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Avalonia.Layout;

namespace HelL.Desktop.Controls;

public sealed class MessageDocument : ContentControl
{
    internal Func<string, Task>? ClipboardWriter { get; set; }
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MessageDocument, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) Render();
    }
    private void Render()
    {
        var layout = new StackPanel { Spacing = 6 };
        var code = new List<string>();
        bool fenced = false;
        bool paragraphBreak = false;
        string language = "";
        foreach (string line in (Text ?? "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (fenced) { AddCode(layout, string.Join("\n", code), language); code.Clear(); }
                else language = line.Trim()[3..].Trim();
                fenced = !fenced;
                continue;
            }
            if (fenced) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { paragraphBreak = true; continue; }
            int heading = line.TakeWhile(c => c == '#').Count();
            string text = heading is > 0 and <= 6 ? line[heading..].TrimStart() : line;
            layout.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                FontWeight = heading is > 0 and <= 6 ? FontWeight.SemiBold : FontWeight.Normal,
                Margin = new Thickness(0, paragraphBreak || heading is > 0 and <= 6 && layout.Children.Count > 0 ? 6 : 0, 0, 0),
                FontSize = heading is > 0 and <= 2 ? 18 : heading is > 2 and <= 6 ? 16 : 14,
                LineHeight = heading is > 0 and <= 2 ? 26 : 22 });
            paragraphBreak = false;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"https?://[^\s)<>]+"))
            {
                if (!Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? address) || address.UserInfo.Length != 0) continue;
                var link = new Button { Content = "打开链接：" + address.Host, Classes = { "ghost" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                link.Click += async (_, _) =>
                {
                    try
                    {
                        var launcher = TopLevel.GetTopLevel(this)?.Launcher ?? throw new IOException("System browser unavailable.");
                        if (!await launcher.LaunchUriAsync(address)) throw new IOException("System browser refused the link.");
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    { link.Content = "链接打开失败，可复制原地址"; System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
                };
                layout.Children.Add(link);
            }
        }
        if (code.Count > 0) AddCode(layout, string.Join("\n", code), language);
        Content = layout;
    }
    private void AddCode(StackPanel layout, string text, string language)
    {
        var copy = new Button { Content = "复制代码", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Right,
            MinHeight = 30, Padding = new Thickness(8, 3), FontSize = 12 };
        Avalonia.Automation.AutomationProperties.SetName(copy, "复制此代码块的原始内容");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(copy, Avalonia.Automation.AutomationLiveSetting.Polite);
        copy.Click += async (_, _) =>
        {
            try
            {
                if (ClipboardWriter is { } writer) await writer(text);
                else
                {
                    if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) throw new IOException("Clipboard unavailable.");
                    await clipboard.SetTextAsync(text);
                }
                copy.Content = "已复制代码";
                Avalonia.Automation.AutomationProperties.SetName(copy, "代码已复制");
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                copy.Content = "复制失败，请重试";
                Avalonia.Automation.AutomationProperties.SetName(copy, "代码复制失败，请重试");
                System.Diagnostics.Trace.TraceWarning("MessageDocument copy: " + error.GetType().Name);
            }
        };
        bool knownLanguage = language.Length is > 0 and <= 24 && language.All(character => char.IsLetterOrDigit(character) || character is '#' or '+' or '-' or '_');
        var caption = new TextBlock { Text = knownLanguage ? language : "代码", FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(caption);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        var source = new SelectableTextBlock { Text = text,
            FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, DejaVu Sans Mono"),
            FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.NoWrap };
        var block = new StackPanel { Spacing = 8, Children =
        { header, new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = source } } };
        var surface = new Border { Padding = new Thickness(14, 10, 14, 14), CornerRadius = new CornerRadius(8), Child = block };
        surface.Bind(Border.BackgroundProperty, this.GetResourceObservable("HelL.Field"));
        layout.Children.Add(surface);
    }
}
