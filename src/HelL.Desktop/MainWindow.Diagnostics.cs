using HelL.Core.Diagnostics;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    internal IReadOnlyList<DiagnosticIssue> DiagnosticIssues => TasksView.History
        .Where(task => task.State == Models.DesktopTaskState.Failed).Take(8)
        .Select(_ => new DiagnosticIssue("task_failed", "A launcher task failed.")).ToArray();
}
