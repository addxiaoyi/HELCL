using Avalonia.Controls;
using Avalonia.Interactivity;
using HelL.Core.Diagnostics;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    public async Task RunDiagnosticWorkflowPreviewAsync()
    {
        OpenDiagnostics(this, new RoutedEventArgs());
        for (int attempt = 0; attempt < 100 && (diagnosticBusy || diagnosticReport is null); attempt++) await Task.Delay(50);
        if (diagnosticReport is null || diagnosticBusy) throw new IOException("The diagnostic workflow did not initialize.");

        string preview = diagnosticPreview.Text ?? "";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int saves = 0;
        string exported = "";
        Task saving = ExportDiagnosticPackageAsync(async (report, cancellation) =>
        {
            saves++;
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellation);
            exported = report.ToJson();
            return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            if (!diagnosticBusy || diagnosticFields.IsEnabled || diagnosticChecks.IsEnabled)
                throw new IOException("Diagnostic fields remained editable during an export.");
            await ExportDiagnosticPackageAsync((_, _) => { saves++; return Task.FromResult(true); });
            if (saves != 1) throw new IOException("A second diagnostic export ran concurrently.");
            if (diagnosticPreview.Text != preview) throw new IOException("The diagnostic preview changed during export.");
        }
        finally { release.TrySetResult(); await saving; }
        if (exported != preview || diagnosticBusy) throw new IOException("The exported diagnostic bundle differs from its frozen preview.");

        await ExportDiagnosticPackageAsync((_, _) => Task.FromException<bool>(new IOException("controlled diagnostic save failure")));
        if (diagnosticBusy || diagnosticReport is null) throw new IOException("An export failure prevented a retry.");
        await ExportDiagnosticPackageAsync((_, _) => Task.FromResult(false));
        if (diagnosticBusy || diagnosticReport is null) throw new IOException("A cancelled file picker prevented a retry.");
        await ExportDiagnosticPackageAsync((report, token) => SaveFixture(report, token));

        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task interrupted = ExportDiagnosticPackageAsync(async (_, cancellation) =>
        {
            waiting.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            return true;
        });
        await waiting.Task;
        ShowPage("home");
        await interrupted.WaitAsync(TimeSpan.FromSeconds(2));
        if (diagnosticBusy || diagnosticPage?.IsVisible == true || !Pages.IsEnabled)
            throw new IOException("Leaving diagnostics did not cancel its active export.");

        await session.Operations.WaitAsync();
        try
        {
            ShowPage("diagnostics");
            Task queued = RefreshDiagnosticPreview();
            if (!diagnosticBusy || !Pages.IsEnabled) throw new IOException("Diagnostic collection blocked navigation while waiting for the owner.");
            CancelDiagnosticOperation(this, new RoutedEventArgs());
            await queued.WaitAsync(TimeSpan.FromSeconds(2));
            if (diagnosticBusy || session.Operations.CurrentCount != 0)
                throw new IOException("Cancelled diagnostic collection released another operation's owner lock.");
        }
        finally { session.Operations.Release(); }
        await RefreshDiagnosticPreview();
        if (diagnosticReport is null || diagnosticBusy) throw new IOException("Diagnostic collection did not recover after cancellation.");

        using (var replacement = new MobileHome())
        {
            replacement.ShowPage("diagnostics");
            replacement.diagnosticReport = diagnosticReport;
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken exportToken = default;
            Task late = replacement.ExportDiagnosticPackageAsync(async (_, cancellation) =>
            {
                exportToken = cancellation;
                await pending.Task;
                return true;
            });
            string beforeDispose = replacement.diagnosticStatus.Text ?? "";
            replacement.Dispose();
            pending.TrySetResult();
            await late;
            if (!exportToken.IsCancellationRequested || replacement.diagnosticStatus.Text != beforeDispose || replacement.diagnosticBusy)
                throw new IOException("Disposed diagnostics accepted a late export result.");
        }

        var exportFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task staleExport = ExportDiagnosticPackageAsync(async (_, _) =>
        {
            await exportFailure.Task;
            throw new IOException("controlled late document-provider failure");
        });
        ShowPage("home");
        string exportNotice = diagnosticStatus.Text ?? "";
        exportFailure.TrySetResult();
        await staleExport;
        bool exportChangedHiddenStatus = diagnosticStatus.Text != exportNotice;

        ShowPage("diagnostics");
        var collectionFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task staleCollection = RefreshDiagnosticPreviewAsync(async _ =>
        {
            await collectionFailure.Task;
            throw new IOException("controlled late system-check failure");
        });
        ShowPage("home");
        string collectionNotice = diagnosticStatus.Text ?? "";
        collectionFailure.TrySetResult();
        await staleCollection;
        bool collectionChangedHiddenStatus = diagnosticStatus.Text != collectionNotice;
        if (exportChangedHiddenStatus || collectionChangedHiddenStatus)
            throw new IOException($"Late diagnostic failures updated a hidden view (export={exportChangedHiddenStatus}, collection={collectionChangedHiddenStatus}).");

        File.WriteAllText(Path.Combine(EvidenceRoot, "diagnostic-workflow-preview.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = true, singleExport = saves == 1, immutablePreview = exported == preview,
            retryAfterFailure = true, retryAfterPickerCancellation = true,
            cancelOnNavigation = true, cancelledOwnerWait = true, noLateDisposeResult = true,
            noLateExportFailure = true, noLateCollectionFailure = true,
            desktopHosted = true, nativeAndroidVerified = false
        }));

        async Task<bool> SaveFixture(DiagnosticReport report, CancellationToken cancellation)
        {
            await report.SavePackageAsync(Path.Combine(EvidenceRoot, "diagnostic-workflow-preview.zip"), cancellation);
            return true;
        }
    }
}
