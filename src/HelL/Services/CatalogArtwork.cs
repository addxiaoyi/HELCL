using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;

namespace HelL.Services;

public sealed class CatalogArtwork : ContentControl
{
    private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4);
    private readonly ProjectCover _icon;

    public CatalogArtwork(CatalogItem project, double size = 64)
    {
        _icon = new ProjectCover(project.Name, size, size, icon: true);
        Content = _icon;
        _ = Load(project);
    }

    private async Task Load(CatalogItem project)
    {
        string icon = CatalogPresentation.ImageUrl(project.IconUrl);
        if (icon.Length == 0 && (project.IdValue != null || !string.IsNullOrEmpty(project.Slug)))
        {
            await Gate.WaitAsync();
            try
            {
                var detail = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new
                { source = project.Source, slug = project.Slug ?? "", id = project.IdValue?.ToString() ?? "" });
                icon = CatalogPresentation.ImageUrl(detail?.IconUrl);
                project.IconUrl = icon;
            }
            catch (Exception ex) { Smoke.Note("catalog-artwork", ex.Message); }
            finally { Gate.Release(); }
        }
        await _icon.Load(icon);
    }
}
