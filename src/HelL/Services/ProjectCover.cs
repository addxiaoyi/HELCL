using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Core.Launcher;

namespace HelL.Services;

public sealed class ProjectCover : Grid
{
    private readonly Image _image = new Image { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _fallback = Ui.Txt("", 12, false, "B.InkMuted");
    private int _generation;
    private readonly bool _icon;
    public bool Ready => _image.Source != null;

    public ProjectCover(string title, double width, double height, bool icon = false)
    {
        _icon = icon;
        if (icon) { _image.Stretch = Stretch.Uniform; _fallback.FontSize = 10; }
        Width = width; Height = height;
        _fallback.Text = I18n.L(icon ? "暂无图标" : "暂无封面");
        _fallback.HorizontalAlignment = HorizontalAlignment.Center;
        _fallback.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_fallback); Children.Add(_image);
        SetResourceReference(BackgroundProperty, "B.Paper2");
        System.Windows.Automation.AutomationProperties.SetName(this, title);
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 7, 7);
    }

    public async Task Load(string url)
    {
        int generation = ++_generation;
        _image.Source = null;
        _fallback.Visibility = Visibility.Visible;
        _fallback.Text = I18n.L(_icon ? "暂无图标" : "暂无封面");
        url = CatalogPresentation.ImageUrl(url);
        if (url.Length == 0) return;
        _fallback.Text = I18n.L(_icon ? "加载中" : "正在加载封面…");
        try
        {
            var bitmap = await Thumbs.LoadAsync(url, _icon ? Math.Max(16, (int)Width) : 600);
            if (generation != _generation) return;
            _image.Source = bitmap;
            _fallback.Visibility = bitmap == null ? Visibility.Visible : Visibility.Collapsed;
            _fallback.Text = I18n.L(_icon ? "图标暂不可用" : "封面暂不可用");
        }
        catch (Exception ex) { if (generation == _generation) _fallback.Text = I18n.L(_icon ? "图标暂不可用" : "封面暂不可用"); Smoke.Note("project-cover", ex.Message); }
    }
}
