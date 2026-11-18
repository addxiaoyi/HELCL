using HelL.Desktop.Models;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class DesktopTaskTests
{
    [Fact]
    public void CancellationRemainsActiveUntilTheOperationAcknowledgesIt()
    {
        var task = new DesktopTask("安装 Minecraft");
        task.Report("正在下载", 25);
        task.RequestCancellation();
        Assert.Equal(DesktopTaskState.Cancelling, task.State);
        Assert.True(task.IsActive);
        Assert.False(task.CanCancel);
        task.Finish(DesktopTaskState.Cancelled, "下载已取消");
        Assert.Equal(DesktopTaskState.Cancelled, task.State);
        Assert.False(task.IsActive);
        Assert.False(task.Indeterminate);
        Assert.NotNull(task.FinishedAt);
        Assert.Equal("下载已取消", task.Message);
    }

    [Fact]
    public void AGameCanDisableCancellationWhileKeepingItsProgressActive()
    {
        var task = new DesktopTask("运行 Minecraft");
        task.SetCancellable(false);
        task.RequestCancellation();
        Assert.Equal(DesktopTaskState.Running, task.State);
        Assert.False(task.CanCancel);
        task.Finish(DesktopTaskState.Completed, "游戏已退出");
        Assert.Equal(100, task.Percent);
    }

    [Fact]
    public void DelayedProgressCannotChangeAFinishedTask()
    {
        var task = new DesktopTask("下载资源");
        task.Finish(DesktopTaskState.Failed, "校验失败");
        DateTimeOffset? finished = task.FinishedAt;
        int logs = task.Log.Count;
        task.Report("旧进度", 80);
        task.Finish(DesktopTaskState.Completed, "错误的完成回调");
        task.RequestCancellation();
        Assert.Equal(DesktopTaskState.Failed, task.State);
        Assert.Equal("校验失败", task.Message);
        Assert.Equal(finished, task.FinishedAt);
        Assert.Equal(logs, task.Log.Count);
    }

    [Fact]
    public void LogKeepsRecentMessagesAndDoesNotRepeatTheSameStatus()
    {
        var task = new DesktopTask("安装 Minecraft");
        for (int i = 0; i < 200; i++) task.Report("文件 " + i, i);
        task.Report("文件 199", 100);
        Assert.Equal(160, task.Log.Count);
        Assert.EndsWith("文件 40", task.Log[0]);
        Assert.EndsWith("文件 199", task.Log[^1]);
        Assert.Equal(100, task.Percent);
    }

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(130, 100)]
    public void ProgressIsClampedToTheVisibleRange(double supplied, double expected)
    {
        var task = new DesktopTask("下载资源");
        task.Report("下载中", supplied);
        Assert.Equal(expected, task.Percent);
        Assert.False(task.Indeterminate);
        task.Report("连接中");
        Assert.True(task.Indeterminate);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteProgressCannotEnterTheUi(double supplied)
    {
        var task = new DesktopTask("下载资源");
        Assert.Throws<ArgumentOutOfRangeException>(() => task.Report("下载中", supplied));
    }

    [Theory]
    [InlineData(DesktopTaskState.Running)]
    [InlineData(DesktopTaskState.Cancelling)]
    [InlineData((DesktopTaskState)20)]
    public void FinishRequiresATerminalState(DesktopTaskState state)
    {
        var task = new DesktopTask("下载资源");
        Assert.Throws<ArgumentOutOfRangeException>(() => task.Finish(state, "结束"));
        Assert.True(task.IsActive);
    }
}
