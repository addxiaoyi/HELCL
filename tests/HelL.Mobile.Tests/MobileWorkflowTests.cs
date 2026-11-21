using System.Text.Json;
using HelL.Mobile.Ai;
using HelL.Mobile.Workflow;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class MobileWorkflowTests
{
    [Fact]
    public void TerminalTasksIgnoreLateProgressAndHistoryIsBounded()
    {
        var tasks = new MobileOperationHistory();
        Assert.False(tasks.HasFinished);
        Assert.Equal(0, tasks.ClearFinished());
        string first = tasks.Start("download", "instance");
        Assert.False(tasks.HasFinished);
        tasks.Update(first, "failed", "checksum failed");
        Assert.True(tasks.HasFinished);
        IReadOnlyList<string> confirmedIds = tasks.FinishedIds;
        tasks.Update(first, "running", "late progress");
        Assert.Equal("checksum failed", tasks.Entries.Single().Message);
        string second = tasks.Start("later task", "instance");
        tasks.Update(second, "completed", "done");
        string running = tasks.Start("still running", "instance");
        Assert.Equal(2, tasks.FinishedCount);
        Assert.Equal(1, tasks.ClearFinished(confirmedIds));
        Assert.Equal(new[] { running, second }, tasks.Entries.Select(item => item.Id));
        Assert.True(tasks.HasFinished);
        Assert.Equal(1, tasks.ClearFinished());
        Assert.False(tasks.HasFinished);
        Assert.Equal(running, tasks.Entries.Single().Id);
        for (int index = 0; index < 110; index++) tasks.Start("task-" + index, "instance");
        Assert.Equal(100, tasks.Entries.Count);
        Assert.Equal("task-109", tasks.Entries[0].Title);
    }

    [Fact]
    public void ChatRestoresInterruptedRepliesAndSupportsUndoClear()
    {
        string root = Path.Combine(Path.GetTempPath(), "hell-chat-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "chat.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new[] { new ChatEntry("user", "question", "complete"), new ChatEntry("assistant", "partial", "streaming") }));
            var chat = new MobileChat();
            chat.Restore(path);
            Assert.Equal("cancelled", chat.Entries.Last().State);
            chat.Clear();
            Assert.Empty(chat.Entries);
            Assert.True(chat.UndoClear());
            Assert.Equal(2, chat.Entries.Count);
            chat.Save(path);
            var restored = new MobileChat(); restored.Restore(path);
            Assert.Equal("partial", restored.Entries.Last().Text);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[{\"Role\":\"system\",\"Text\":\"untrusted\",\"State\":\"complete\"}]")]
    [InlineData("[{\"Role\":\"user\",\"Text\":null,\"State\":\"complete\"}]")]
    public void InvalidChatHistoryIsRejectedWithoutChangingTheOriginal(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), "hell-chat-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            var chat = new MobileChat();
            Assert.ThrowsAny<Exception>(() => chat.Restore(path));
            Assert.Empty(chat.Entries);
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptHistoryCannotBeOverwrittenByALaterSave()
    {
        string path = Path.Combine(Path.GetTempPath(), "hell-corrupt-chat-" + Guid.NewGuid().ToString("N") + ".json");
        const string corrupt = "{broken original history}";
        File.WriteAllText(path, corrupt);
        try
        {
            var chat = new MobileChat();
            Assert.ThrowsAny<Exception>(() => chat.Restore(path));
            Assert.Throws<InvalidOperationException>(() => chat.Save(path));
            Assert.Equal(corrupt, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitChatRecoveryPreservesCorruptBytesAndAllowsNewHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "hell-recover-chat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "chat.json");
        byte[] corrupt = "{unreadable original bytes}"u8.ToArray();
        File.WriteAllBytes(path, corrupt);
        UnixFileMode? mode = !OperatingSystem.IsWindows() ? File.GetUnixFileMode(path) : null;
        try
        {
            var chat = new MobileChat();
            Assert.ThrowsAny<Exception>(() => chat.Restore(path));
            Assert.True(chat.NeedsHistoryRecovery);
            string backup = chat.RecoverHistory(path);
            Assert.NotEqual(path, backup);
            Assert.Equal(corrupt, File.ReadAllBytes(backup));
            if (!OperatingSystem.IsWindows()) Assert.Equal(mode, File.GetUnixFileMode(backup));
            Assert.False(chat.NeedsHistoryRecovery);
            chat.Save(path);
            Assert.Equal("[]", File.ReadAllText(path));
            Assert.Equal(corrupt, File.ReadAllBytes(backup));
        }
        finally { Directory.Delete(root, true); }
    }
}
