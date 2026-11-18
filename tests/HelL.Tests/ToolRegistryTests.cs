using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

/// <summary>工具表的一致性：每个工具都有权限元数据、名字不重复、常驻工具都存在。</summary>
public class ToolRegistryTests
{
	private static List<string> Names => Tools.TOOL_SCHEMAS.Select(Tools.SchemaName).ToList();

	[Fact]
	public void NamesAreUnique()
	{
		List<string> dup = Names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
		Assert.Empty(dup);
	}

	[Fact]
	public void EveryToolHasMeta()
	{
		Assert.All(Names, n => Assert.True(ToolMeta.Get(n) != null, "缺少 ToolMeta: " + n));
	}

	[Fact]
	public void CoreToolsExist()
	{
		Assert.All(AiData.CORE_TOOLS, n => Assert.Contains(n, Names));
	}

	[Fact]
	public void KeywordToolsExist()
	{
		Assert.All(AiData.KEYWORD_TOOLS.SelectMany(k => k.extra), n => Assert.Contains(n, Names));
	}

	[Fact]
	public void SchemasHaveObjectParameters()
	{
		foreach (object s in Tools.TOOL_SCHEMAS)
		{
			HelL.Core.PyDict fn = (HelL.Core.PyDict)((HelL.Core.PyDict)s)["function"];
			HelL.Core.PyDict p = (HelL.Core.PyDict)fn["parameters"];
			Assert.Equal("object", p["type"]);
			Assert.False(string.IsNullOrWhiteSpace((string)fn["description"]), (string)fn["name"]);
		}
	}

	[Fact]
	public void MemoryAndWriteFlags()
	{
		Assert.False(Tools.IsWriteTool("remember"));
		Assert.False(Tools.IsWriteTool("list_mods"));
		Assert.True(Tools.IsWriteTool("delete_mod"));
		Assert.True(Tools.IsWriteTool("restore_save_backup"));
	}
}
