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
        string first = tasks.Start("download", "instance");
        tasks.Update(first, "failed", "checksum failed");
        tasks.Update(first, "running", "late progress");
        Assert.Equal("checksum failed", tasks.Entries.Single().Message);
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
}
