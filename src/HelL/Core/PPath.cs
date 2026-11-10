using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public sealed class PPath : IEquatable<PPath>
{
	public static bool DefaultWindows = Path.DirectorySeparatorChar == '\\';

	private readonly string[] _parts;

	public bool Windows { get; }

	public string Drive { get; }

	public string Root { get; }

	public IReadOnlyList<string> Tail => _parts;

	public string Anchor => Drive + Root;

	public List<string> Parts
	{
		get
		{
			List<string> list = new List<string>();
			if (Anchor.Length > 0)
			{
				list.Add(Anchor);
			}
			list.AddRange(_parts);
			return list;
		}
	}

	public string Name
	{
		get
		{
			if (_parts.Length == 0)
			{
				return "";
			}
			return _parts[_parts.Length - 1];
		}
	}

	public string Suffix
	{
		get
		{
			string name = Name;
			int num = name.LastIndexOf('.');
			if (num <= 0 || num >= name.Length - 1)
			{
				return "";
			}
			return name.Substring(num);
		}
	}

	public List<string> Suffixes
	{
		get
		{
			List<string> list = new List<string>();
			string name = Name;
			if (name.EndsWith("."))
			{
				return list;
			}
			name = name.TrimStart('.');
			string[] array = name.Split('.');
			for (int i = 1; i < array.Length; i++)
			{
				list.Add("." + array[i]);
			}
			return list;
		}
	}

	public string Stem
	{
		get
		{
			string name = Name;
			int num = name.LastIndexOf('.');
			if (num <= 0 || num >= name.Length - 1)
			{
				return name;
			}
			return name.Substring(0, num);
		}
	}

	public PPath Parent
	{
		get
		{
			if (_parts.Length != 0)
			{
				return new PPath(Windows, Drive, Root, _parts.Take(_parts.Length - 1));
			}
			return this;
		}
	}

	public bool IsAbsolute
	{
		get
		{
			if (!Windows)
			{
				return Root.Length > 0;
			}
			if (Drive.Length <= 0 || Root.Length <= 0)
			{
				return Drive.StartsWith("\\\\");
			}
			return true;
		}
	}

	public PPath(params object[] segments)
		: this(DefaultWindows, segments)
	{
	}

	public PPath(bool windows, params object[] segments)
	{
		Windows = windows;
		string drive = "";
		string root = "";
		List<string> list = new List<string>();
		object[] array = segments ?? Array.Empty<object>();
		foreach (object obj in array)
		{
			if (obj == null)
			{
				throw new TypeError("expected str, bytes or os.PathLike object, not NoneType");
			}
			string s = ((obj is PPath pPath) ? pPath.ToString() : ((obj as string) ?? throw new TypeError("expected str, bytes or os.PathLike object, not " + Py.TypeName(obj))));
			Combine(windows, ref drive, ref root, list, s);
		}
		Drive = drive;
		Root = root;
		_parts = list.ToArray();
	}

	private PPath(bool windows, string drive, string root, IEnumerable<string> parts)
	{
		Windows = windows;
		Drive = drive;
		Root = root;
		_parts = parts.ToArray();
	}

	private static void SplitRoot(bool windows, string s, out string drive, out string root, out string rest)
	{
		if (!windows)
		{
			drive = "";
			if (s.StartsWith("/"))
			{
				if (s.StartsWith("//") && !s.StartsWith("///"))
				{
					root = "//";
					rest = s.Substring(2);
				}
				else
				{
					root = "/";
					rest = s.TrimStart('/');
				}
			}
			else
			{
				root = "";
				rest = s;
			}
			return;
		}
		s = s.Replace('/', '\\');
		if (s.Length >= 2 && s[0] == '\\' && s[1] == '\\')
		{
			string text = s.ToUpperInvariant();
			int startIndex = 2;
			if (text.StartsWith("\\\\?\\UNC\\"))
			{
				startIndex = 8;
			}
			int num = s.IndexOf('\\', startIndex);
			if (num < 0)
			{
				drive = s;
				root = "";
				rest = "";
				return;
			}
			int num2 = s.IndexOf('\\', num + 1);
			if (num2 < 0)
			{
				drive = s;
				root = "";
				rest = "";
			}
			else
			{
				drive = s.Substring(0, num2);
				root = "\\";
				rest = s.Substring(num2 + 1);
			}
		}
		else if (s.Length >= 2 && s[1] == ':')
		{
			drive = s.Substring(0, 2);
			if (s.Length >= 3 && s[2] == '\\')
			{
				root = "\\";
				rest = s.Substring(3);
			}
			else
			{
				root = "";
				rest = s.Substring(2);
			}
		}
		else
		{
			drive = "";
			if (s.StartsWith("\\"))
			{
				root = "\\";
				rest = s.Substring(1);
			}
			else
			{
				root = "";
				rest = s;
			}
		}
	}

	private static IEnumerable<string> SplitParts(bool windows, string rest)
	{
		char c = (windows ? '\\' : '/');
		string[] array = rest.Split(c);
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.Length > 0 && text != ".")
			{
				yield return text;
			}
		}
	}

	private static void Combine(bool windows, ref string drive, ref string root, List<string> parts, string s)
	{
		SplitRoot(windows, s, out string drive2, out string root2, out string rest);
		if (!windows)
		{
			if (root2.Length > 0)
			{
				root = root2;
				parts.Clear();
			}
			parts.AddRange(SplitParts(windows: false, rest));
			return;
		}
		if (root2.Length > 0)
		{
			if (drive2.Length > 0 || drive.Length == 0)
			{
				drive = drive2;
			}
			root = root2;
			parts.Clear();
			parts.AddRange(SplitParts(windows: true, rest));
			return;
		}
		if (drive2.Length > 0 && drive2 != drive)
		{
			if (!string.Equals(drive2, drive, StringComparison.OrdinalIgnoreCase))
			{
				drive = drive2;
				root = root2;
				parts.Clear();
				parts.AddRange(SplitParts(windows: true, rest));
				return;
			}
			drive = drive2;
		}
		parts.AddRange(SplitParts(windows: true, rest));
	}

	public override string ToString()
	{
		string separator = (Windows ? "\\" : "/");
		string text = Drive + Root;
		if (_parts.Length == 0)
		{
			if (text.Length <= 0)
			{
				return ".";
			}
			return text;
		}
		return text + string.Join(separator, _parts);
	}

	public static implicit operator string(PPath p)
	{
		return p?.ToString();
	}

	public static PPath operator /(PPath a, string b)
	{
		return new PPath(a.Windows, a, b);
	}

	public static PPath operator /(PPath a, PPath b)
	{
		return new PPath(a.Windows, a, b);
	}

	public static string Join(params object[] segments)
	{
		return new PPath(segments).ToString();
	}

	public static string Norm(string s)
	{
		return new PPath(s).ToString();
	}

	public bool Equals(PPath other)
	{
		if (other != null)
		{
			return string.Equals(Key(), other.Key(), StringComparison.Ordinal);
		}
		return false;
	}

	public override bool Equals(object obj)
	{
		if (obj is PPath other)
		{
			return Equals(other);
		}
		return false;
	}

	public override int GetHashCode()
	{
		return Key().GetHashCode();
	}

	private string Key()
	{
		if (!Windows)
		{
			return ToString();
		}
		return ToString().ToLowerInvariant();
	}

	public PPath WithName(string name)
	{
		if (_parts.Length == 0)
		{
			throw new ValueError(Py.ReprStr(ToString()) + " has an empty name");
		}
		if (string.IsNullOrEmpty(name) || name == "." || name.Contains("/") || (Windows && name.Contains("\\")))
		{
			throw new ValueError("Invalid name " + Py.ReprStr(name ?? ""));
		}
		string[] array = _parts.ToArray();
		array[array.Length - 1] = name;
		return new PPath(Windows, Drive, Root, array);
	}

	public PPath WithSuffix(string suffix)
	{
		if (!string.IsNullOrEmpty(suffix) && (!suffix.StartsWith(".") || suffix == "."))
		{
			throw new ValueError("Invalid suffix " + Py.ReprStr(suffix));
		}
		string name = Name;
		if (name.Length == 0)
		{
			throw new ValueError(Py.ReprStr(ToString()) + " has an empty name");
		}
		string suffix2 = Suffix;
		string text = ((suffix2.Length == 0) ? name : name.Substring(0, name.Length - suffix2.Length));
		return WithName(text + suffix);
	}

	public PPath WithStem(string stem)
	{
		return WithName(stem + Suffix);
	}

	public PPath RelativeTo(object other)
	{
		PPath pPath = (other as PPath) ?? new PPath(Windows, other);
		List<string> parts = Parts;
		List<string> parts2 = pPath.Parts;
		bool flag = parts2.Count <= parts.Count;
		int num = 0;
		while (flag && num < parts2.Count)
		{
			flag = eq(parts[num], parts2[num]);
			num++;
		}
		if (!flag || (pPath.Anchor.Length == 0 && Anchor.Length > 0))
		{
			throw new ValueError(Py.ReprStr(ToString()) + " is not in the subpath of " + Py.ReprStr(pPath.ToString()));
		}
		return new PPath(Windows, "", "", parts.Skip(parts2.Count));
		bool eq(string a, string b)
		{
			if (!Windows)
			{
				return a == b;
			}
			return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}
	}

	public bool IsRelativeTo(object other)
	{
		try
		{
			RelativeTo(other);
			return true;
		}
		catch (ValueError)
		{
			return false;
		}
	}

	public static string Abs(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			path = ".";
		}
		if (Path.IsPathRooted(path) && (!DefaultWindows || path.Length < 2 || path[1] != ':' || (path.Length != 2 && (path[2] == '\\' || path[2] == '/'))))
		{
			return Path.GetFullPath(path);
		}
		return Path.GetFullPath(Path.Combine(Utils.ROOT, path));
	}

	public string Abs()
	{
		return Abs(ToString());
	}

	public bool Exists()
	{
		string path = Abs();
		if (!File.Exists(path))
		{
			return Directory.Exists(path);
		}
		return true;
	}

	public bool IsFile()
	{
		return File.Exists(Abs());
	}

	public bool IsDir()
	{
		return Directory.Exists(Abs());
	}

	public PPath Resolve()
	{
		return new PPath(Windows, Abs());
	}
}
