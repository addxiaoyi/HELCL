using System.Linq;
using HelL.Core.Launcher;
using Xunit;

public class LayoutDefaultTests
{
	[Fact]
	public void BackendDefaultHasAllSixHomeCards()
	{
		var types = UiLayout.DefaultDoc().items.Select(i => i.type).ToArray();
		Assert.Equal(new[] { "banner", "config", "log", "quick", "playtime", "news" }, types);
	}
}

public class VersionCompareTests
{
	[Fact]
	public void TwoDigitPatchIsNewer()
	{
		Assert.True(Updater.Newer("1.0.10", "1.0.9"));
		Assert.False(Updater.Newer("1.0.9", "1.0.10"));
	}

	[Fact]
	public void UpdateUrlAndClientIdAreBuiltIn()
	{
		Assert.Equal(Config.DEFAULT_UPDATE_URL, Config.UpdateUrl());
		Assert.Equal(Config.DEFAULT_MS_CLIENT_ID, Config.MsClientId());
	}
}
