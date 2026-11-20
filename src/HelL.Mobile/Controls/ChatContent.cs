using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.LogicalTree;

namespace HelL.Mobile.Controls;

public sealed class ChatContent : StackPanel
{
    private static readonly FontFamily CodeFont = FontFamily.Parse(
        "avares://HelL.Mobile/Assets/Fonts#Cousine, avares://HelL.Mobile/Assets/Fonts#Noto Sans SC");
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ChatContent, string>(nameof(Text), "");
    public static readonly StyledProperty<double> TextScaleProperty = AvaloniaProperty.Register<ChatContent, double>(nameof(TextScale), 1);
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double TextScale { get => GetValue(TextScaleProperty); set => SetValue(TextScaleProperty, value); }
    static ChatContent()
    {
        TextProperty.Changed.AddClassHandler<ChatContent>((view, _) => view.Render());
        TextScaleProperty.Changed.AddClassHandler<ChatContent>((view, _) => view.ApplyTextSize());
    }
    public ChatContent() { Spacing = 8; }
    private void Render()
    {
        Children.Clear();
        string[] chunks = Text.Split("```", StringSplitOptions.None);
        for (int index = 0; index < chunks.Length; index++)
        {
            string value = chunks[index];
            if (value.Length == 0) continue;
            if (index % 2 == 0)
            {
                foreach (string line in value.Trim().Split('\n'))
                {
                    bool heading = line.StartsWith('#');
                    string readable = heading ? line.TrimStart('#', ' ') : line.TrimStart().StartsWith("- ", StringComparison.Ordinal) ? "• " + line.TrimStart()[2..] : line;
                    if (line.Length == 0) { Children.Add(new Border { Height = 4 }); continue; }
                    var paragraph = new SelectableTextBlock { Text = readable, TextWrapping = TextWrapping.Wrap,
                        FontSize = heading ? 17 : 15, LineHeight = heading ? 27 : 24,
                        FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal };
                    paragraph.Classes.Add(heading ? "chat-heading" : "chat-prose");
                    Children.Add(paragraph);
                }
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(value, @"https://[^\s<>\]\)]+"))
                {
                    if (!Uri.TryCreate(match.Value, UriKind.Absolute, out Uri? address) || address.UserInfo.Length != 0) continue;
                    var open = new Button { Content = "打开链接：" + address.Host, Classes = { "secondary" }, MinHeight = 44 };
                    open.Click += async (_, _) =>
                    {
                        try
                        {
                            var top = TopLevel.GetTopLevel(this) ?? throw new IOException("链接启动器不可用。");
                            if (!await top.Launcher.LaunchUriAsync(address)) open.Content = "无法打开，可选择原文复制链接";
                        }
                        catch (Exception error) when (error is not OutOfMemoryException)
                        { open.Content = "无法打开，可选择原文复制链接"; System.Diagnostics.Trace.TraceWarning("Mobile chat link: " + error.GetType().Name); }
                    };
                    Children.Add(open);
                }
                continue;
            }
            int newline = value.IndexOf('\n');
            string language = newline >= 0 ? value[..newline].Trim() : "";
            string code = newline >= 0 ? value[(newline + 1)..] : value;
            var block = new StackPanel { Spacing = 6 };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            header.Children.Add(new TextBlock { Text = language.Length == 0 ? "代码" : language, FontSize = 12, LineHeight = 18,
                Classes = { "caption", "code-language" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var codeText = new SelectableTextBlock { Text = code,
                FontFamily = CodeFont, FontSize = 13, LineHeight = 20,
                TextWrapping = TextWrapping.NoWrap, Classes = { "chat-code" } };
            var copy = new Button { Content = "复制代码", Classes = { "secondary", "code-copy" }, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 44, Padding = new Thickness(10, 4) };
            Avalonia.Automation.AutomationProperties.SetName(copy, "复制此代码块的原始内容");
            copy.Click += async (_, _) =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new IOException("剪贴板不可用。");
                    await clipboard.SetTextAsync(code);
                    copy.Content = "已复制代码";
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                { copy.Content = "复制失败，可长按选择文本"; System.Diagnostics.Trace.TraceWarning("Mobile code copy: " + error.GetType().Name); }
            };
            Grid.SetColumn(copy, 1);
            header.Children.Add(copy);
            block.Children.Add(header);
            block.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = codeText });
            Children.Add(new Border { Classes = { "code-surface" }, Padding = new Thickness(12, 8, 12, 12), Child = block });
        }
        ApplyTextSize();
    }

    private void ApplyTextSize()
    {
        double scale = Math.Clamp(TextScale, 1, 2);
        foreach (Control control in this.GetLogicalDescendants().OfType<Control>())
        {
            if (control is TextBlock text)
            {
                double size = text.Classes.Contains("chat-heading") ? 17 : text.Classes.Contains("chat-prose") ? 15
                    : text.Classes.Contains("chat-code") ? 13 : 12;
                double leading = text.Classes.Contains("chat-heading") ? 27 : text.Classes.Contains("chat-prose") ? 24
                    : text.Classes.Contains("chat-code") ? 20 : 18;
                text.FontSize = size * scale;
                text.LineHeight = leading * scale;
            }
            else if (control is Button button) button.FontSize = 12 * scale;
        }
    }
}
