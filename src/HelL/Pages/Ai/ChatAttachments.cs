using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages.Ai;

internal sealed class ChatAttachments : ContentControl
{
    private readonly SPanel _tiles = Ui.H(8);
    private readonly TextBlock _notice = Ui.Small("").Wrap();
    private readonly Dictionary<string, FrameworkElement> _previews = new(StringComparer.OrdinalIgnoreCase);
    internal event Action<string>? Removed;

    internal string Notice
    {
        set { _notice.Text = value; _notice.Visibility = value.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }
    }

    internal ChatAttachments()
    {
        _tiles.Wrap = true;
        _notice.SetResourceReference(TextBlock.ForegroundProperty, "B.Warn");
        _notice.Visibility = Visibility.Collapsed;
        Content = Ui.V(6, _tiles, _notice);
        Visibility = Visibility.Collapsed;
    }

    internal void Update(IReadOnlyList<string> paths)
    {
        var selected = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (string old in _previews.Keys.Where(path => !selected.Contains(path)).ToArray())
        {
            _tiles.Children.Remove(_previews[old]);
            _previews.Remove(old);
        }
        foreach (string path in paths)
        {
            if (_previews.ContainsKey(path)) continue;
            string name = Path.GetFileName(path);
            var picture = new LocalPicture(80, 58, I18n.L("图片暂不可用"), contain: true, decodeWidth: 160);
            _ = picture.SetPath(path, Path.GetDirectoryName(path)!, name);
            var open = Ui.Btn("", BtnKind.Ghost, (_, _) => Show(path));
            open.Content = picture; open.Padding = new Thickness(0); open.ToolTip = name;
            AutomationProperties.SetName(open, I18n.L("预览附件 {0}", name));
            var remove = Ui.IconBtn("\ue711", I18n.L("移除附件 {0}", name), (_, _) => Removed?.Invoke(path));
            remove.Width = remove.Height = 24; remove.MinWidth = remove.MinHeight = 24;
            remove.Padding = new Thickness(0); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top;
            remove.SetResourceReference(BackgroundProperty, "B.Popup");
            var tile = new Grid { Width = 80, Children = { open, remove } };
            _previews.Add(path, tile); _tiles.Children.Add(tile);
        }
        Visibility = paths.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (paths.Count == 0) Notice = "";
    }

    private static void Show(string path)
    {
        string name = Path.GetFileName(path);
        var picture = new LocalPicture(double.NaN, Math.Max(120, Math.Min(450, (AppServices.Window?.ActualHeight ?? 740) - 220)),
            I18n.L("图片暂不可用"), contain: true, decodeWidth: 1600);
        _ = picture.SetPath(path, Path.GetDirectoryName(path)!, name);
        Dlg.Panel(name, picture, 850);
    }
}
