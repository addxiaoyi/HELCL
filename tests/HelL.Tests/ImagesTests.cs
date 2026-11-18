using System;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class ImagesTests
{
	[Fact]
	public void SniffMime()
	{
		Assert.Equal("image/png", Images.SniffMime(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }));
		Assert.Equal("image/jpeg", Images.SniffMime(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }));
		Assert.Equal("image/gif", Images.SniffMime(System.Text.Encoding.ASCII.GetBytes("GIF89a......")));
	}

	[Fact]
	public void StripImagesKeepsText()
	{
		var content = new System.Collections.Generic.List<object>
		{
			HelL.Core.PyDict.Of("type", "text", "text", "看看"),
			HelL.Core.PyDict.Of("type", "image_url", "image_url", HelL.Core.PyDict.Of("url", "data:image/png;base64,AAAA"))
		};
		object stripped = Images.StripImages(content);
		string s = stripped is string str ? str : HelL.Core.PyJson.Dumps(stripped);
		Assert.Contains("看看", s);
		Assert.DoesNotContain("base64", s);
	}
}
