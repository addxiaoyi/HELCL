using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop.Views;

public partial class PlaytimeView : UserControl
{
    public sealed record PlaytimeRankRow(int Rank, string InstanceName, string DurationText, double SharePercent, string SubDetail)
    {
        public string RankLabel => $"{Rank:00}";
    }

    public PlaytimeView() => InitializeComponent();

    internal void Reload()
    {
        long totalSeconds = Py.Int(Playtime.GetTotalPlaytime());
        TotalDurationLabel.Text = Playtime.FormatDuration(totalSeconds);

        var all = Playtime.GetAllPlaytime() as PyDict ?? new PyDict();
        long totalSessions = 0;
        var rows = new List<(string Name, long Seconds, int Sessions, string LatestVer)>();

        foreach (var kv in all)
        {
            if (kv.Value is not PyDict entry) continue;
            long sec = Py.Int(entry.Get("total", 0L));
            var sessions = entry.Get("sessions") as List<object> ?? [];
            totalSessions += sessions.Count;
            string lastVer = sessions.LastOrDefault() is PyDict s ? Py.S(s.Get("version")) : "";
            if (sec > 0 || sessions.Count > 0)
                rows.Add((kv.Key, sec, sessions.Count, lastVer));
        }

        InstanceCountLabel.Text = $"{rows.Count} 个";
        SessionCountLabel.Text = $"{totalSessions} 场";

        long maxSec = Math.Max(1, rows.Select(r => r.Seconds).DefaultIfEmpty(1).Max());
        var ordered = rows.OrderByDescending(r => r.Seconds).Select((r, idx) =>
        {
            double pct = Math.Clamp(100.0 * r.Seconds / maxSec, 4, 100);
            string detail = string.IsNullOrWhiteSpace(r.LatestVer)
                ? $"启动 {r.Sessions} 次"
                : $"启动 {r.Sessions} 次 · {r.LatestVer}";
            return new PlaytimeRankRow(idx + 1, r.Name, Playtime.FormatDuration(r.Seconds), pct, detail);
        }).ToArray();

        RankList.ItemsSource = ordered;
        EmptyStats.IsVisible = ordered.Length == 0;
    }

    private void RefreshStats(object? sender, RoutedEventArgs e) => Reload();
}
