namespace HelL.Mobile.Workflow;

internal sealed record MobileOperation(string Id, string Title, string Target, string State, string Message, DateTimeOffset Started)
{
    public string Caption => $"{Target} · {Started.ToLocalTime():HH:mm:ss} · {State switch { "running" => "进行中", "completed" => "已完成", "cancelled" => "已取消", _ => "失败" }}";
}

internal sealed class MobileOperationHistory
{
    private readonly List<MobileOperation> entries = [];
    internal IReadOnlyList<MobileOperation> Entries => entries.ToArray();
    internal string Start(string title, string target)
    {
        string id = Guid.NewGuid().ToString("N");
        entries.Insert(0, new(id, title, target, "running", "准备中…", DateTimeOffset.UtcNow));
        if (entries.Count > 100) entries.RemoveRange(100, entries.Count - 100);
        return id;
    }
    internal void Update(string id, string state, string message)
    {
        int index = entries.FindIndex(item => item.Id == id);
        if (index < 0 || entries[index].State != "running") return;
        entries[index] = entries[index] with { State = state, Message = message.Length <= 800 ? message : message[..800] };
    }
    internal void ClearFinished() => entries.RemoveAll(item => item.State != "running");
}
