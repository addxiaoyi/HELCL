using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class VisionCapsTests
{
	private const string ModelsDev = "{\"a\": {\"models\": {\"vl-1\": {\"modalities\": {\"input\": [\"text\", \"image\"]}}, \"text-1\": {\"modalities\": {\"input\": [\"text\"]}}, \"mixed\": {\"modalities\": {\"input\": [\"text\", \"image\"]}}}}, \"b\": {\"models\": {\"org/vl-1\": {\"modalities\": {\"input\": [\"text\", \"image\"]}}, \"mixed\": {\"modalities\": {\"input\": [\"text\"]}}}}, \"c\": {\"models\": {\"mixed:free\": {\"modalities\": {\"input\": [\"text\"]}}}}}";

	private const string OpenRouter = "{\"data\": [{\"id\": \"x/eye\", \"architecture\": {\"input_modalities\": [\"text\", \"image\"]}}, {\"id\": \"x/blind\", \"architecture\": {\"input_modalities\": [\"text\"]}}]}";

	[Theory]
	[InlineData("openai/GPT-4o:free", "gpt-4o")]
	[InlineData("  qwen-vl-max ", "qwen-vl-max")]
	[InlineData("Qwen/Qwen2.5-VL-72B-Instruct", "qwen2.5-vl-72b-instruct")]
	[InlineData("", "")]
	public void Normalize(string input, string expected)
	{
		Assert.Equal(expected, VisionCaps.Normalize(input));
	}

	[Fact]
	public void ModelsDevVote()
	{
		Assert.True(VisionCaps.VoteModelsDev("vl-1", ModelsDev));
		Assert.False(VisionCaps.VoteModelsDev("text-1", ModelsDev));
		// 1 票支持、2 票不支持 → 不支持
		Assert.False(VisionCaps.VoteModelsDev("mixed", ModelsDev));
		Assert.Null(VisionCaps.VoteModelsDev("unknown", ModelsDev));
		Assert.Null(VisionCaps.VoteModelsDev("vl-1", "not json"));
	}

	[Fact]
	public void OpenRouterVote()
	{
		Assert.True(VisionCaps.VoteOpenRouter("eye", OpenRouter));
		Assert.False(VisionCaps.VoteOpenRouter("blind", OpenRouter));
		Assert.Null(VisionCaps.VoteOpenRouter("none", OpenRouter));
	}

	[Fact]
	public void ManualModeWins()
	{
		using TestHome home = new TestHome();
		Assert.Equal(VisionCaps.ModeAuto, VisionCaps.Mode);
		VisionCaps.Mode = VisionCaps.ModeOn;
		VisionCaps.Result on = VisionCaps.Detect(new HelL.Core.PyDict(), force: false);
		Assert.True(on.Supported);
		Assert.Equal("manual", on.Source);
		VisionCaps.Mode = VisionCaps.ModeOff;
		Assert.False(VisionCaps.Detect(new HelL.Core.PyDict(), force: true).Supported);
		VisionCaps.Mode = "garbage";
		Assert.Equal(VisionCaps.ModeAuto, VisionCaps.Mode);
	}
}
