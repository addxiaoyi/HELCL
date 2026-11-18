using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Sync;
using Xunit;

namespace HelL.Tests;

/// <summary>用一个假的手机端（按手机启动器存档互传协议实现）跑一遍：握手 → 对比 → 推送 → 拉取 → 冲突判定。</summary>
public class PhoneLinkTests
{
	/// <summary>模拟手机端 HTTP 服务：世界放在 Dir/global 下。</summary>
	private sealed class FakePhone : IDisposable
	{
		public readonly string Dir;

		public readonly string Token = "123456";

		public readonly int Port;

		private readonly HttpListener _http = new HttpListener();

		private readonly Thread _thread;

		public string Saves => Path.Combine(Dir, "global");

		public FakePhone(string dir)
		{
			Dir = dir;
			Directory.CreateDirectory(Saves);
			TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			Port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			_http.Prefixes.Add("http://127.0.0.1:" + Port + "/");
			_http.Start();
			_thread = new Thread(Loop) { IsBackground = true };
			_thread.Start();
		}

		private void Loop()
		{
			while (_http.IsListening)
			{
				HttpListenerContext c;
				try
				{
					c = _http.GetContext();
				}
				catch (Exception)
				{
					return;
				}
				try
				{
					Handle(c);
				}
				catch (Exception e)
				{
					Json(c, 500, new { ok = false, error = e.Message });
				}
			}
		}

		private static void Json(HttpListenerContext c, int code, object o)
		{
			byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o));
			c.Response.StatusCode = code;
			c.Response.ContentType = "application/json";
			c.Response.OutputStream.Write(b, 0, b.Length);
			c.Response.Close();
		}

		private static long MaxMtime(string d)
		{
			return Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f)).ToUnixTimeMilliseconds()).DefaultIfEmpty(0).Max();
		}

		private void Handle(HttpListenerContext c)
		{
			HttpListenerRequest r = c.Request;
			if (r.Headers["X-PyMCL-Token"] != Token)
			{
				Json(c, 401, new { error = "unauthorized" });
				return;
			}
			string name = r.QueryString["name"] ?? "";
			string wd = Path.Combine(Saves, name);
			switch (r.Url.AbsolutePath)
			{
			case "/api/ping":
				Json(c, 200, new { app = "PyMCL", protocol = 1, device = "Test Phone", android = "14", port = Port, token = Token, deviceTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
				return;
			case "/api/worlds":
				Json(c, 200, new
				{
					root = new { id = "global", label = "全局存档", exists = true },
					roots = new[] { new { id = "global", label = "全局存档" }, new { id = "v:1.20.1", label = "版本隔离 1.20.1" } },
					worlds = Directory.GetDirectories(Saves).Where(d => File.Exists(Path.Combine(d, "level.dat"))).Select(d => new { dirName = Path.GetFileName(d), sizeBytes = 1L, fileCount = 1L, lastModified = MaxMtime(d) }).ToArray()
				});
				return;
			case "/api/worlds/download":
				if (!Directory.Exists(wd))
				{
					Json(c, 404, new { error = "world not found" });
					return;
				}
				c.Response.ContentType = "application/zip";
				c.Response.SendChunked = true;
				SaveSync.ExportZip(wd, c.Response.OutputStream);
				c.Response.Close();
				return;
			case "/api/worlds/upload":
			{
				if (Directory.Exists(wd) && r.QueryString["replace"] != "1")
				{
					Json(c, 409, new { ok = false, error = "conflict" });
					return;
				}
				MemoryStream ms = new MemoryStream();
				r.InputStream.CopyTo(ms);
				if (Directory.Exists(wd))
				{
					Directory.Delete(wd, true);
				}
				ms.Position = 0;
				using (ZipArchive z = new ZipArchive(ms))
				{
					foreach (ZipArchiveEntry e in z.Entries)
					{
						string rel = e.FullName.Substring(e.FullName.IndexOf('/') + 1);
						string dst = Path.Combine(wd, rel);
						Directory.CreateDirectory(Path.GetDirectoryName(dst));
						e.ExtractToFile(dst, true);
						File.SetLastWriteTime(dst, e.LastWriteTime.DateTime);
					}
				}
				Json(c, 200, new { ok = true, name, replaced = r.QueryString["replace"] == "1" });
				return;
			}
			default:
				Json(c, 404, new { error = "not found" });
				return;
			}
		}

		public void Dispose()
		{
			try
			{
				_http.Stop();
				_http.Close();
			}
			catch (Exception)
			{
			}
		}
	}

	[Fact]
	public void NormalizeAddress()
	{
		Assert.Equal("http://192.168.1.5:27531", PhoneClient.NormalizeAddress("192.168.1.5"));
		Assert.Equal("http://192.168.1.5:27532", PhoneClient.NormalizeAddress("192.168.1.5:27532"));
		Assert.Equal("http://192.168.1.5:27531", PhoneClient.NormalizeAddress(" http://192.168.1.5/ "));
	}

	[Fact]
	public void ParseAdbDevices()
	{
		string o = "List of devices attached\nABC123\tdevice\nXYZ\tunauthorized\n10.0.0.2:5555\tdevice\n\n";
		Assert.Equal(new[] { "ABC123", "10.0.0.2:5555" }, PhoneClient.ParseAdbDevices(o));
	}

	[Fact]
	public void DecideWithoutBase()
	{
		PhoneClient.World w = new PhoneClient.World { DirName = "a", LastModified = 1_000_000 };
		Assert.Equal(SyncDecision.Same, PhoneLink.Decide("h", 1_001_500, w, null));
		Assert.Equal(SyncDecision.Conflict, PhoneLink.Decide("h", 1_100_000, w, null));
		Assert.Equal(SyncDecision.OnlyLocal, PhoneLink.Decide("h", 1, null, null));
		Assert.Equal(SyncDecision.OnlyRemote, PhoneLink.Decide(null, 0, w, null));
		PhoneLink.Base b = new PhoneLink.Base { local_hash = "h", remote_mtime = 1_000_000 };
		Assert.Equal(SyncDecision.Same, PhoneLink.Decide("h", 0, w, b));
		Assert.Equal(SyncDecision.Push, PhoneLink.Decide("h2", 0, w, b));
		Assert.Equal(SyncDecision.Pull, PhoneLink.Decide("h", 0, new PhoneClient.World { LastModified = 2 }, b));
		Assert.Equal(SyncDecision.Conflict, PhoneLink.Decide("h2", 0, new PhoneClient.World { LastModified = 2 }, b));
	}

	[Fact]
	public void FullFlow()
	{
		using TestHome home = new TestHome();
		using FakePhone phone = new FakePhone(Path.Combine(home.Dir, "phone"));
		try
		{
			string addr = "127.0.0.1:" + phone.Port;
			PhoneClient.ApiException bad = Assert.Throws<PhoneClient.ApiException>(() => PhoneLink.Connect(addr, "000000"));
			Assert.Equal(401, bad.Status);

			PhoneClient.Info info = PhoneLink.Connect(addr, phone.Token);
			Assert.Equal("Test Phone", info.Device);
			Assert.Equal(1, info.Protocol);
			Assert.Equal(phone.Token, PhoneLink.LastToken);
			Assert.Equal(2, PhoneLink.Roots().Count);

			SaveSync.Target t = SaveSync.FindTarget("", "");
			Assert.NotNull(t);
			Assert.Equal("global", PhoneLink.DefaultRoot(t, PhoneLink.Roots()));

			// 电脑上有一个世界，手机上有另一个
			string pcWorld = WorldFixture.Create(t.SavesDir, "电脑世界");
			WorldFixture.Create(phone.Saves, "手机世界", "手机上玩的", 1729779999000L);
			List<PhoneLink.Row> rows = PhoneLink.Compare(t, "global");
			Assert.Equal(SyncDecision.OnlyLocal, rows.Single(r => r.Name == "电脑世界").Decision);
			Assert.Equal(SyncDecision.OnlyRemote, rows.Single(r => r.Name == "手机世界").Decision);

			// 一键同步：两边互相补齐
			List<string> done = PhoneLink.SyncAll(t, "global", _ => SyncDecision.Same);
			Assert.Equal(2, done.Count);
			Assert.True(File.Exists(Path.Combine(phone.Saves, "电脑世界", "level.dat")));
			Assert.True(File.Exists(Path.Combine(t.SavesDir, "手机世界", "level.dat")));
			Assert.All(PhoneLink.Compare(t, "global"), r => Assert.Equal(SyncDecision.Same, r.Decision));

			// 电脑上改了 → Push；推过去后手机上的旧版本留在电脑的备份目录
			File.WriteAllText(Path.Combine(pcWorld, "region", "new.mca"), "changed on pc");
			Assert.Equal(SyncDecision.Push, PhoneLink.Compare(t, "global").Single(r => r.Name == "电脑世界").Decision);
			PhoneLink.Push(t, "global", "电脑世界");
			Assert.Equal("changed on pc", File.ReadAllText(Path.Combine(phone.Saves, "电脑世界", "region", "new.mca")));
			string bdir = Saves.BackupsDir(new Instance(t.Instance));
			Assert.Contains(Directory.GetFiles(bdir), f => Path.GetFileName(f).StartsWith("电脑世界-phone-"));
			Assert.Equal(SyncDecision.Same, PhoneLink.Compare(t, "global").Single(r => r.Name == "电脑世界").Decision);

			// 手机上改了 → Pull
			string pw = Path.Combine(phone.Saves, "手机世界", "region", "p.mca");
			File.WriteAllText(pw, "changed on phone");
			File.SetLastWriteTimeUtc(pw, DateTime.UtcNow.AddMinutes(5));
			Assert.Equal(SyncDecision.Pull, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);
			PhoneLink.Pull(t, "global", "手机世界");
			Assert.Equal("changed on phone", File.ReadAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca")));
			Assert.Equal(SyncDecision.Same, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);

			// 两边都改 → Conflict，一键同步时交给回调，跳过就什么都不动
			File.WriteAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca"), "pc edit");
			File.WriteAllText(pw, "phone edit 2");
			File.SetLastWriteTimeUtc(pw, DateTime.UtcNow.AddMinutes(10));
			Assert.Equal(SyncDecision.Conflict, PhoneLink.Compare(t, "global").Single(r => r.Name == "手机世界").Decision);
			Assert.Empty(PhoneLink.SyncAll(t, "global", _ => SyncDecision.Same));
			Assert.Equal("pc edit", File.ReadAllText(Path.Combine(t.SavesDir, "手机世界", "region", "p.mca")));
		}
		finally
		{
			PhoneLink.Disconnect();
		}
	}
}
