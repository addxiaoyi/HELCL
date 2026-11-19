using System.Windows;
using System.Windows.Controls;
using HelL.Models;

namespace HelL.Services;

public sealed class ResourceTitle : ContentControl
{
    private readonly TextBlock _name;
    private readonly TextBlock _original = Ui.Small("").Wrap();
    public ResourceTitle(CatalogItem item, double size = 16)
    {
        _name = Ui.Txt("", size, true).Wrap();
        _name.MaxHeight = size * 3; _name.TextTrimming = TextTrimming.CharacterEllipsis;
        _original.MaxHeight = UiMetrics.CaptionLine * 2;
        _original.TextTrimming = TextTrimming.CharacterEllipsis;
        Content = Ui.V(2, _name, _original);
        Apply(item);
    }
    public void Apply(CatalogItem item)
    {
        _name.Text = item.DisplayName;
        _name.ToolTip = item.DisplayName;
        _original.Text = item.Name;
        _original.Visibility = item.DisplayName == item.Name ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = item.NameLocalization.Length == 0 ? item.Name : item.Name + "\n" + item.NameLocalization;
    }
}
