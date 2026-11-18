using System;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

/// <summary>设置页「记忆」卡片和对话里「撤销」用到的记忆编辑逻辑。</summary>
public class MemoryEditTests
{
	[Fact]
	public void TextOfStripsDate()
	{
		Assert.Equal("常玩 1.20.1", FileMemory.TextOf("2026-10-31 常玩 1.20.1"));
		Assert.Equal("没有日期的一条", FileMemory.TextOf("没有日期的一条"));
		Assert.Equal("", FileMemory.TextOf(null));
	}

	[Fact]
	public void RememberedOfParsesToolResult()
	{
		Assert.Equal("显卡是 RTX 3060", FileMemory.RememberedOf("已记住: 显卡是 RTX 3060"));
		Assert.Equal("喜欢 BSL", FileMemory.RememberedOf("已记住：喜欢 BSL"));
		Assert.Null(FileMemory.RememberedOf("已经记过了: 喜欢 BSL"));
		Assert.Null(FileMemory.RememberedOf("没有要记的内容"));
	}

	[Fact]
	public void UndoRemovesOnlyThatEntry()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("常玩 1.20.1 Fabric");
		string result = FileMemory.Remember("显卡是 RTX 3060");
		Assert.True(FileMemory.Undo(FileMemory.RememberedOf(result)));
		Assert.Single(FileMemory.Entries());
		Assert.Contains("1.20.1 Fabric", FileMemory.Entries()[0]);
		Assert.False(FileMemory.Undo("显卡是 RTX 3060"));
		Assert.False(FileMemory.Undo(""));
	}

	[Fact]
	public void UpdateKeepsDate()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("内存 8G");
		string before = FileMemory.Entries()[0];
		Assert.True(FileMemory.Update(0, "内存 16G"));
		string after = FileMemory.Entries()[0];
		Assert.Equal("内存 16G", FileMemory.TextOf(after));
		Assert.Equal(before.Substring(0, 10), after.Substring(0, 10));
		Assert.False(FileMemory.Update(5, "越界"));
		Assert.False(FileMemory.Update(-1, "越界"));
	}

	[Fact]
	public void UpdateWithEmptyTextDeletes()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("第一条");
		FileMemory.Remember("第二条");
		Assert.True(FileMemory.Update(0, "   "));
		Assert.Single(FileMemory.Entries());
		Assert.Equal("第二条", FileMemory.TextOf(FileMemory.Entries()[0]));
	}

	[Fact]
	public void UpdateFlattensNewlines()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("一条");
		Assert.True(FileMemory.Update(0, "两行\n内容"));
		Assert.Single(FileMemory.Entries());
		Assert.Equal("两行 内容", FileMemory.TextOf(FileMemory.Entries()[0]));
	}

	[Fact]
	public void RemoveAtAndClear()
	{
		using TestHome home = new TestHome();
		FileMemory.Remember("A 条");
		FileMemory.Remember("B 条");
		FileMemory.Remember("C 条");
		Assert.True(FileMemory.RemoveAt(1));
		Assert.Equal(2, FileMemory.Entries().Count);
		Assert.DoesNotContain(FileMemory.Entries(), e => e.EndsWith("B 条", StringComparison.Ordinal));
		Assert.False(FileMemory.RemoveAt(9));
		Assert.Equal(2, FileMemory.Clear());
		Assert.Empty(FileMemory.Entries());
		Assert.Equal(0, FileMemory.Clear());
	}
}
