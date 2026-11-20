using System;
using System.Collections.Generic;

namespace HelL.Design;

/// <summary>Shared outline drawings in a 24-unit frame; aliases keep existing client markup compatible.</summary>
public static class IconCatalog
{
    public const double Frame = 24;
    public const double StrokeWidth = 1.8;
    public const double ActiveStrokeWidth = 2.25;

    private static readonly Dictionary<string, string> Drawings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Play"] = "M9.5 7.5L17 12l-7.5 4.5z M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z",
        ["Library"] = "M3 7.5V6a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7.5z M3 9h18 M8 13v3 M12 13v3 M16 13v3",
        ["Download"] = "M12 3.5v11 M7.5 10.5L12 15l4.5-4.5 M4 16v3a1.5 1.5 0 0 0 1.5 1.5h13A1.5 1.5 0 0 0 20 19v-3",
        ["Explore"] = "M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z M15.5 8.5l-2 5-5 2 2-5z",
        ["Puzzle"] = "M8.5 3.5H5a1.5 1.5 0 0 0-1.5 1.5v4h1a2.5 2.5 0 1 1 0 5h-1v4A1.5 1.5 0 0 0 5 19.5h4v-1a2.5 2.5 0 1 1 5 0v1h4a1.5 1.5 0 0 0 1.5-1.5v-4h-1a2.5 2.5 0 1 1 0-5h1V5A1.5 1.5 0 0 0 18 3.5h-4v1a2.5 2.5 0 1 1-5 0v-1z",
        ["World"] = "M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z M3 12h18 M12 3c-5 5-5 13 0 18 M12 3c5 5 5 13 0 18",
        ["Server"] = "M5 3.5h14A1.5 1.5 0 0 1 20.5 5v4A1.5 1.5 0 0 1 19 10.5H5A1.5 1.5 0 0 1 3.5 9V5A1.5 1.5 0 0 1 5 3.5z M5 13.5h14a1.5 1.5 0 0 1 1.5 1.5v4a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 19v-4A1.5 1.5 0 0 1 5 13.5z M7.5 7h.01 M7.5 17h.01 M15 7h2 M15 17h2",
        ["Clock"] = "M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z M12 6.5V12l3.5 2",
        ["Java"] = "M5 9h12v6a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4z M17 10h1.5a2.5 2.5 0 0 1 0 5H17 M4 21h15 M8 3v2 M12 2v3 M16 3v2",
        ["Chat"] = "M5 4.5h14A1.5 1.5 0 0 1 20.5 6v10a1.5 1.5 0 0 1-1.5 1.5h-8l-5 3v-3H5A1.5 1.5 0 0 1 3.5 16V6A1.5 1.5 0 0 1 5 4.5z M7.5 9h9 M7.5 13h5.5",
        ["Settings"] = "M9 2.5h6l.5 2.7 1.5.9 2.6-.8L22 9.5 20 12l2 2.5-2.4 4.2-2.6-.8-1.5.9-.5 2.7H9l-.5-2.7-1.5-.9-2.6.8L2 14.5 4 12 2 9.5l2.4-4.2 2.6.8 1.5-.9z M15.5 12a3.5 3.5 0 1 1-7 0a3.5 3.5 0 1 1 7 0z",
        ["Tasks"] = "M10 5h10 M10 12h10 M10 19h10 M3.5 4.5L5 6l2.5-3 M3.5 11.5L5 13l2.5-3 M3.5 18.5L5 20l2.5-3",
        ["Account"] = "M16 7.5a4 4 0 1 1-8 0a4 4 0 1 1 8 0z M4.5 20.5V19a7.5 5.5 0 0 1 15 0v1.5",
        ["Info"] = "M21 12a9 9 0 1 1-18 0a9 9 0 1 1 18 0z M12 10.5v6 M12 7.5h.01",
        ["Search"] = "M17 10.5a6.5 6.5 0 1 1-13 0a6.5 6.5 0 1 1 13 0z M15.5 15.5L21 21",
        ["Plus"] = "M12 4.5v15 M4.5 12h15",
        ["Close"] = "M5.5 5.5l13 13 M18.5 5.5l-13 13",
        ["Check"] = "M4.5 12l5 5 10-10",
        ["Pin"] = "M8 3.5h8 M9 3.5V9l-3.5 4v2h13v-2L15 9V3.5 M12 15v6",
        ["Refresh"] = "M20 8a8.5 8.5 0 1 0 .5 7 M20 3.5V8h-4.5",
        ["Folder"] = "M3.5 8V6A1.5 1.5 0 0 1 5 4.5h4l2 2h8A1.5 1.5 0 0 1 20.5 8v10A1.5 1.5 0 0 1 19 19.5H5A1.5 1.5 0 0 1 3.5 18z M3.5 9h17",
        ["Trash"] = "M3.5 6.5h17 M8.5 6.5v-2h7v2 M6 6.5l1 14h10l1-14 M10 10.5v6 M14 10.5v6",
        ["Save"] = "M5 3.5h12l3.5 3.5v12a1.5 1.5 0 0 1-1.5 1.5H5A1.5 1.5 0 0 1 3.5 19V5A1.5 1.5 0 0 1 5 3.5z M8 3.5v6h8v-6 M8 20.5v-7h8v7",
        ["Copy"] = "M9 8.5h10A1.5 1.5 0 0 1 20.5 10v9a1.5 1.5 0 0 1-1.5 1.5H9A1.5 1.5 0 0 1 7.5 19v-9A1.5 1.5 0 0 1 9 8.5z M16.5 5.5V5A1.5 1.5 0 0 0 15 3.5H5A1.5 1.5 0 0 0 3.5 5v10A1.5 1.5 0 0 0 5 16.5",
        ["Share"] = "M8 10l8-4 M8 14l8 4 M8.5 12a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z M21 5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z M21 19a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z",
        ["ChevronDown"] = "M6 9l6 6 6-6",
        ["ChevronRight"] = "M9 5.5l6.5 6.5L9 18.5",
        ["ChevronLeft"] = "M15 5.5L8.5 12l6.5 6.5",
        ["ArrowUp"] = "M12 20V4 M6 10l6-6 6 6",
        ["ArrowDown"] = "M12 4v16 M6 14l6 6 6-6",
        ["Minimize"] = "M5 12h14",
        ["Maximize"] = "M5 5h14v14H5z",
        ["Restore"] = "M8 5V3.5h12.5V16H19 M3.5 8H16v12.5H3.5z",
        ["Grid"] = "M4 4h6v6H4z M14 4h6v6h-6z M4 14h6v6H4z M14 14h6v6h-6z",
        ["Cube"] = "M12 3l8.5 4.5v9L12 21l-8.5-4.5v-9z M3.5 7.5L12 12l8.5-4.5 M12 12v9 M8 5.1l8.5 4.5",
        ["Layers"] = "M3.5 7L12 3l8.5 4-8.5 4z M3.5 12L12 16l8.5-4 M3.5 17l8.5 4 8.5-4",
        ["Sun"] = "M16.5 12a4.5 4.5 0 1 1-9 0a4.5 4.5 0 1 1 9 0z M12 2v2 M12 20v2 M2 12h2 M20 12h2 M5 5l1.5 1.5 M17.5 17.5L19 19 M5 19l1.5-1.5 M17.5 6.5L19 5",
        ["Moon"] = "M20.5 14a8.7 8.7 0 0 1-10.5-10.5A8.8 8.8 0 1 0 20.5 14z",
        ["Filter"] = "M3.5 6h17 M3.5 12h17 M3.5 18h17 M8 3.5v5 M16 9.5v5 M9 15.5v5",
        ["More"] = "M5.5 12h.01 M12 12h.01 M18.5 12h.01",
        ["Lightning"] = "M13.5 2.5L5 13h6l-.5 8.5L19 11h-6z",
        ["Trend"] = "M3.5 18L10 11l4 4 6.5-9 M15 6h5.5v5.5",
        ["Speed"] = "M4.5 18a9 9 0 1 1 15 0 M6.5 17h11 M12 14l4-6 M5 10h1.5 M11 4.5V6 M18 10h1.5",
        ["Upload"] = "M12 16V4 M7.5 8.5L12 4l4.5 4.5 M4 16v3a1.5 1.5 0 0 0 1.5 1.5h13A1.5 1.5 0 0 0 20 19v-3",
        ["Edit"] = "M4 20l4.5-1 12-12a2.1 2.1 0 0 0-3-3l-12 12z M14.5 7l3 3 M13 20h7",
        ["Link"] = "M9 15l6-6 M8 16l-1 1a3 3 0 0 1-4-4l4-4a3 3 0 0 1 4 0 M16 8l1-1a3 3 0 0 1 4 4l-4 4a3 3 0 0 1-4 0",
        ["Shield"] = "M12 3l8 3v6c0 4.5-3.5 7.5-8 9-4.5-1.5-8-4.5-8-9V6z M8.5 12l2.5 2.5 4.5-5",
        ["Eye"] = "M2.5 12s3.5-6.5 9.5-6.5 9.5 6.5 9.5 6.5-3.5 6.5-9.5 6.5S2.5 12 2.5 12z M15 12a3 3 0 1 1-6 0a3 3 0 1 1 6 0z",
        ["EyeOff"] = "M3 3l18 18 M9 5.9A10 10 0 0 1 12 5.5c6 0 9.5 6.5 9.5 6.5a18 18 0 0 1-3 3.7 M14.5 18.1a10 10 0 0 1-2.5.4C6 18.5 2.5 12 2.5 12a18 18 0 0 1 3-3.7 M9.9 9.9a3 3 0 0 0 4.2 4.2",
        ["File"] = "M6 3.5h8L20 9v10a1.5 1.5 0 0 1-1.5 1.5H6A1.5 1.5 0 0 1 4.5 19V5A1.5 1.5 0 0 1 6 3.5z M14 3.5V9h6 M8.5 13h7 M8.5 17h5",
        ["External"] = "M14 3.5h6.5V10 M20 4l-9 9 M10 5H5A1.5 1.5 0 0 0 3.5 6.5V19A1.5 1.5 0 0 0 5 20.5h12.5A1.5 1.5 0 0 0 19 19v-5",
        ["Undo"] = "M8 4L3.5 8.5 8 13 M4 8.5h10a6 6 0 0 1 0 12h-3",
        ["Warning"] = "M10.3 4.5a2 2 0 0 1 3.4 0l8 14a1.5 1.5 0 0 1-1.3 2.3H3.6a1.5 1.5 0 0 1-1.3-2.3z M12 9v5 M12 17h.01",
        ["Bell"] = "M5.5 9a6.5 6.5 0 0 1 13 0v5l2 3h-17l2-3z M9.5 20h5 M12 2v1",
        ["Network"] = "M8.5 12a3.5 3.5 0 1 1-7 0a3.5 3.5 0 1 1 7 0z M22.5 12a3.5 3.5 0 1 1-7 0a3.5 3.5 0 1 1 7 0z M8.5 12h7 M5 5V3.5h14V5 M5 19v1.5h14V19",
        ["Menu"] = "M4 6h16 M4 12h16 M4 18h16",
        ["Palette"] = "M12 3a9 9 0 1 0 0 18h1.2a2.3 2.3 0 0 0 1.7-3.8 1.5 1.5 0 0 1 1.1-2.5H18a3 3 0 0 0 3-3C21 6.7 17 3 12 3z M7.5 8h.01 M12 6.5h.01 M16.5 8h.01 M6.5 12.5h.01",
        ["Send"] = "M3 4l18 8-18 8 3-8z M6 12h15",
        ["Star"] = "M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9z",
        ["Broom"] = "M14 10l6-6 M12 8l4 4-6 8-7-7z M6 16l5-5 M9 19l5-5 M3 21h17",
        ["Stop"] = "M6 5h12a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z",
        ["Pause"] = "M7 5v14 M17 5v14",
        ["Bug"] = "M8 7h8v8a4 4 0 0 1-8 0z M9 7V5l-2-2 M15 7V5l2-2 M4 9l4 2 M20 9l-4 2 M3 15h5 M16 15h5 M5 21l4-3 M19 21l-4-3 M12 11v7",
        ["FolderPlus"] = "M3.5 8V6A1.5 1.5 0 0 1 5 4.5h4l2 2h8A1.5 1.5 0 0 1 20.5 8v10A1.5 1.5 0 0 1 19 19.5H5A1.5 1.5 0 0 1 3.5 18z M3.5 9h17 M12 11.5v5 M9.5 14h5",
        ["Wand"] = "M3.5 18.5l12-12 3 3-12 12z M13.5 8.5l3 3 M6 3v4 M4 5h4 M17 2v3 M15.5 3.5h3 M20.5 16v4 M18.5 18h4",
        ["ChevronUp"] = "M6 15l6-6 6 6"
    };

    private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PlayCircleOutline"] = "Play", ["Home"] = "Play", ["HomeOutline"] = "Play",
        ["FolderOutline"] = "Library", ["ViewGrid"] = "Library", ["ViewGridOutline"] = "Library",
        ["ArrowDownCircleOutline"] = "Download", ["DownloadOutline"] = "Download",
        ["CompassOutline"] = "Explore", ["PuzzleOutline"] = "Puzzle", ["Earth"] = "World",
        ["ServerNetwork"] = "Server", ["ClockOutline"] = "Clock", ["CoffeeOutline"] = "Java",
        ["CreationOutline"] = "Chat", ["ChatOutline"] = "Chat", ["AccountOutline"] = "Account",
        ["CogOutline"] = "Settings", ["CogSyncOutline"] = "Settings", ["TuneVariant"] = "Filter",
        ["FormatListChecks"] = "Tasks", ["InformationOutline"] = "Info", ["Magnify"] = "Search",
        ["CubeOutline"] = "Cube", ["LayersTripleOutline"] = "Layers", ["GridLarge"] = "Grid",
        ["DotsHorizontal"] = "More", ["FlashOutline"] = "Lightning", ["TrendingUp"] = "Trend",
        ["Speedometer"] = "Speed", ["WhiteBalanceSunny"] = "Sun", ["TrashCanOutline"] = "Trash",
        ["WindowClose"] = "Close", ["WindowMaximize"] = "Maximize", ["WindowMinimize"] = "Minimize",
        ["WindowRestore"] = "Restore", ["BellOutline"] = "Bell"
    };

    private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["\ue7c1"] = "Play", ["\ue896"] = "Download", ["\ue99a"] = "Chat", ["\ue712"] = "More",
        ["\ue9d5"] = "Tasks", ["\ue7fc"] = "Cube", ["\uea86"] = "Puzzle", ["\ue7b8"] = "Layers",
        ["\ue8f1"] = "File", ["\ueb9f"] = "Cube", ["\ue706"] = "Sun", ["\ue909"] = "World",
        ["\uec32"] = "Java", ["\ue80a"] = "Library", ["\ue8fd"] = "Layers", ["\ue77b"] = "Account",
        ["\ue701"] = "Network", ["\ue968"] = "Server", ["\ue823"] = "Clock", ["\ue8bd"] = "Chat",
        ["\ue713"] = "Settings", ["\ue710"] = "Plus", ["\ue711"] = "Close", ["\ue73e"] = "Check",
        ["\ue718"] = "Pin", ["\ue840"] = "Pin", ["\ue72c"] = "Refresh", ["\ue8b7"] = "Folder",
        ["\ue74d"] = "Trash", ["\ue74e"] = "Save", ["\ue8c8"] = "Copy", ["\ue7ba"] = "Warning",
        ["\ue8b5"] = "Download", ["\ue70d"] = "ChevronDown", ["\ue76c"] = "ChevronRight",
        ["\ue921"] = "Minimize", ["\ue922"] = "Maximize", ["\ue923"] = "Restore", ["\ue8bb"] = "Close",
        ["\ue721"] = "Search", ["\ue70f"] = "Edit", ["\ue7ad"] = "Shield", ["\ue8a7"] = "External",
        ["\ue8a5"] = "File", ["\ue71c"] = "Filter", ["\ue91c"] = "Eye", ["\ued1a"] = "EyeOff", ["\ue72b"] = "ChevronLeft",
        ["\ue8a4"] = "Grid", ["\ue8a1"] = "Tasks", ["\ue7e8"] = "Download", ["\ue898"] = "Upload",
        ["\ue768"] = "Play", ["\ue790"] = "Palette", ["\ue724"] = "Send", ["\ue734"] = "Star",
        ["\ue735"] = "Star", ["\uede1"] = "Upload", ["\uea99"] = "Broom", ["\ue71b"] = "Link",
        ["\ue946"] = "Info", ["\uea18"] = "Shield", ["\ue71a"] = "Stop", ["\ue700"] = "Menu",
        ["\ue708"] = "Moon", ["\uec61"] = "Check", ["\uea39"] = "Close", ["\ue70e"] = "ChevronUp",
        ["\ue945"] = "Lightning", ["\ue964"] = "Wand", ["\uebe8"] = "Bug", ["\ue8f4"] = "FolderPlus",
        ["\ue76b"] = "ChevronLeft", ["\ue769"] = "Pause"
    };

    public static IReadOnlyCollection<string> Names { get; } = new List<string>(Drawings.Keys).AsReadOnly();

    public static string GetPath(string kind)
    {
        if (TryGetPath(kind, out string path)) return path;
        throw new ArgumentException("Unknown launcher icon: " + kind, nameof(kind));
    }

    public static bool TryGetPath(string? kind, out string path)
    {
        if (kind == null) { path = ""; return false; }
        if (Aliases.TryGetValue(kind, out var canonical)) kind = canonical;
        if (Drawings.TryGetValue(kind, out var drawing)) { path = drawing; return true; }
        path = "";
        return false;
    }

    public static bool TryGetGlyph(string? glyph, out string path)
    {
        path = "";
        return glyph != null && Glyphs.TryGetValue(glyph, out var kind) && TryGetPath(kind, out path);
    }

    public static bool IsFilledGlyph(string? glyph) => glyph == "\ue735" || glyph == "\ue840";
}
