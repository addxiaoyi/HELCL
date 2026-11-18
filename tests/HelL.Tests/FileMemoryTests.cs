using System.IO;
using System.Linq;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class FileMemoryTests
{
	[Fact]
	public void RememberLoadForget()
	{
		using TestHome home = new TestHome();
		Assert.StartsWith("已记住", FileMemory.Remember("常玩 1.20.1 Fabric"));
		Assert.StartsWith("已记住", FileMemory.Remember("显卡是 RTX 3060"));
		Assert.Equal(2, FileMemory.Entries().Count);
		Assert.True(File.Exists(Path.Combine(home.Dir, "ai_memory.md")));
		string ctx = new FileMemory().Load("any");
		Assert.Contains("1.20.1 Fabric", ctx);
		Assert.Contains("RTX 3060", ctx);
		Assert.StartsWith("已删除 1 条", FileMemory.Forget("显卡"));
		Assert.Single(FileMemory.Entries());
		Assert.StartsWith("没有包含", FileMemory.Forget("不存在的词"));
	}

	[Fact]
	public void DuplicateIsIgnored()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("喜欢 BSL 光影");
		Assert.StartsWith("已经记过了", FileMemory.Remember("喜欢 BSL 光影"));
		Assert.Single(FileMemory.Entries());
	}

	[Fact]
	public void EmptyInputRejected()
	{
		using TestHome home = new TestHome();
		Assert.Equal("没有要记的内容", FileMemory.Remember("   "));
		Assert.Equal("需要关键词", FileMemory.Forget(""));
	}

	[Fact]
	public void LongEntryIsTruncatedAndCountCapped()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember(new string('a', 1000));
		Assert.True(FileMemory.Entries().Single().Length <= 340);
		for (int i = 0; i < 100; i++)
		{
			FileMemory.Remember("第 " + i + " 条");
		}
		Assert.True(FileMemory.Entries().Count <= 80);
	}

	[Fact]
	public void LoadIsEmptyWithoutFile()
	{
		using TestHome home = new TestHome();
		Assert.Equal("", new FileMemory().Load("x"));
	}
}
