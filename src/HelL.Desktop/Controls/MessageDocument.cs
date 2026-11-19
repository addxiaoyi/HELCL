using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input.Platform;

namespace HelL.Desktop.Controls;

public sealed class MessageDocument : ContentControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<MessageDocument, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) Render();
    }
    private void Render()
    {
        var layout = new StackPanel { Spacing = 7 };
        var code = new List<string>();
        bool fenced = false;
        foreach (string line in (Text ?? "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (fenced) { AddCode(layout, string.Join("\n", code)); code.Clear(); }
                fenced = !fenced;
                continue;
            }
            if (fenced) { code.Add(line); continue; }
            int heading = line.TakeWhile(c => c == '#').Count();
            string text = heading is > 0 and <= 6 ? line[heading..].TrimStart() : line;
            layout.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
                FontWeight = heading is > 0 and <= 6 ? FontWeight.SemiBold : FontWeight.Normal,
                FontSize = heading is > 0 and <= 6 ? 17 : 13, LineHeight = 21 });
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
        if (code.Count > 0) AddCode(layout, string.Join("\n", code));
        Content = layout;
    }
    private void AddCode(StackPanel layout, string text)
    {
        var copy = new Button { Content = "复制代码", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        Avalonia.Automation.AutomationProperties.SetName(copy, "复制此代码块");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) throw new IOException("Clipboard unavailable.");
                await clipboard.SetTextAsync(text);
                copy.Content = "已复制代码";
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { copy.Content = "复制失败，请重试"; System.Diagnostics.Trace.TraceWarning("MessageDocument copy: " + error.GetType().Name); }
        };
        layout.Children.Add(new Border { Classes = { "tile" }, Padding = new Thickness(10), Child = new StackPanel { Spacing = 5, Children =
        { new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = new SelectableTextBlock { Text = text, FontFamily = new FontFamily("monospace"), FontSize = 12 } }, copy } } });
    }
}
