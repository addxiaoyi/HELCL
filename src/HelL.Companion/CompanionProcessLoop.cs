namespace HelL.Companion;

public static class CompanionProcessLoop
{
    public static async Task WaitForExitAsync(Task controls, Task hosting, CancellationTokenSource stopped)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(hosting);
        ArgumentNullException.ThrowIfNull(stopped);
        Task completed = await Task.WhenAny(controls, hosting).ConfigureAwait(false);
        bool expectedStop = stopped.IsCancellationRequested || completed.IsCompletedSuccessfully;
        stopped.Cancel();
        try { await Task.WhenAll(controls, hosting).ConfigureAwait(false); }
        catch (OperationCanceledException) when (expectedStop) { }
    }

    public static async Task<string?> ReadLineAsync(TextReader input, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellation.ThrowIfCancellationRequested();
        // Console readers may block synchronously. Cancellation is terminal for this helper input.
        Task<string?> reading = Task.Run(input.ReadLine);
        _ = reading.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await reading.WaitAsync(cancellation).ConfigureAwait(false);
    }
}
