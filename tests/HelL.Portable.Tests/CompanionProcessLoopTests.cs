using HelL.Companion;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CompanionProcessLoopTests
{
    [Fact]
    public async Task AnUnexpectedHostCancellationCannotBecomeASuccessfulExit()
    {
        using var stopped = new CancellationTokenSource();
        Task controls = Task.Delay(Timeout.InfiniteTimeSpan, stopped.Token);
        Task hosting = Task.FromCanceled(new CancellationToken(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CompanionProcessLoop.WaitForExitAsync(controls, hosting, stopped));
        Assert.True(stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task ARequestedStopOrEndOfInputCanExitWithoutAnError()
    {
        using var stopped = new CancellationTokenSource();
        Task hosting = Task.Delay(Timeout.InfiniteTimeSpan, stopped.Token);
        await CompanionProcessLoop.WaitForExitAsync(Task.CompletedTask, hosting, stopped);
        Assert.True(stopped.IsCancellationRequested);
    }

    [Fact]
    public async Task CancellingAConsoleStyleSynchronousReadDoesNotBlockHelperShutdown()
    {
        using var reader = new BlockingReader();
        using var stopped = new CancellationTokenSource();
        Task<string?> reading = Task.Run(() => CompanionProcessLoop.ReadLineAsync(reader, stopped.Token));
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopped.Cancel();
        try
        {
            Assert.Same(reading, await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2))));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        }
        finally
        {
            reader.Release.TrySetResult();
            try { await reading; } catch (OperationCanceledException) { }
        }
    }

    private sealed class BlockingReader : TextReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string? ReadLine()
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return null;
        }
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());
    }
}
