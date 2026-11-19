using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;

namespace HelL.Services;

public sealed class ProjectGallery : ContentControl
{
    private readonly ProjectCover _cover;
    private readonly TextBlock _caption = Ui.Small("").Wrap();
    private readonly List<CatalogImage> _images;
    private readonly Button _previous;
    private readonly Button _next;
    private int _index;
    public int Index => _index;
    public int Count => _images.Count;

    public ProjectGallery(List<CatalogImage> images, string title)
    {
        _images = images.Where(image => Core.Launcher.CatalogPresentation.ImageUrl(image.Url).Length > 0).ToList();
        _cover = new ProjectCover(title, double.NaN, 230);
        _previous = Ui.Btn(I18n.L("上一张"), BtnKind.Ghost, (_, _) => { _index--; Update(); }, "\ue76b");
        _next = Ui.Btn(I18n.L("下一张"), BtnKind.Ghost, (_, _) => { _index++; Update(); }, "\ue76c");
        var footer = Ui.G(null, "Auto,*,Auto"); footer.Add(_previous, 0); footer.Add(_caption.M(10, 0, 10, 0).VCenter(), 0, 1); footer.Add(_next, 0, 2);
        Content = Ui.V(6, _cover, footer);
        Visibility = _images.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Update();
    }

    private void Update()
    {
        if (_images.Count == 0) return;
        _index = Math.Max(0, Math.Min(_index, _images.Count - 1));
        var image = _images[_index];
        _caption.Text = (_index + 1) + " / " + _images.Count + (image.Title.Length > 0 ? " · " + image.Title : "");
        _previous.IsEnabled = _index > 0; _next.IsEnabled = _index + 1 < _images.Count;
        _ = _cover.Load(image.Url);
    }
}
