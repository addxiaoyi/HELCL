using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public sealed class DashboardBox
{
    public string Id { get; }
    public double X, Y, Width, Height;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public DashboardBox(string id, double x, double y, double width, double height) { Id = id; X = x; Y = y; Width = width; Height = height; }
    public DashboardBox Copy() => new DashboardBox(Id, X, Y, Width, Height);
    public bool Overlaps(DashboardBox other, double gap = 12) => X < other.Right + gap - 0.5 && Right + gap > other.X + 0.5 && Y < other.Bottom + gap - 0.5 && Bottom + gap > other.Y + 0.5;
}

public static class DashboardGeometry
{
    public const double ReferenceHeight = 800;
    public const double Gap = 12;
    public const double MagnetDistance = 10;

    public static List<DashboardBox> Arrange(IEnumerable<LayoutItem> cards, double width, Func<string, (double W, double H)> minimum, string active = "")
    {
        width = Math.Max(1, width); var placed = new List<DashboardBox>();
        foreach (var card in cards.Where(card => !card.hidden).OrderBy(card => card.id == active ? 0 : 1).ThenBy(card => card.y).ThenBy(card => card.x).ThenBy(card => card.z))
        {
            var min = minimum(card.type);
            double w = width < 720 ? width : Math.Min(width, Math.Max(min.W, card.w * width)), h = Math.Max(min.H, card.h * ReferenceHeight);
            var box = new DashboardBox(card.id, Math.Max(0, Math.Min(card.x * width, width - w)), Math.Max(0, card.y * ReferenceHeight), w, h);
            for (int attempt = 0; attempt <= placed.Count; attempt++)
            {
                var collisions = placed.Where(other => box.Overlaps(other)).ToList();
                if (collisions.Count == 0) break;
                box.Y = collisions.Max(other => other.Bottom) + Gap;
            }
            placed.Add(box);
        }
        return placed;
    }

    public static DashboardBox Snap(DashboardBox proposed, IEnumerable<DashboardBox> others, double width, int grid, string handle, out double? guideX, out double? guideY)
    {
        var box = proposed.Copy(); guideX = guideY = null;
        if (grid <= 0) return Bound(box, width);
        var xs = new List<double> { 0, width / 2, width }; var ys = new List<double> { 0 };
        foreach (var peer in others.Where(other => other.Id != box.Id))
        {
            xs.AddRange(new[] { peer.X, peer.Right, peer.X + peer.Width / 2, peer.Right + Gap, peer.X - Gap });
            ys.AddRange(new[] { peer.Y, peer.Bottom, peer.Y + peer.Height / 2, peer.Bottom + Gap, peer.Y - Gap });
        }
        if (handle == "move")
        {
            double dx = Nearest(new[] { box.X, box.Right, box.X + box.Width / 2 }, xs, out guideX);
            double dy = Nearest(new[] { box.Y, box.Bottom, box.Y + box.Height / 2 }, ys, out guideY);
            box.X = guideX.HasValue ? box.X + dx : Math.Round(box.X / grid) * grid;
            box.Y = guideY.HasValue ? box.Y + dy : Math.Round(box.Y / grid) * grid;
        }
        else
        {
            if (handle.Contains("w")) { double edge = Edge(box.X, xs, grid, out guideX); box.Width += box.X - edge; box.X = edge; }
            if (handle.Contains("e")) box.Width = Edge(box.Right, xs, grid, out guideX) - box.X;
            if (handle.Contains("n")) { double edge = Edge(box.Y, ys, grid, out guideY); box.Height += box.Y - edge; box.Y = edge; }
            if (handle.Contains("s")) box.Height = Edge(box.Bottom, ys, grid, out guideY) - box.Y;
        }
        return Bound(box, width);
    }
    private static double Edge(double value, List<double> targets, int grid, out double? guide)
    { double delta = Nearest(new[] { value }, targets, out guide); return guide.HasValue ? value + delta : Math.Round(value / grid) * grid; }
    private static double Nearest(IEnumerable<double> edges, IEnumerable<double> targets, out double? guide)
    {
        guide = null; double nearest = MagnetDistance + 0.01, delta = 0;
        foreach (double edge in edges) foreach (double target in targets)
            if (Math.Abs(target - edge) < nearest) { nearest = Math.Abs(target - edge); delta = target - edge; guide = target; }
        return delta;
    }
    private static DashboardBox Bound(DashboardBox box, double width)
    {
        box.Width = Math.Min(Math.Max(1, width), Math.Max(40, box.Width)); box.Height = Math.Max(40, box.Height);
        box.X = Math.Max(0, Math.Min(width - box.Width, box.X)); box.Y = Math.Max(0, Math.Min(ReferenceHeight * 20, box.Y)); return box;
    }
}
