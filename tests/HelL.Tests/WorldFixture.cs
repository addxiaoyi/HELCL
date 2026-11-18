using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HelL.Tests;

/// <summary>造一个最小的 Minecraft 世界：gzip NBT 的 level.dat 加几个区域文件。</summary>
internal static class WorldFixture
{
	public static byte[] LevelDat(string levelName, long lastPlayed, int dataVersion, string versionName)
	{
		MemoryStream raw = new MemoryStream();
		BinaryWriterBE w = new BinaryWriterBE(raw);
		w.Tag(10, "");
		w.Tag(10, "Data");
		w.Tag(1, "hardcore"); w.U8(0);
		w.Tag(9, "ServerBrands"); w.U8(8); w.I32(1); w.Str("vanilla");
		w.Tag(7, "Bytes"); w.I32(3); w.U8(1); w.U8(2); w.U8(3);
		w.Tag(11, "Ints"); w.I32(2); w.I32(1); w.I32(2);
		w.Tag(6, "BorderSize"); w.I64(0);
		w.Tag(8, "LevelName"); w.Str(levelName);
		w.Tag(4, "LastPlayed"); w.I64(lastPlayed);
		w.Tag(3, "DataVersion"); w.I32(dataVersion);
		w.Tag(10, "Version");
		w.Tag(8, "Name"); w.Str(versionName);
		w.Tag(3, "Id"); w.I32(dataVersion);
		w.U8(0);
		w.Tag(10, "Player");
		w.Tag(9, "Pos"); w.U8(6); w.I32(3); w.I64(0); w.I64(0); w.I64(0);
		w.Tag(8, "LevelName"); w.Str("不是这个");
		w.U8(0);
		w.U8(0);
		w.U8(0);
		MemoryStream gz = new MemoryStream();
		using (GZipStream g = new GZipStream(gz, CompressionMode.Compress, leaveOpen: true))
		{
			raw.Position = 0;
			raw.CopyTo(g);
		}
		return gz.ToArray();
	}

	public static string Create(string savesDir, string name, string levelName = "测试世界", long lastPlayed = 1729770000000L)
	{
		string dir = Path.Combine(savesDir, name);
		Directory.CreateDirectory(Path.Combine(dir, "region"));
		File.WriteAllBytes(Path.Combine(dir, "level.dat"), LevelDat(levelName, lastPlayed, 3465, "1.20.1"));
		File.WriteAllBytes(Path.Combine(dir, "region", "r.0.0.mca"), Encoding.ASCII.GetBytes("region-data-0"));
		File.WriteAllText(Path.Combine(dir, "session.lock"), "☃");
		return dir;
	}

	private sealed class BinaryWriterBE
	{
		private readonly Stream _s;

		public BinaryWriterBE(Stream s)
		{
			_s = s;
		}

		public void U8(int v)
		{
			_s.WriteByte((byte)v);
		}

		public void I16(int v)
		{
			U8(v >> 8); U8(v);
		}

		public void I32(int v)
		{
			U8(v >> 24); U8(v >> 16); U8(v >> 8); U8(v);
		}

		public void I64(long v)
		{
			I32((int)(v >> 32)); I32((int)v);
		}

		public void Str(string s)
		{
			byte[] b = Encoding.UTF8.GetBytes(s);
			I16(b.Length);
			_s.Write(b, 0, b.Length);
		}

		public void Tag(int type, string name)
		{
			U8(type);
			Str(name);
		}
	}
}
