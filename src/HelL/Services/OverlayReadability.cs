using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace HelL.Services;

internal static class OverlayReadability
{
    private static readonly Dictionary<object, UIElement[]> Owners = new();
    private static readonly Dictionary<UIElement, (double Opacity, int Count)> Hidden = new();

    public static void Cover(object owner, bool hide, bool dialogs = false)
    {
        var targets = new List<UIElement>();
        if (AppServices.Window?.FindName("PageHost") is UIElement page) targets.Add(page);
        if (dialogs && Dlg.Host != null) targets.AddRange(Dlg.Host.Children.Cast<UIElement>());
        CoverElements(owner, hide, targets);
    }

    public static void CoverElements(object owner, bool hide, IEnumerable<UIElement> elements)
    {
        if (hide)
        {
            if (Owners.ContainsKey(owner)) return;
            var targets = elements.Distinct().ToArray(); Owners[owner] = targets;
            foreach (var target in targets)
            {
                Hidden.TryGetValue(target, out var prior);
                Hidden[target] = (prior.Count == 0 ? target.Opacity : prior.Opacity, prior.Count + 1);
                target.Opacity = 0;
            }
            return;
        }

        // A submenu can close after its parent; only the last overlay restores the page.
        if (!Owners.TryGetValue(owner, out var covered)) return;
        Owners.Remove(owner);
        foreach (var target in covered)
        {
            var prior = Hidden[target];
            if (prior.Count > 1) Hidden[target] = (prior.Opacity, prior.Count - 1);
            else { target.Opacity = prior.Opacity; Hidden.Remove(target); }
        }
    }
}
