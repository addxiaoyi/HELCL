using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HelL.Tests;

/// <summary>
/// 反编译器有时把“装箱的元组”写成指针强转读取（((ValueTuple&lt;..&gt;*)(&amp;obj))-&gt;Item1），
/// 读到的是对象引用而不是元组内容，运行时会把垃圾值塞进控件（例如 Toast 图标崩溃）。
/// </summary>
public class DecompileArtifactTests
{
	[Fact]
	public void NoPointerReadsOfBoxedTuples()
	{
		string src = Path.Combine(TestPaths.Root(), "src", "HelL");
		var bad = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
			.Where(f => File.ReadAllText(f).Contains("*)(&"))
			.Select(f => Path.GetRelativePath(src, f))
			.ToList();
		Assert.True(bad.Count == 0, "发现指针读取装箱元组的反编译残留: " + string.Join(", ", bad));
	}
}
