using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Services;

namespace HelL.Pages.Ai;

public sealed class MinecraftDashboard : ContentControl
{
    private readonly ContentControl _view = new ContentControl();
    private JsonElement _snapshot;
    private int _tab;
    public event Action<JsonElement>? RetryRequested;

    public MinecraftDashboard()
    {
        Content = Ui.V(8, Ui.Segmented(new[] { I18n.L("附近地形"), I18n.L("背包"), I18n.L("动作记录") }, selected => { _tab = selected; Render(); }), _view);
        Render();
    }
    public void Update(JsonElement state) { _snapshot = state.Clone(); Render(); }
    private void Render()
    {
        if (_snapshot.ValueKind != JsonValueKind.Object) { _view.Content = Ui.Muted(I18n.L("连接游戏后显示实时状态。")); return; }
        if (_tab == 0)
        {
            if (!_snapshot.TryGetProperty("map", out var map) || map.ValueKind != JsonValueKind.Object)
            { _view.Content = Ui.Muted(I18n.L("尚未取得地形；未知区块不会推测。")); return; }
            var layout = Ui.G(null, "Auto,*");
            layout.Add(new TerrainMap(map) { Width = 204, Height = 204 }, 0);
            layout.Add(Ui.V(7, Ui.Txt(I18n.L("以 AI 玩家为中心 · 北向上"), 12, true).Wrap(),
                Ui.Small(I18n.L("绿色：草地与树木\n蓝色：水\n灰色：实地\n橙色：岩浆\n深色：未知\n白点：AI 玩家")).Wrap(),
                Ui.Small(I18n.L("仅显示附近 17×17 格、已加载且在有限高度内的方块。")).Wrap()).M(14, 0, 0, 0), 0, 1);
            _view.Content = layout; return;
        }
        var rows = Ui.V(5);
        string key = _tab == 1 ? "inventory" : "history";
        if (_snapshot.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var entries = items.EnumerateArray().ToList();
            if (_tab == 2) entries.Reverse();
            foreach (var item in entries.Take(50))
            {
                string text = _tab == 1 ? Text(item, "name") + "  × " + Text(item, "count")
                    : ActionName(Text(item, "action")) + " · " + StateName(Text(item, "state")) + " · " + Time(Text(item, "at"));
                var row = Ui.Txt(text, 12).Wrap();
                if (_tab == 1) { rows.Children.Add(row); continue; }
                var receipt = item.Clone();
                var detail = Ui.Small(I18n.L(Text(item, "detail"))).Wrap();
                var content = Ui.V(3, row, detail);
                bool authorized = _snapshot.TryGetProperty("tools_enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
                if (authorized && item.TryGetProperty("retryable", out var retryable) && retryable.ValueKind == JsonValueKind.True)
                    content.Children.Add(Ui.Btn(I18n.L("确认后重试"), BtnKind.Ghost, (_, _) => RetryRequested?.Invoke(receipt)).Left());
                rows.Children.Add(content);
            }
        }
        if (rows.Children.Count == 0) rows.Children.Add(Ui.Muted(I18n.L(_tab == 1 ? "背包为空或尚未连接。" : "还没有执行动作。")));
        if (_tab == 2 && _snapshot.TryGetProperty("history_warning", out var warning)) rows.Children.Add(Ui.Muted(I18n.L(warning.ToString())).Wrap());
        var scroll = Ui.Scroll(rows); scroll.MaxHeight = 204; _view.Content = scroll;
    }
    private static string ActionName(string name) => I18n.L(name switch { "follow_player" => "跟随玩家", "collect_wood" => "采集天然原木", "pickup_items" => "拾取掉落物", "move_to" => "移动 AI 玩家", "dig" => "挖掘方块", "place" => "放置方块", "craft" => "合成物品", "equip" => "装备物品", "look_at" => "调整 AI 玩家视角", "consume" => "食用物品", "interact" => "使用方块", _ => name });
    private static string StateName(string state) => I18n.L(state switch { "running" => "执行中", "completed" => "已完成", "failed" => "失败", "stopped" => "已停止", _ => state });
    private static string Time(string time) => DateTimeOffset.TryParse(time, out var stamp) ? stamp.ToLocalTime().ToString("HH:mm:ss") : "";
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) ? property.ToString() : "";

    private sealed class TerrainMap : FrameworkElement
    {
        private readonly JsonElement _map;
        private static readonly Dictionary<string, Brush> Colors = new Dictionary<string, Brush> {
            ["grass"] = new SolidColorBrush(Color.FromRgb(91, 139, 91)), ["tree"] = new SolidColorBrush(Color.FromRgb(46, 89, 57)),
            ["water"] = new SolidColorBrush(Color.FromRgb(66, 118, 169)), ["lava"] = new SolidColorBrush(Color.FromRgb(213, 115, 50)),
            ["sand"] = new SolidColorBrush(Color.FromRgb(189, 174, 124)), ["snow"] = Brushes.Gainsboro,
            ["solid"] = new SolidColorBrush(Color.FromRgb(130, 132, 126)), ["unknown"] = new SolidColorBrush(Color.FromRgb(48, 50, 52)) };
        public TerrainMap(JsonElement map) { _map = map.Clone(); System.Windows.Automation.AutomationProperties.SetName(this, I18n.L("附近 17×17 格地形，北向上")); }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            var center = _map.GetProperty("center"); int cx = center.GetProperty("x").GetInt32(), cz = center.GetProperty("z").GetInt32();
            const int radius = 8; double cell = Math.Min(ActualWidth, ActualHeight) / 17;
            foreach (var tile in _map.GetProperty("tiles").EnumerateArray())
            {
                int x = tile.GetProperty("x").GetInt32() - cx + radius, z = tile.GetProperty("z").GetInt32() - cz + radius;
                if (x < 0 || x > 16 || z < 0 || z > 16) continue;
                string kind = tile.GetProperty("kind").GetString() ?? "unknown";
                drawing.DrawRectangle(Colors.TryGetValue(kind, out var brush) ? brush : Colors["unknown"], null, new Rect(x * cell, z * cell, cell + 0.2, cell + 0.2));
            }
            drawing.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 1), new Point(8.5 * cell, 8.5 * cell), 4, 4);
        }
    }
}
