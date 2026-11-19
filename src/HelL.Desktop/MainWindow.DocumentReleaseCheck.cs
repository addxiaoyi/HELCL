using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core;
using HelL.Desktop.Controls;
using HelL.Portable;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckDocumentReleaseBinding()
    {
        ShowPage("resources");
        var picker = ResourcesView.FindControl<ComboBox>("ReleasePicker")!;
        var documents = ResourcesView.FindControl<StackPanel>("ProjectDocuments")!;
        var project = new CatalogProject("document-fixture", "document-fixture", "modrinth", "resourcepack",
            "Document binding fixture", "fixture", "fixture", "", "", "", 0);
        var first = new CatalogRelease("first", "First file", "first.zip", [], [], "", 0, new PyDict());
        var second = new CatalogRelease("second", "Second file", "second.zip", [], [], "", 0, new PyDict());
        ResourcesView.ReleasesFixture = _ => Task.FromResult<IReadOnlyList<CatalogRelease>>([first, second]);
        try
        {
            foreach ((bool lateFailure, bool returnToFirst) in new[] { (false, false), (true, false), (false, true) })
            {
                ResourcesView.PresentProject(project);
                var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var firstReply = new TaskCompletionSource<CatalogProjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
                var secondReply = new TaskCompletionSource<CatalogProjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
                int requests = 0;
                ResourcesView.DetailsFixture = _ =>
                {
                    if (++requests == 1) { firstEntered.TrySetResult(); return firstReply.Task; }
                    secondEntered.TrySetResult();
                    return secondReply.Task;
                };
                Task loading = ResourcesView.LoadReleases();
                await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                picker.SelectedItem = second;
                if (returnToFirst) picker.SelectedItem = first;
                if (lateFailure) firstReply.SetException(new IOException("late first-file failure"));
                else firstReply.SetResult(new CatalogProjectDetails([], "First file body", "First file changelog", ""));
                await loading;
                if (VisibleDocuments().Contains("First file changelog", StringComparison.Ordinal) || documents.Children.OfType<Button>().Any())
                    throw new IOException("The old file's document or failure was shown for the newly selected file.");
                await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                string expected = returnToFirst ? "First file reselected changelog" : "Second file changelog";
                secondReply.SetResult(new CatalogProjectDetails([], "Current file body", expected, ""));
                await WaitForDocument(expected);
            }

            ResourcesView.DetailsFixture = _ => Task.FromResult(new CatalogProjectDetails([], "", "First file ready", ""));
            ResourcesView.PresentProject(project);
            await ResourcesView.LoadReleases();
            var idleRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ResourcesView.DetailsFixture = _ =>
            {
                idleRequest.TrySetResult();
                return Task.FromResult(new CatalogProjectDetails([], "", "Second file after busy task", ""));
            };
            var taskGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task unrelated = RunTask(_ => taskGate.Task, "DocumentBindingFixture");
            picker.SelectedItem = second;
            bool oldTextVisible = VisibleDocuments().Contains("First file ready", StringComparison.Ordinal);
            taskGate.SetResult();
            await unrelated;
            if (oldTextVisible) throw new IOException("Changing a file left the old file's changelog visible during unrelated work.");
            await idleRequest.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitForDocument("Second file after busy task");

            ResourcesView.PresentProject(project);
            var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ResourcesView.DetailsFixture = token =>
            {
                cancellationEntered.TrySetResult();
                return new TaskCompletionSource<CatalogProjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(token);
            };
            Task cancelled = ResourcesView.LoadReleases();
            await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            CancelTask(this, new RoutedEventArgs());
            await cancelled;
            if (documents.Children.OfType<Button>().SingleOrDefault()?.Content is not string retryLabel || !retryLabel.Contains("已取消", StringComparison.Ordinal))
                throw new IOException("Cancelling the current file's document left an indefinite loading state.");

            File.WriteAllText(Path.Combine(Program.DataRoot, "document-release-binding.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = true, lateReplyRejected = true, lateFailureRejected = true,
                latestFileLoadedAfterBusy = true, oldFileTextClearedOnSelection = true,
                reselectedFileUsesFreshRequest = true, cancelledDocumentCanRetry = true
            }));
        }
        finally { ResourcesView.ReleasesFixture = null; ResourcesView.DetailsFixture = null; }

        string VisibleDocuments() => string.Join("\n", documents.Children.OfType<Expander>()
            .Select(expander => ((expander.Content as ScrollViewer)?.Content as MessageDocument)?.Text ?? ""));

        async Task WaitForDocument(string expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (IsWorking || !VisibleDocuments().Contains(expected, StringComparison.Ordinal))
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("The current file's document did not load: " + expected);
                await Task.Delay(10);
            }
        }
    }
}
