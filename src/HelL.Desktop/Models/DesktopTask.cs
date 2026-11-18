using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HelL.Desktop.Models;

public enum DesktopTaskState { Running, Cancelling, Completed, Failed, Cancelled }

public sealed partial class DesktopTask : INotifyPropertyChanged
{
    private const int MaxLogEntries = 160;
    private DesktopTaskState state = DesktopTaskState.Running;
    private string message = "准备中…";
    private double percent;
    private bool indeterminate = true;
    private bool cancellable = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public string StartedLabel => StartedAt.ToString("HH:mm:ss");
    public DateTimeOffset? FinishedAt { get; private set; }
    public ObservableCollection<string> Log { get; } = [];
    public DesktopTaskState State => state;
    public string StateLabel => state switch
    {
        DesktopTaskState.Running => "进行中",
        DesktopTaskState.Cancelling => "取消中",
        DesktopTaskState.Completed => "已完成",
        DesktopTaskState.Failed => "失败",
        _ => "已取消"
    };
    public string Message => message;
    public double Percent => percent;
    public bool Indeterminate => indeterminate;
    public bool IsActive => state is DesktopTaskState.Running or DesktopTaskState.Cancelling;
    public bool CanCancel => cancellable && state == DesktopTaskState.Running;

    public DesktopTask(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("任务名称不能为空。", nameof(title));
        Title = title;
    }

    public void Report(string text, double? progress = null)
    {
        if (!IsActive) return;
        if (progress.HasValue && !double.IsFinite(progress.Value))
            throw new ArgumentOutOfRangeException(nameof(progress), "任务进度必须是有限数值。");
        if (!string.IsNullOrWhiteSpace(text) && message != text)
        {
            message = text;
            if (Log.Count == MaxLogEntries) Log.RemoveAt(0);
            Log.Add($"{DateTime.Now:HH:mm:ss}  {text}");
            Changed(nameof(Message));
        }
        indeterminate = !progress.HasValue;
        percent = progress.HasValue ? Math.Clamp(progress.Value, 0, 100) : percent;
        Changed(nameof(Indeterminate));
        Changed(nameof(Percent));
    }

    public void SetCancellable(bool enabled)
    {
        cancellable = enabled;
        Changed(nameof(CanCancel));
    }

    public void RequestCancellation()
    {
        if (!CanCancel) return;
        SetState(DesktopTaskState.Cancelling);
        Report("正在取消…");
    }

    public void Finish(DesktopTaskState terminal, string text)
    {
        if (!IsActive) return;
        if (terminal is not (DesktopTaskState.Completed or DesktopTaskState.Failed or DesktopTaskState.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(terminal), "结束状态必须是完成、失败或取消。");
        Report(text, terminal == DesktopTaskState.Completed ? 100 : null);
        FinishedAt = DateTimeOffset.Now;
        SetState(terminal);
        indeterminate = false;
        Changed(nameof(Indeterminate));
        Changed(nameof(FinishedAt));
    }

    private void SetState(DesktopTaskState next)
    {
        state = next;
        Changed(nameof(State));
        Changed(nameof(StateLabel));
        StateAppearanceChanged();
        Changed(nameof(IsActive));
        Changed(nameof(CanCancel));
    }

    partial void StateAppearanceChanged();

    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
