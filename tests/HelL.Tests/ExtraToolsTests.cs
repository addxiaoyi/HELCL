using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class ExtraToolsTests
{
	[Theory]
	[InlineData(16384L, 0, 3072L)]
	[InlineData(16384L, 30, 4096L)]
	[InlineData(16384L, 80, 6144L)]
	[InlineData(16384L, 150, 8192L)]
	[InlineData(32768L, 250, 10240L)]
	[InlineData(8192L, 250, 5632L)]
	[InlineData(2048L, 10, 1280L)]
	[InlineData(0L, 10, 4096L)]
	public void RecommendMemory(long totalMb, int mods, long expected)
	{
		Assert.Equal(expected, ExtraTools.RecommendMemory(totalMb, mods));
	}

	[Fact]
	public void RecommendMemoryNeverExceeds16G()
	{
		Assert.True(ExtraTools.RecommendMemory(262144L, 1000) <= 16384L);
	}

	[Fact]
	public void RecommendGc()
	{
		Assert.Equal("auto", ExtraTools.RecommendGc(2048, 17));
		Assert.Equal("g1_tuned", ExtraTools.RecommendGc(6144, 17));
		Assert.Equal("g1_tuned", ExtraTools.RecommendGc(16384, 17));
		Assert.Equal("zgc", ExtraTools.RecommendGc(16384, 21));
	}

	[Fact]
	public void SchemasMetaLabelsAgree()
	{
		List<string> schemaNames = ExtraTools.Schemas().Select(Tools.SchemaName).ToList();
		Assert.Equal(ExtraTools.Names.OrderBy(n => n), schemaNames.OrderBy(n => n));
		Assert.Equal(ExtraTools.Names.OrderBy(n => n), ExtraTools.Meta.Select(m => m.name).OrderBy(n => n));
		Assert.All(ExtraTools.Names, n => Assert.True(ExtraTools.Labels.ContainsKey(n), n));
	}

	[Fact]
	public void KeywordsOnlyPointAtKnownTools()
	{
		Assert.All(ExtraTools.Keywords.SelectMany(k => k.extra), n => Assert.Contains(n, ExtraTools.Names));
	}

	[Fact]
	public void UnknownToolFallsThrough()
	{
		Assert.False(ExtraTools.Handles("nope"));
		Assert.Null(ExtraTools.Run(null, "nope", new PyDict()));
	}
}
