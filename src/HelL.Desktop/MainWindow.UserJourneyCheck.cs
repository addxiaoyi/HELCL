using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async void RunUserJourneyCheck(object? sender, EventArgs args)
    {
        var findings = new List<string>();
        int exitCode = 1;
        try
        {
            await Task.Delay(600);
            await JourneyIdle();
            if (InstanceList.ItemCount != 0) throw new InvalidOperationException("用户流程验收需要空数据目录。");
            await JourneyCapture("01-welcome");
            if (TasksView.History.Count != 0 || TaskState.Text == "已完成") findings.Add("首次打开产生了用户未发起的已完成任务。");
            if (WelcomePanel.GetLogicalDescendants().OfType<TextBox>().Count() == 0) findings.Add("首次创建入口缺少就近的名称输入。");
            if (this.FindControl<TextBox>("WelcomeName") is { } welcomeName)
            {
                this.FindControl<Button>("WelcomeCreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!InstanceError.IsVisible || !welcomeName.IsFocused) findings.Add("首次创建校验没有留在主表单。");
                welcomeName.Text = "旅程草稿";
                ShowPage("resources");
                ShowPage("launch");
                if (welcomeName.Text != "旅程草稿" || InstanceName.Text != "旅程草稿") findings.Add("离开再返回丢失首次创建草稿。");
                welcomeName.Clear();
            }

            ShowPage("tasks");
            await JourneyCapture("02-empty-tasks");
            if (TasksView.FindControl<Border>("LogDetails")!.IsVisible) findings.Add("空任务页仍显示无对象的日志操作。");
            ShowPage("install");
            await JourneyCapture("03-install-without-instance");
            if (InstallerView.FindControl<Button>("CreateTargetButton")?.IsVisible != true) findings.Add("安装页缺少创建目标实例的直接入口。");
            else
            {
                InstallerView.FindControl<Button>("CreateTargetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!LaunchView.IsVisible || this.FindControl<TextBox>("WelcomeName")?.IsFocused != true) findings.Add("安装页创建入口没有带到可输入的表单。");
            }
            ShowPage("resources");
            await JourneyCapture("04-resources-without-instance");
            var resourceNav = Navigation.Children.OfType<Button>().Single(button => button.Tag?.ToString() == "resources");
            if (resourceNav.Background is not ISolidColorBrush { Color.A: > 0 }) findings.Add("当前页面的导航高亮没有及时绘制。");
            if (ResourcesView.FindControl<TextBlock>("ResultNotice")!.Text?.Contains("目标已更新") == true) findings.Add("没有实例时仍提示目标已更新。");
            if (ResourcesView.FindControl<TextBlock>("PageLabel")!.Text?.Contains("第 1 页") == true) findings.Add("尚未搜索时显示无意义的分页。");
            ShowPage("settings");
            await JourneyCapture("05-settings");
            ShowPage("ai");
            AiView.FindControl<Border>("ConfigPanel")!.IsVisible = true;
            AiView.FindControl<ComboBox>("ModePicker")!.SelectedIndex = 1;
            AiView.FindControl<TextBox>("EndpointBox")!.Text = "invalid-endpoint";
            AiView.FindControl<TextBox>("ModelBox")!.Text = "";
            AiView.FindControl<TextBox>("ApiKeyBox")!.Text = "";
            await AiView.SaveConfigurationAsync();
            await JourneyCapture("06-ai-invalid-settings", 800, 600);
            if (AiView.FindControl<TextBlock>("ConfigError")?.IsVisible != true) findings.Add("AI 配置错误没有显示在表单旁。");
            int historyBeforeValidation = TasksView.History.Count;
            await AiView.SaveConfigurationAsync();
            if (TasksView.History.Count != historyBeforeValidation) findings.Add("填写错误被记为后台失败任务。");

            int sent = 0;
            AiView.SendFixture = (_, _) => { sent++; return Task.CompletedTask; };
            var suggestion = AiView.FindControl<StackPanel>("StarterPanel")!.GetLogicalDescendants().OfType<Button>().First(button => button.Tag is string);
            suggestion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyIdle();
            if (sent != 0) findings.Add("点击 AI 问题建议立即发送，用户没有编辑机会。");
            var prompt = AiView.FindControl<TextBox>("PromptInput")!;
            prompt.Text = "我已经写好的问题";
            suggestion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyIdle();
            if (!prompt.Text!.StartsWith("我已经写好的问题")) findings.Add("AI 建议覆盖了原有草稿。");
            AiView.SendFixture = null;
            AiView.FindControl<Border>("ConfigPanel")!.IsVisible = false;

            ShowPage("launch");
            InstanceName.Text = "我的生存世界";
            (this.FindControl<Button>("WelcomeCreateButton") ?? CreateButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyIdle();
            if (this.FindControl<Button>("WelcomeCreateButton") != null && !InstallerView.IsVisible) findings.Add("首次创建没有进入版本选择。");
            ShowPage("ai");
            prompt.Text = "请保留我的排查背景";
            var logSuggestion = AiView.FindControl<StackPanel>("StarterPanel")!.GetLogicalDescendants().OfType<Button>().First(button => button.Tag == null);
            logSuggestion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!prompt.Text!.StartsWith("请保留我的排查背景")) findings.Add("没有日志时，日志建议覆盖了问题草稿。");
            prompt.Text = "请保留我的排查背景";
            await AiView.LoadLogReviewAsync("我的生存世界", "fixture.log", _ => Task.FromResult("fixture log text"));
            if (!prompt.Text!.StartsWith("请保留我的排查背景") || !prompt.Text.Contains("fixture log text")) findings.Add("日志预览没有保留现有问题。");
            var endpoint = AiView.FindControl<TextBox>("EndpointBox")!;
            var model = AiView.FindControl<TextBox>("ModelBox")!;
            var key = AiView.FindControl<TextBox>("ApiKeyBox")!;
            int saves = 0;
            string savedKey = "";
            AiView.SaveConfigFixture = (profile, _) => { saves++; savedKey = profile.ApiKey; return Task.CompletedTask; };
            endpoint.Text = "https://provider-a.invalid/v1";
            model.Text = "fixture-model";
            key.Text = "fixture-key-for-provider-a";
            await AiView.SaveConfigurationAsync();
            endpoint.Text = "https://provider-b.invalid/v1";
            key.Clear();
            await AiView.SaveConfigurationAsync();
            if (saves != 1 || !AiView.FindControl<TextBlock>("ConfigError")!.IsVisible) findings.Add("更换 AI 服务地址时复用了旧服务密钥。");
            endpoint.Text = "https://provider-a.invalid/v1/";
            model.Text = "updated-model";
            await AiView.SaveConfigurationAsync();
            if (saves != 2 || savedKey != "fixture-key-for-provider-a") findings.Add("同一 AI 服务不能保留密钥更新模型。");
            AiView.SaveConfigFixture = null;
            endpoint.Text = "invalid-endpoint";
            await AiView.SaveConfigurationAsync();
            ShowPage("launch");
            await JourneyCapture("07-created-instance");
            FirstInstallButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyCapture("08-install-target");
            ShowPage("mods");
            await JourneyCapture("09-empty-mods");
            ShowPage("saves");
            await JourneyCapture("10-empty-saves");
            ShowPage("tasks");
            TasksView.FindControl<TextBox>("SearchBox")!.Text = "没有这种任务";
            if (TasksView.FindControl<Border>("LogDetails")!.IsVisible) findings.Add("筛选无结果时仍显示旧日志操作。");
            if (TasksView.FindControl<Button>("ClearFiltersButton") is { IsVisible: true } clearFilters)
            {
                clearFilters.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (TasksView.FindControl<ListBox>("TaskList")!.ItemCount == 0) findings.Add("清除任务筛选未恢复列表。");
            }
            SelectTheme("light", persist: false);
            ShowPage("ai");
            AiView.FindControl<Border>("ConfigPanel")!.IsVisible = true;
            await JourneyCapture("11-ai-light-small", 800, 600);
            ShowPage("install");
            await JourneyCapture("12-install-light-small", 800, 600);
            var installAction = InstallerView.FindControl<Button>("StartInstallButton")!;
            var installSearch = InstallerView.FindControl<TextBox>("SearchBox")!;
            var actionPoint = installAction.TranslatePoint(default, this);
            if (installSearch.Bounds.Width < 180 || actionPoint == null || actionPoint.Value.Y + installAction.Bounds.Height > Bounds.Height - 20)
                findings.Add("小窗口的版本搜索或安装操作被遮挡。");
            var navigation = Navigation.Children.OfType<Button>().Where(button => button.Classes.Contains("current")).ToArray();
            if (navigation.Length != 1 || navigation[0].Tag?.ToString() != "install") findings.Add("导航没有唯一标记当前页面。");
            await CheckInstanceContextAsync(findings);
            await CheckAppearanceCommitAsync(findings);
            File.WriteAllText(Path.Combine(Program.DataRoot, "user-journey.json"), JsonSerializer.Serialize(new { ok = findings.Count == 0, findings }));
            exitCode = findings.Count == 0 ? 0 : 1;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Program.DataRoot, "user-journey-error.txt"), error.ToString()); }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exitCode); }
    }

    private async Task JourneyIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (IsWorking)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("用户流程任务未结束：" + Status.Text);
            await Task.Delay(30);
        }
        await Task.Delay(70);
    }

    private async Task JourneyCapture(string step, int width = 1180, int height = 760)
    {
        Width = width;
        Height = height;
        await Task.Delay(450);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(Path.Combine(Program.DataRoot, step + ".png"), new PngBitmapEncoderOptions());
        File.WriteAllText(Path.Combine(Program.DataRoot, step + "-navigation.json"), JsonSerializer.Serialize(
            Navigation.Children.OfType<Button>().Select(button => new
            {
                page = button.Tag?.ToString(), current = button.Classes.Contains("current"),
                background = button.Background?.ToString()
            })));
    }
}
