using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages.Ai;

internal static class AiSelfTest
{
	private static readonly List<string> Log = new List<string>();

	private static int _fail;

	private const string Answer = "找到原因了：**Sodium Extra 0.4.18 和 Sodium 0.5.8 不兼容**，进世界时调用了新版 Sodium 已删除的方法。\n\n| 模组 | 当前版本 | 建议 |\n|---|---|---|\n| Sodium | `0.5.8` | 保留 |\n| Sodium Extra | `0.4.18` | 升级到 `0.5.7` |\n| Iris | `1.7.0` | 保留 |\n\n我可以直接替换，替换前会备份旧文件，出问题能一键撤回。";

	public static int Run(string[] args)
	{
		string text = Path.Combine(Environment.CurrentDirectory, "_ai_ui_selftest");
		int num = Array.IndexOf<string>(args, "--ai-ui-selftest");
		if (num >= 0 && num + 1 < args.Length && !args[num + 1].StartsWith("--", StringComparison.Ordinal))
		{
			text = args[num + 1];
		}
		Directory.CreateDirectory(text);
		bool flag = args.Contains<string>("--dark");
		try
		{
			Motion.Enabled = false;
			if (flag)
			{
				App.ApplyTheme(dark: true);
			}
			RunAll(text, flag ? "dark-" : "");
		}
		catch (Exception ex)
		{
			Fail("未捕获异常", ex.ToString());
		}
		Log.Add("");
		Log.Add((_fail == 0) ? "ALL PASSED" : $"FAILED: {_fail}");
		File.WriteAllText(Path.Combine(text, "result.txt"), string.Join(Environment.NewLine, Log), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		try
		{
			Console.WriteLine(string.Join(Environment.NewLine, Log));
		}
		catch
		{
		}
		return _fail;
	}

	private static void Check(string name, bool ok, string detail = "")
	{
		if (ok)
		{
			Log.Add("PASS  " + name);
		}
		else
		{
			Fail(name, detail);
		}
	}

	private static void Fail(string name, string detail)
	{
		_fail++;
		Log.Add("FAIL  " + name + ((detail.Length > 0) ? (" — " + detail) : ""));
	}

	private static void Pump(int ms = 120)
	{
		DateTime dateTime = DateTime.Now.AddMilliseconds(ms);
		do
		{
			DispatcherFrame frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
			{
				frame.Continue = false;
			});
			Dispatcher.PushFrame(frame);
		}
		while (DateTime.Now < dateTime);
	}

	private static void Shot(Window w, string dir, string name)
	{
		try
		{
			Pump(200);
			FrameworkElement frameworkElement = (FrameworkElement)w.Content;
			frameworkElement.UpdateLayout();
			double num = 1.5;
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap((int)(frameworkElement.ActualWidth * num), (int)(frameworkElement.ActualHeight * num), 96.0 * num, 96.0 * num, PixelFormats.Pbgra32);
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				drawingContext.DrawRectangle((Brush)(frameworkElement.TryFindResource("B.Canvas") ?? Brushes.White), null, new Rect(0.0, 0.0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
				drawingContext.DrawRectangle(new VisualBrush(frameworkElement), null, new Rect(0.0, 0.0, frameworkElement.ActualWidth, frameworkElement.ActualHeight));
			}
			renderTargetBitmap.Render(drawingVisual);
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
			using FileStream stream = File.Create(Path.Combine(dir, name + ".png"));
			pngBitmapEncoder.Save(stream);
			Log.Add("SHOT  " + name + ".png");
		}
		catch (Exception ex)
		{
			Fail("截图 " + name, ex.Message);
		}
	}

	private static BridgeEvent Status(string kind, string name = "", string label = "", object? payload = null)
	{
		return new BridgeEvent
		{
			Event = "ai.status",
			Kind = kind,
			Name = name,
			Label = label,
			Payload = J(payload ?? new
			{
				chat_id = "c1"
			})
		};
	}

	private static JsonElement J(object o)
	{
		return JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement.Clone();
	}

	private static AiStoreDto Store(bool empty)
	{
		return new AiStoreDto
		{
			ActiveId = (empty ? "c0" : "c1"),
			Chats = 
			{
				new AiChatDto
				{
					Id = "c0",
					Title = ""
				},
				new AiChatDto
				{
					Id = "c1",
					Title = "进世界崩溃排查"
				},
				new AiChatDto
				{
					Id = "c2",
					Title = "整合包性能优化"
				},
				new AiChatDto
				{
					Id = "c3",
					Title = "1.20.1 优化模组推荐"
				}
			}
		};
	}

	private static void RunAll(string dir, string prefix)
	{
		AiPage aiPage = new AiPage();
		Window window = new Window
		{
			Width = 1280.0,
			Height = 800.0,
			Content = aiPage,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ShowInTaskbar = false,
			Title = "HelL AI UI selftest"
		};
		window.SetResourceReference(Control.BackgroundProperty, "B.Canvas");
		window.Show();
		Pump();
		aiPage.SelfTestLoad(Store(empty: true));
		Pump();
		Check("空对话：显示欢迎页与 4 张快捷卡", aiPage.SelfTestMessages.Children.Count == 1 && FindAll<Button>(aiPage.SelfTestMessages).Count() == 4, $"children={aiPage.SelfTestMessages.Children.Count}");
		Check("空对话：输入区显示发送按钮", aiPage.SelfTestSendStop.Content == aiPage.SelfTestSend);
		Check("空对话：占位提示可见", aiPage.SelfTestPlaceholder.Visibility == Visibility.Visible && aiPage.SelfTestPlaceholder.Text.Length > 0);
		Check("历史栏：列出 4 个对话", FindAll<Button>(aiPage.SelfTestChatList).Count() == 4, $"n={FindAll<Button>(aiPage.SelfTestChatList).Count()}");
		Check("权限提示：默认「每次确认」", aiPage.SelfTestPermTip.Text.Contains("每次确认"), aiPage.SelfTestPermTip.Text);
		Shot(window, dir, prefix + "1-empty");
		aiPage.SelfTestLoad(Store(empty: false));
		aiPage.SelfTestUser("我的「生存整合包 1.20.1」一进世界就崩溃，帮我看看怎么回事");
		Check("发送后：按钮切成停止", aiPage.SelfTestSendStop.Content == aiPage.SelfTestStop);
		aiPage.OnEvent(Status("think"));
		Pump();
		string[][] array = new string[4][]
		{
			new string[2] { "get_crash_report", "读取崩溃报告：crash-2026-10-24_12.04.31" },
			new string[2] { "list_mods", "列出已装模组：生存整合包 1.20.1" },
			new string[2] { "scan_mod_conflicts", "扫描模组冲突：sodium · sodium-extra · iris" },
			new string[2] { "search_mods", "搜索模组：sodium extra · fabric 1.20.1" }
		};
		foreach (string[] array2 in array)
		{
			aiPage.OnEvent(Status("tool_start", array2[0], array2[1]));
			Pump(30);
			aiPage.OnEvent(Status("tool_done", array2[0], array2[1]));
		}
		Pump();
		StepGroup[] array3 = aiPage.SelfTestMessages.Children.OfType<StepGroup>().ToArray();
		Check("工具：连续 4 步合并成 1 组", array3.Length == 1 && array3[0].Count == 4, $"groups={array3.Length} count={array3.FirstOrDefault()?.Count}");
		ToolLine[] array4 = FindAll<ToolLine>(aiPage.SelfTestMessages).ToArray();
		Check("工具：4 行全部完成状态", array4.Length == 4 && array4.All((ToolLine l) => l.State == ToolState.Done));
		Check("工具：动作名与参数已拆开", array4.Length != 0 && array4[0].LabelText == "读取崩溃报告" && array4[0].ArgText.StartsWith("crash-"), (array4.Length != 0) ? (array4[0].LabelText + " | " + array4[0].ArgText) : "");
		Check("工具：标题不再带「完成：」前缀", array4.All((ToolLine l) => !l.LabelText.StartsWith("完成")));
		string text = "";
		for (int num = 0; num < "找到原因了：**Sodium Extra 0.4.18 和 Sodium 0.5.8 不兼容**，进世界时调用了新版 Sodium 已删除的方法。\n\n| 模组 | 当前版本 | 建议 |\n|---|---|---|\n| Sodium | `0.5.8` | 保留 |\n| Sodium Extra | `0.4.18` | 升级到 `0.5.7` |\n| Iris | `1.7.0` | 保留 |\n\n我可以直接替换，替换前会备份旧文件，出问题能一键撤回。".Length; num += 37)
		{
			string text2 = "找到原因了：**Sodium Extra 0.4.18 和 Sodium 0.5.8 不兼容**，进世界时调用了新版 Sodium 已删除的方法。\n\n| 模组 | 当前版本 | 建议 |\n|---|---|---|\n| Sodium | `0.5.8` | 保留 |\n| Sodium Extra | `0.4.18` | 升级到 `0.5.7` |\n| Iris | `1.7.0` | 保留 |\n\n我可以直接替换，替换前会备份旧文件，出问题能一键撤回。".Substring(num, Math.Min(37, "找到原因了：**Sodium Extra 0.4.18 和 Sodium 0.5.8 不兼容**，进世界时调用了新版 Sodium 已删除的方法。\n\n| 模组 | 当前版本 | 建议 |\n|---|---|---|\n| Sodium | `0.5.8` | 保留 |\n| Sodium Extra | `0.4.18` | 升级到 `0.5.7` |\n| Iris | `1.7.0` | 保留 |\n\n我可以直接替换，替换前会备份旧文件，出问题能一键撤回。".Length - num));
			text += text2;
			aiPage.OnEvent(new BridgeEvent
			{
				Event = "ai.delta",
				Text = text2,
				Payload = J(new
				{
					chat_id = "c1"
				})
			});
			Pump(15);
		}
		Pump(250);
		Bubble bubble = aiPage.SelfTestMessages.Children.OfType<Bubble>().LastOrDefault((Bubble b) => !b.IsMine);
		Check("回答：新起一个助手气泡（在工具组之后）", bubble != null && aiPage.SelfTestMessages.Children.IndexOf(bubble) > aiPage.SelfTestMessages.Children.IndexOf(array3.FirstOrDefault()));
		MdView mdView = ((bubble == null) ? null : FindAll<MdView>(bubble).FirstOrDefault());
		Check("回答：Markdown 渲染出表格", mdView != null && mdView.Document.Blocks.OfType<Table>().Count() == 1, (mdView == null) ? "no MdView" : $"blocks={mdView.Document.Blocks.Count}");
		Check("回答：粗体被渲染（不是原样 **）", mdView != null && !new TextRange(mdView.Document.ContentStart, mdView.Document.ContentEnd).Text.Contains("**"));
		Check("回答：工具组在回答开始后不再追加", array3[0].Count == 4);
		aiPage.OnEvent(new BridgeEvent
		{
			Event = "ai.done",
			Payload = J(new
			{
				chat_id = "c1",
				stop_reason = "",
				usage = new
				{
					input = 48200,
					output = 1200,
					requests = 3,
					real_requests = 3
				}
			})
		});
		Pump(300);
		Check("结束：按钮回到发送", aiPage.SelfTestSendStop.Content == aiPage.SelfTestSend);
		Check("结束：顶栏显示上下文占用", aiPage.SelfTestUsage.Text.Contains("%"), aiPage.SelfTestUsage.Text);
		Check("结束：超过 3 步的工具组自动收起", !array3[0].IsOpen, $"open={array3[0].IsOpen}");
		array3[0].SetOpen(open: true);
		aiPage.SelfTestScrollEnd();
		Shot(window, dir, prefix + "2-chat");
		aiPage.SelfTestUser("好，帮我换掉，顺便把渲染距离调到 12");
		aiPage.OnEvent(Status("tool", "write_mod_config", "修改配置：options.txt"));
		aiPage.OnEvent(new BridgeEvent
		{
			Event = "ai.confirm",
			Name = "write_mod_config",
			Label = "修改配置 options.txt",
			Payload = J(new
			{
				chat_id = "c1",
				reason = "把渲染距离从 8 调到 12，旧文件会备份为 options.txt.bak",
				preview = new
				{
					head = "第 14 行",
					lines = new string[2] { "- renderDistance:8", "+ renderDistance:12" }
				}
			})
		});
		Pump(250);
		ConfirmCard confirmCard = aiPage.SelfTestDock.Children.OfType<ConfirmCard>().FirstOrDefault();
		Check("确认：卡片停靠在输入框上方（不在消息流里）", confirmCard != null && !aiPage.SelfTestMessages.Children.OfType<ConfirmCard>().Any());
		Check("确认：风险分级为「写入」", confirmCard != null && confirmCard.Risk == AiRisk.Write, confirmCard?.Risk.ToString() ?? "");
		Check("确认：输入框提示改为先处理确认", aiPage.SelfTestPlaceholder.Text.Contains("确认"), aiPage.SelfTestPlaceholder.Text);
		aiPage.SelfTestScrollEnd();
		Shot(window, dir, prefix + "3-confirm");
		aiPage.OnEvent(new BridgeEvent
		{
			Event = "ai.confirm",
			Name = "delete_mod",
			Label = "删除 sodium-extra-0.4.18+mc1.20.1.jar",
			Payload = J(new
			{
				chat_id = "c1"
			})
		});
		Pump();
		ConfirmCard confirmCard2 = aiPage.SelfTestDock.Children.OfType<ConfirmCard>().FirstOrDefault();
		Check("确认：同时只保留一张卡", aiPage.SelfTestDock.Children.Count == 1);
		Check("确认：删除操作标红「删除」", confirmCard2 != null && confirmCard2.Risk == AiRisk.Delete);
		Shot(window, dir, prefix + "3b-confirm-delete");
		aiPage.SelfTestBusy(on: false);
		Pump();
		Check("回合结束：停靠区清空", aiPage.SelfTestDock.Children.Count == 0);
		aiPage.SelfTestLoad(Store(empty: false));
		aiPage.OnEvent(Status("compact"));
		aiPage.SelfTestUser("帮我把这个整合包优化到 100 帧以上，能装的都装上");
		aiPage.OnEvent(Status("plan", "", "", new
		{
			chat_id = "c1",
			items = new object[5]
			{
				new
				{
					title = "检查现有模组和 Java 参数",
					status = "completed"
				},
				new
				{
					title = "安装 Sodium / Lithium / FerriteCore",
					status = "completed"
				},
				new
				{
					title = "查找与 Create 兼容的渲染优化",
					status = "in_progress"
				},
				new
				{
					title = "调整 JVM 内存到 6 GB",
					status = "pending"
				},
				new
				{
					title = "调试启动并记录帧数",
					status = "pending"
				}
			}
		}));
		aiPage.OnEvent(Status("tool_done", "install_mod", "安装模组：lithium-fabric-0.11.2"));
		aiPage.OnEvent(Status("tool_skip", "install_mod2", "安装模组：ferritecore-6.0.1"));
		aiPage.OnEvent(Status("tool_done", "mcp__mcmod__search", "查询 MC 百科：create 渲染 兼容"));
		aiPage.OnEvent(Status("tool_start", "read_artifact", "读取更新日志：flywheel 0.6.10"));
		aiPage.OnEvent(Status("model_fallback", "", "", new
		{
			chat_id = "c1",
			model = "deepseek-reasoner"
		}));
		Pump(250);
		PlanCard planCard = aiPage.SelfTestMessages.Children.OfType<PlanCard>().FirstOrDefault();
		Check("计划：显示 2 / 5 进度", planCard != null && planCard.Done == 2 && planCard.Total == 5);
		ToolLine[] source = FindAll<ToolLine>(aiPage.SelfTestMessages).ToArray();
		Check("步骤：跳过状态", source.Any((ToolLine l) => l.State == ToolState.Skipped));
		Check("步骤：运行中状态", source.Any((ToolLine l) => l.State == ToolState.Running));
		Check("MCP 工具有专属图标", Lucide.ToolIcon("mcp__mcmod__search") != null);
		aiPage.SelfTestScrollEnd();
		Shot(window, dir, prefix + "4-plan");
		aiPage.OnEvent(new BridgeEvent
		{
			Event = "ai.fail",
			Text = "接口返回 429：请求太频繁，请稍后再试",
			Payload = J(new
			{
				chat_id = "c1"
			})
		});
		Pump();
		Check("失败：回到可发送状态", aiPage.SelfTestSendStop.Content == aiPage.SelfTestSend);
		aiPage.SelfTestHist.Visibility = Visibility.Collapsed;
		Pump();
		Shot(window, dir, prefix + "5-collapsed");
		aiPage.SelfTestHist.Visibility = Visibility.Visible;
		aiPage.SelfTestPerm.SelectedIndex = 3;
		Pump();
		Check("权限：全自动提示", aiPage.SelfTestPermTip.Text.Contains("全自动"));
		aiPage.SelfTestPerm.SelectedIndex = 0;
		Check("图标字体：Fluent 优先、MDL2 回退", Ui.Glyph("\ue710").FontFamily.Source.Contains("Fluent") && Ui.Glyph("\ue710").FontFamily.Source.Contains("MDL2"));
		DateTime now = DateTime.Now;
		AiStoreDto aiStoreDto = Store(empty: false);
		AiChatDto aiChatDto = aiStoreDto.Chats.First((AiChatDto c) => c.Id == "c1");
		for (int num2 = 0; num2 < 120; num2++)
		{
			aiChatDto.Messages.Add(new AiMsgDto
			{
				Id = "u" + num2,
				Role = "user",
				Content = "第 " + num2 + " 个问题"
			});
			aiChatDto.Messages.Add(new AiMsgDto
			{
				Id = "a" + num2,
				Role = "assistant",
				Content = "找到原因了：**Sodium Extra 0.4.18 和 Sodium 0.5.8 不兼容**，进世界时调用了新版 Sodium 已删除的方法。\n\n| 模组 | 当前版本 | 建议 |\n|---|---|---|\n| Sodium | `0.5.8` | 保留 |\n| Sodium Extra | `0.4.18` | 升级到 `0.5.7` |\n| Iris | `1.7.0` | 保留 |\n\n我可以直接替换，替换前会备份旧文件，出问题能一键撤回。"
			});
		}
		aiPage.SelfTestLoad(aiStoreDto);
		Pump(300);
		double totalMilliseconds = (DateTime.Now - now).TotalMilliseconds;
		Check("压力：240 条历史消息渲染 < 8s", totalMilliseconds < 8000.0, $"{totalMilliseconds:0}ms");
		Bubble[] source2 = aiPage.SelfTestMessages.Children.OfType<Bubble>().ToArray();
		Check("历史：120 条用户消息全部渲染", source2.Count((Bubble b) => b.IsMine) == 120);
		window.Close();
	}

	private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
	{
		if (root == null)
		{
			yield break;
		}
		foreach (object child in LogicalTreeHelper.GetChildren(root))
		{
			if (!(child is DependencyObject d))
			{
				continue;
			}
			if (d is T val)
			{
				yield return val;
			}
			foreach (T item in FindAll<T>(d))
			{
				yield return item;
			}
		}
	}
}
