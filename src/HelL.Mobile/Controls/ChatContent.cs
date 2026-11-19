using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;

namespace HelL.Mobile.Controls;

public sealed class ChatContent : StackPanel
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ChatContent, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    static ChatContent() => TextProperty.Changed.AddClassHandler<ChatContent>((view, _) => view.Render());
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
                    Children.Add(new SelectableTextBlock { Text = readable, TextWrapping = TextWrapping.Wrap, LineHeight = 22,
                        FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal });
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
            string code = newline >= 0 ? value[(newline + 1)..].TrimEnd() : value;
            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(new TextBlock { Text = language.Length == 0 ? "代码" : language, Classes = { "caption" } });
            block.Children.Add(new SelectableTextBlock { Text = code, FontFamily = FontFamily.Parse("monospace"), TextWrapping = TextWrapping.Wrap });
            var copy = new Button { Content = "复制代码", Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 44 };
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
            block.Children.Add(copy);
            Children.Add(new Border { Classes = { "glass" }, Child = block });
        }
    }
}
