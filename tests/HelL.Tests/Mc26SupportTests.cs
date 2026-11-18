using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public class Mc26SupportTests
{
	[Theory]
	[InlineData("26.3", 25L)]
	[InlineData("26.1.2", 25L)]
	[InlineData("1.21.11", 21L)]
	[InlineData("1.20.5", 21L)]
	[InlineData("1.20.4", 17L)]
	[InlineData("1.18.2", 17L)]
	public void InstallerJava(string mc, long expected)
	{
		Assert.Equal(expected, Installer.InstallerJavaMajor(mc));
	}

	[Theory]
	[InlineData("26.3", "26.3.0")]
	[InlineData("26.1.2", "26.1.2")]
	[InlineData("26.1", "26.1.0")]
	[InlineData("1.21.1", "21.1")]
	[InlineData("1.21.11", "21.11")]
	[InlineData("1.20.1", "47.1")]
	public void NeoforgePrefix(string mc, string expected)
	{
		Assert.Equal(expected, LoaderMeta.NeoforgePrefix(mc));
	}
}
