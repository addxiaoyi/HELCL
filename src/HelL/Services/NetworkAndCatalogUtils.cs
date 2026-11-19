using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Services;

public class ColorSegment
{
    public string Text { get; set; }
    public string HexColor { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    public ColorSegment(string text, string hexColor, bool bold = false, bool italic = false)
    {
        Text = text;
        HexColor = hexColor;
        Bold = bold;
        Italic = italic;
    }
}

public static class MinecraftColorParser
{
    private static readonly Dictionary<char, string> ColorTable = new Dictionary<char, string>
    {
        { '0', "#000000" },
        { '1', "#0000AA" },
        { '2', "#00AA00" },
        { '3', "#00AAAA" },
        { '4', "#AA0000" },
        { '5', "#AA00AA" },
        { '6', "#FFAA00" },
        { '7', "#AAAAAA" },
        { '8', "#555555" },
        { '9', "#5555FF" },
        { 'a', "#55FF55" },
        { 'b', "#55FFFF" },
        { 'c', "#FF5555" },
        { 'd', "#FF55FF" },
        { 'e', "#FFFF55" },
        { 'f', "#FFFFFF" },
        { 'r', "#FFFFFF" }
    };

    public static string StripColorCodes(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return Regex.Replace(raw, "§[0-9a-fk-orA-FK-OR]", "");
    }

    public static List<ColorSegment> ParseColorRuns(string raw)
    {
        var result = new List<ColorSegment>();
        if (string.IsNullOrEmpty(raw)) return result;

        string currentColor = "#FFFFFF";
        bool bold = false;
        bool italic = false;
        var currentText = new StringBuilder();

        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '§' && i + 1 < raw.Length)
            {
                char code = char.ToLowerInvariant(raw[i + 1]);
                i++; // skip code char

                if (ColorTable.TryGetValue(code, out string hex))
                {
                    if (currentText.Length > 0)
                    {
                        result.Add(new ColorSegment(currentText.ToString(), currentColor, bold, italic));
                        currentText.Clear();
                    }
                    currentColor = hex;
                    if (code == 'r')
                    {
                        bold = false;
                        italic = false;
                    }
                }
                else if (code == 'l')
                {
                    bold = true;
                }
                else if (code == 'o')
                {
                    italic = true;
                }
            }
            else
            {
                currentText.Append(raw[i]);
            }
        }

        if (currentText.Length > 0)
        {
            result.Add(new ColorSegment(currentText.ToString(), currentColor, bold, italic));
        }

        return result;
    }
}

public static class MinecraftPingProtocol
{
    public static byte[] EncodeVarInt(int value)
    {
        var buffer = new List<byte>();
        uint v = (uint)value;
        do
        {
            byte temp = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
            {
                temp |= 0x80;
            }
            buffer.Add(temp);
        } while (v != 0);

        return buffer.ToArray();
    }

    public static int DecodeVarInt(Stream stream)
    {
        int numRead = 0;
        int result = 0;
        byte read;
        do
        {
            int b = stream.ReadByte();
            if (b == -1)
            {
                throw new EndOfStreamException("End of stream while decoding VarInt");
            }
            read = (byte)b;
            int value = (read & 0x7F);
            result |= (value << (7 * numRead));
            numRead++;
            if (numRead > 5)
            {
                throw new InvalidOperationException("VarInt is too big");
            }
        } while ((read & 0x80) != 0);

        return result;
    }
}

public class ModDependency
{
    public string ModId { get; set; }
    public bool IsRequired { get; set; }

    public ModDependency(string modId, bool isRequired)
    {
        ModId = modId;
        IsRequired = isRequired;
    }
}

public static class DependencyResolver
{
    public static List<ModDependency> GetMissingRequired(HashSet<string> installedModIds, IEnumerable<ModDependency> dependencies)
    {
        var missing = new List<ModDependency>();
        if (dependencies == null) return missing;

        var set = installedModIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dep in dependencies)
        {
            if (dep.IsRequired && !set.Contains(dep.ModId))
            {
                missing.Add(dep);
            }
        }

        return missing;
    }
}

public static class NetworkAndCatalogUtils
{
    public static string FormatDependencyWarning(IEnumerable<string> missingMods)
    {
        if (missingMods == null) return null;
        var list = missingMods.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (list.Count == 0) return null;
        return "缺少前置模组：" + string.Join(", ", list) + "，可能无法正常运行";
    }
}
