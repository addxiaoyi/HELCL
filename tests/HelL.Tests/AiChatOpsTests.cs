using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher.Ai;
using HelL.Pages.Ai;
using Xunit;

namespace HelL.Tests;

/// <summary>AI 页斜杠命令、编辑重发、任务卡、MCP 状态灯用到的纯逻辑。</summary>
public class AiChatOpsTests
{
	[Fact]
	public void RequiredSlashCommandsExist()
	{
		string[] names = AiChatOps.Commands.Select(c => c.Name).ToArray();
		foreach (string n in new[] { "崩溃分析", "扫模组", "新实例", "新对话", "清空" })
		{
			Assert.Contains(n, names);
		}
		Assert.Equal("new_chat", AiChatOps.ExactSlash("/新对话").Local);
		Assert.Equal("clear", AiChatOps.ExactSlash("/清空").Local);
		Assert.Equal("", AiChatOps.ExactSlash("/崩溃分析").Local);
		Assert.Equal("刚才启动失败了，读一下日志告诉我原因", AiChatOps.ExactSlash("/崩溃分析").Prompt);
		Assert.Equal("扫一下当前实例的模组冲突", AiChatOps.ExactSlash("/扫模组").Prompt);
	}

	[Fact]
	public void SlashAloneListsAll()
	{
		Assert.Equal(AiChatOps.Commands.Count, AiChatOps.MatchSlash("/").Count);
		Assert.Equal(AiChatOps.Commands.Count, AiChatOps.MatchSlash("／").Count);
	}

	[Fact]
	public void SlashPrefixAndAliasMatch()
	{
		Assert.Equal("崩溃分析", AiChatOps.MatchSlash("/崩").First().Name);
		Assert.Equal("新对话", AiChatOps.MatchSlash("/new").First().Name);
		Assert.Equal("清空", AiChatOps.MatchSlash("/CLE").First().Name);
		List<SlashCommand> xin = AiChatOps.MatchSlash("/新");
		Assert.Contains(xin, c => c.Name == "新实例");
		Assert.Contains(xin, c => c.Name == "新对话");
	}

	[Fact]
	public void SlashContainsMatchComesAfterPrefix()
	{
		List<SlashCommand> list = AiChatOps.MatchSlash("/模组");
		Assert.NotEmpty(list);
		Assert.Equal("扫模组", list[0].Name);
	}

	[Fact]
	public void NonSlashInputDoesNotMatch()
	{
		Assert.Empty(AiChatOps.MatchSlash("帮我装模组"));
		Assert.Empty(AiChatOps.MatchSlash("/新 对话"));
		Assert.Empty(AiChatOps.MatchSlash(""));
		Assert.Empty(AiChatOps.MatchSlash(null));
		Assert.Empty(AiChatOps.MatchSlash("/zzzz"));
		Assert.Null(AiChatOps.ExactSlash("/清"));
		Assert.Null(AiChatOps.ExactSlash("清空"));
		Assert.Null(AiChatOps.ExactSlash("/"));
	}

	[Fact]
	public void MoveSelectionWraps()
	{
		Assert.Equal(1, AiChatOps.MoveSelection(0, 1, 3));
		Assert.Equal(0, AiChatOps.MoveSelection(2, 1, 3));
		Assert.Equal(2, AiChatOps.MoveSelection(0, -1, 3));
		Assert.Equal(0, AiChatOps.MoveSelection(-1, 1, 3));
		Assert.Equal(2, AiChatOps.MoveSelection(-1, -1, 3));
		Assert.Equal(-1, AiChatOps.MoveSelection(0, 1, 0));
	}

	private static List<(string Role, string Id, string Content)> Chat() => new List<(string, string, string)>
	{
		("user", "u1", "装 1.20.1"),
		("assistant", "a1", "好的"),
		("user", "u2", "再装钠"),
		("user", "steer_1", "顺便装 Iris"),
		("assistant", "a2", "装好了"),
		("user", "u3", "启动"),
		("assistant", "a3", "已启动")
	};

	[Fact]
	public void RoundsToRewindCountsNonSteerUserMessages()
	{
		Assert.Equal(1, AiChatOps.RoundsToRewind(Chat(), "启动"));
		Assert.Equal(2, AiChatOps.RoundsToRewind(Chat(), "再装钠"));
		Assert.Equal(3, AiChatOps.RoundsToRewind(Chat(), " 装 1.20.1 "));
	}

	[Fact]
	public void RoundsToRewindRejectsMissingOrSteer()
	{
		Assert.Equal(-1, AiChatOps.RoundsToRewind(Chat(), "不存在"));
		Assert.Equal(-1, AiChatOps.RoundsToRewind(Chat(), "顺便装 Iris"));
		Assert.Equal(-1, AiChatOps.RoundsToRewind(null, "启动"));
		Assert.Equal(-1, AiChatOps.RoundsToRewind(Chat(), "好的"));
	}

	[Fact]
	public void RoundsToRewindPicksOccurrenceFromEnd()
	{
		var chat = new List<(string Role, string Id, string Content)>
		{
			("user", "u1", "继续"),
			("assistant", "a1", "1"),
			("user", "u2", "继续"),
			("assistant", "a2", "2")
		};
		Assert.Equal(1, AiChatOps.RoundsToRewind(chat, "继续", 0));
		Assert.Equal(2, AiChatOps.RoundsToRewind(chat, "继续", 1));
		Assert.Equal(-1, AiChatOps.RoundsToRewind(chat, "继续", 2));
	}

	[Fact]
	public void ForgottenAndInstalledVersionParsing()
	{
		Assert.Equal("显卡是 RTX 3060；内存 8G", AiChatOps.ForgottenOf("已删除 2 条: 显卡是 RTX 3060；内存 8G"));
		Assert.Null(AiChatOps.ForgottenOf("没有包含「显卡」的记忆"));
		Assert.Equal("1.20.1-fabric-0.15.11", AiChatOps.InstalledVersionOf("版本安装完成: 1.20.1-fabric-0.15.11"));
		Assert.Equal("", AiChatOps.InstalledVersionOf("任务完成"));
		Assert.Equal("", AiChatOps.InstalledVersionOf(null));
	}

	[Fact]
	public void McpNoteToolCallRaisesEvent()
	{
		string seen = null;
		Action<string, DateTime> handler = (name, _) => seen = name;
		McpServer.ToolCalled += handler;
		try
		{
			McpServer.NoteToolCall("list_instances");
		}
		finally
		{
			McpServer.ToolCalled -= handler;
		}
		Assert.Equal("list_instances", seen);
		Assert.Equal("list_instances", McpServer.LastTool);
		Assert.True((DateTime.Now - McpServer.LastToolAt).TotalMinutes < 1);
	}
}
