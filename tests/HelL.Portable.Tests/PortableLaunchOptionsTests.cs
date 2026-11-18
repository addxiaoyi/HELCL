using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class PortableLaunchOptionsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-options-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void OptionsRemainScopedToTheSelectedInstanceAcrossRestart()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("生存世界");
        launcher.CreateInstance("建筑世界");
        var chosen = new PortableLaunchOptions(6144, 1600, 900);
        launcher.SaveLaunchOptions("生存世界", chosen);
        var reopened = new PortableLauncher(root);
        Assert.Equal(chosen, reopened.LaunchOptions("生存世界"));
        Assert.Equal(new PortableLaunchOptions(), reopened.LaunchOptions("建筑世界"));
    }

    [Fact]
    public void InvalidEditDoesNotReplacePreviousSettings()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        var chosen = new PortableLaunchOptions(4096, 1280, 720);
        launcher.SaveLaunchOptions("实例", chosen);
        Assert.Throws<ArgumentOutOfRangeException>(() => launcher.SaveLaunchOptions("实例", chosen with { MemoryMb = 511 }));
        Assert.Equal(chosen, launcher.LaunchOptions("实例"));
    }

    [Theory]
    [InlineData(0, 960, 540)]
    [InlineData(262145, 960, 540)]
    [InlineData(2048, 319, 540)]
    [InlineData(2048, 960, 239)]
    [InlineData(2048, 16385, 540)]
    public void InvalidBoundsAreRejected(int memory, int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PortableLaunchOptions(memory, width, height).Validate());

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Schema\":2,\"Options\":{\"MemoryMb\":2048,\"Width\":960,\"Height\":540}}")]
    [InlineData("{\"Schema\":1,\"Options\":{\"MemoryMb\":0,\"Width\":960,\"Height\":540}}")]
    public void CorruptSettingsArePreservedAndReported(string json)
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        string path = Path.Combine(launcher.InstanceFolder("实例"), "portable-launch.json");
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => launcher.LaunchOptions("实例"));
        Assert.Equal(json, File.ReadAllText(path));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
