using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HelL.Pages;

public sealed class DashCard
{
	public string Id { get; set; } = "";

	public string Type { get; set; } = "";

	public double X { get; set; }

	public double Y { get; set; }

	public double W { get; set; } = 0.3;

	public double H { get; set; } = 0.3;

	public int Z { get; set; }

	public bool Hidden { get; set; }

	public JsonElement? Settings { get; set; }

	public DashCard Clone()
	{
		return new DashCard
		{
			Id = Id,
			Type = Type,
			X = X,
			Y = Y,
			W = W,
			H = H,
			Z = Z,
			Hidden = Hidden,
			Settings = Settings?.Clone()
		};
	}

	public static DashCard FromJson(JsonElement e)
	{
		DashCard obj = new DashCard
		{
			Id = Str(e, "id")
		};
		string text = Str(e, "type");
		obj.Type = ((text != null && text.Length > 0) ? text : "notes");
		obj.X = Clamp01(Num(e, "x", 0.0));
		obj.Y = Clamp.Of(Num(e, "y", 0.0), 0, 20);
		obj.W = Clamp.Of(Num(e, "w", 0.3), 0.04, 1.0);
		obj.H = Clamp.Of(Num(e, "h", 0.3), 0.04, 4.0);
		obj.X = Math.Min(obj.X, 1 - obj.W);
		obj.Z = (int)Num(e, "z", 0.0);
		obj.Hidden = e.TryGetProperty("hidden", out var value) && value.ValueKind == JsonValueKind.True;
		DashCard dashCard = obj;
		if (e.TryGetProperty("settings", out var value2) && value2.ValueKind == JsonValueKind.Object)
		{
			dashCard.Settings = value2.Clone();
		}
		return dashCard;
	}

	public Dictionary<string, object?> ToDict()
	{
		Dictionary<string, object> obj = new Dictionary<string, object>
		{
			["id"] = Id,
			["type"] = Type,
			["x"] = Math.Round(X, 5),
			["y"] = Math.Round(Y, 5),
			["w"] = Math.Round(W, 5),
			["h"] = Math.Round(H, 5),
			["z"] = Z,
			["hidden"] = Hidden
		};
		JsonElement? settings = Settings;
		object value = ((!settings.HasValue) ? new Dictionary<string, object>() : ((object)settings.GetValueOrDefault()));
		obj["settings"] = value;
		return obj;
	}

	private static string Str(JsonElement e, string k)
	{
		if (!e.TryGetProperty(k, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static double Num(JsonElement e, string k, double fallback)
	{
		if (!e.TryGetProperty(k, out var value))
		{
			return fallback;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var value2))
		{
			return double.IsNaN(value2) || double.IsInfinity(value2) ? fallback : value2;
		}
		if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var result))
		{
			return double.IsNaN(result) || double.IsInfinity(result) ? fallback : result;
		}
		return fallback;
	}

	private static double Clamp01(double v)
	{
		return Clamp.Of(v, 0.0, 1.0);
	}
}
