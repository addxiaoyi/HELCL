using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HelL.Core.Launcher.Sync;

/// <summary>
/// 只读 level.dat 里同步需要的几项：世界名、最后游玩时间、数据版本、游戏版本名。
/// level.dat 是 gzip 压缩的大端 NBT，这里只做顺序遍历，不建完整的树。
/// </summary>
public sealed class LevelDat
{
    public const int MaxBytes = 8 * 1024 * 1024;
	public string LevelName = "";

	/// <summary>Data.LastPlayed，Unix 毫秒；读不到为 0。</summary>
	public long LastPlayed;

	/// <summary>Data.DataVersion，世界格式版本号；读不到为 0。</summary>
	public int DataVersion;

	/// <summary>Data.Version.Name，例如 1.20.1；读不到为空。</summary>
	public string VersionName = "";

	public static LevelDat ReadFile(string path)
	{
		try
		{
			if (new FileInfo(path).Length > MaxBytes) return null;
			return Read(File.ReadAllBytes(path));
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static LevelDat Read(byte[] raw)
	{
		try
		{
			if (raw.Length > MaxBytes) return null;
			byte[] data = raw;
			if (raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B)
			{
				using MemoryStream ms = new MemoryStream();
				using (GZipStream gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
				{
					byte[] buffer = new byte[16384]; int read;
					while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
					{
						if (ms.Length + read > MaxBytes) return null;
						ms.Write(buffer, 0, read);
					}
				}
				data = ms.ToArray();
			}
			Reader r = new Reader(data);
			LevelDat res = new LevelDat();
			if (r.U8() != 10)
			{
				return null;
			}
			r.Str();
			r.Compound(res, "");
			return res;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private sealed class Reader
	{
		private readonly byte[] _d;

		private int _p;

		public Reader(byte[] d)
		{
			_d = d;
		}

		public byte U8()
		{
			return _d[_p++];
		}

		public short I16()
		{
			short v = (short)((_d[_p] << 8) | _d[_p + 1]);
			_p += 2;
			return v;
		}

		public int I32()
		{
			int v = (_d[_p] << 24) | (_d[_p + 1] << 16) | (_d[_p + 2] << 8) | _d[_p + 3];
			_p += 4;
			return v;
		}

		public long I64()
		{
			long hi = (uint)I32();
			long lo = (uint)I32();
			return (hi << 32) | lo;
		}

		public string Str()
		{
			int len = (ushort)I16();
			string s = Encoding.UTF8.GetString(_d, _p, len);
			_p += len;
			return s;
		}

		private void Skip(int n)
		{
			if (n < 0 || (long)_p + n > _d.Length)
			{
				throw new InvalidDataException("NBT 越界");
			}
			_p += n;
		}

		private int _depth;
		private int _tags;
		public void Compound(LevelDat res, string path)
		{
			if (++_depth > 64) throw new InvalidDataException("NBT 层级过深");
			while (true)
			{
				byte type = U8();
				if (type == 0)
				{
					_depth--;
					return;
				}
				string name = Str();
				string full = path.Length == 0 ? name : path + "." + name;
				Payload(type, res, full);
			}
		}

		private void Payload(byte type, LevelDat res, string full)
		{
			if (++_tags > 1000000) throw new InvalidDataException("NBT 标签过多");
			switch (type)
			{
			case 1:
				Skip(1);
				break;
			case 2:
				Skip(2);
				break;
			case 3:
			{
				int v = I32();
				if (full == "Data.DataVersion")
				{
					res.DataVersion = v;
				}
				break;
			}
			case 4:
			{
				long v = I64();
				if (full == "Data.LastPlayed")
				{
					res.LastPlayed = v;
				}
				break;
			}
			case 5:
				Skip(4);
				break;
			case 6:
				Skip(8);
				break;
			case 7:
				Skip(I32());
				break;
			case 8:
			{
				string s = Str();
				if (full == "Data.LevelName")
				{
					res.LevelName = s;
				}
				else if (full == "Data.Version.Name")
				{
					res.VersionName = s;
				}
				break;
			}
			case 9:
			{
				if (++_depth > 64) throw new InvalidDataException("NBT 层级过深");
				byte et = U8();
				int n = I32();
				if (n < 0 || n > 1000000) throw new InvalidDataException("NBT 列表长度无效");
				for (int i = 0; i < n; i++)
				{
					Payload(et, res, full + "[]");
				}
				_depth--;
				break;
			}
			case 10:
				Compound(res, full);
				break;
			case 11:
				Skip(checked(I32() * 4));
				break;
			case 12:
				Skip(checked(I32() * 8));
				break;
			default:
				throw new InvalidDataException("未知 NBT 类型 " + type);
			}
		}
	}
}
