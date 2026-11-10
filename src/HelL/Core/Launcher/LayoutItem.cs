using System;

namespace HelL.Core.Launcher;

public sealed class LayoutItem
{
	public string id;

	public string type;

	public double x;

	public double y;

	public double w;

	public double h;

	public long z;

	public bool hidden;

	public PyDict settings;

	private static double Clamp01(double v)
	{
		if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
		if (!(v < 0.0))
		{
			if (!(v > 1.0))
			{
				return v;
			}
			return 1.0;
		}
		return 0.0;
	}

	public LayoutItem(string item_type, double x, double y, double w, double h, string item_id = "", long z = 0L, bool hidden = false, PyDict settings = null)
	{
		id = ((!string.IsNullOrEmpty(item_id)) ? item_id : $"{item_type}-{(long)(Py.Time() * 1000.0) % 100000000}");
		type = item_type;
		this.x = Clamp01(x);
		this.y = double.IsNaN(y) || double.IsInfinity(y) ? 0 : Math.Max(0, Math.Min(20, y));
		this.w = double.IsNaN(w) || double.IsInfinity(w) ? 0.3 : Math.Max(0.04, Math.Min(1, w));
		this.h = double.IsNaN(h) || double.IsInfinity(h) ? 0.3 : Math.Max(0.04, Math.Min(4, h));
		this.x = Math.Min(this.x, 1 - this.w);
		this.z = z;
		this.hidden = hidden;
		this.settings = ((settings != null) ? new PyDict(settings) : new PyDict());
	}

	public PyDict ToDict()
	{
		return PyDict.Of("id", id, "type", type, "x", Py.Round(x, 5), "y", Py.Round(y, 5), "w", Py.Round(w, 5), "h", Py.Round(h, 5), "z", z, "hidden", hidden, "settings", settings);
	}

	public static LayoutItem FromDict(PyDict data)
	{
		return new LayoutItem(Py.Str(Py.Or(data.Get("type"), "notes")), Py.Float(Py.Or(data.Get("x"), 0.0)), Py.Float(Py.Or(data.Get("y"), 0.0)), Py.Float(Py.Or(data.Get("w"), 0.3)), Py.Float(Py.Or(data.Get("h"), 0.3)), Py.Str(Py.Or(data.Get("id"), "")), Py.Int(Py.Or(data.Get("z"), 0L)), Py.Truthy(data.Get("hidden")), Py.DictCtor(Py.Or(data.Get("settings"), new PyDict())));
	}

	public (long x, long y, long w, long h) GeometryPx((long cw, long ch) size)
	{
		var (num, num2) = size;
		return (x: Py.RoundInt(x * (double)num), y: Py.RoundInt(y * (double)num2), w: Math.Max(1L, Py.RoundInt(w * (double)num)), h: Math.Max(1L, Py.RoundInt(h * (double)num2)));
	}

	public void SetGeometryPx(long px, long py, long pw, long ph, (long cw, long ch) size)
	{
		long num = Math.Max(1L, size.cw);
		long num2 = Math.Max(1L, size.ch);
		pw = Math.Max(1L, Math.Min(pw, num));
		ph = Math.Max(1L, Math.Min(ph, num2));
		px = Math.Max(0L, Math.Min(px, num - pw));
		py = Math.Max(0L, Math.Min(py, num2 - ph));
		x = Clamp01((double)px / (double)num);
		y = Clamp01((double)py / (double)num2);
		w = Py.Max(0.01, Py.Min(1.0, (double)pw / (double)num));
		h = Py.Max(0.01, Py.Min(1.0, (double)ph / (double)num2));
	}
}
