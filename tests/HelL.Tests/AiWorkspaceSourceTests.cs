using System;
using System.IO;
using Xunit;

namespace HelL.Tests;

public class AiWorkspaceSourceTests
{
    private static string Source(string relativePath)
    {
        return File.ReadAllText(Path.Combine(TestPaths.Root(), relativePath));
    }

    [Fact]
    public void WorkspaceKeepsModelLibraryMountedAndLocalModeRestrictsTools()
    {
        string source = Source("src/HelL/Pages/AiPage.Workspace.cs");
        Assert.Contains("private LocalModelsPanel? _modelsPanel", source);
        Assert.Contains("_library.Content = _modelsPanel", source);
        Assert.Contains("if (_modelsPanel == null)", source);
        Assert.Contains("_perm.IsEnabled = !_localMode", source);
        Assert.Contains("不执行工具操作", source);
    }

    [Fact]
    public void ModelWorkspaceHasResponsiveRuntimeAndSafeRepositoryDisclosure()
    {
        string source = Source("src/HelL/Pages/Ai/LocalModelsPanel.cs");
        Assert.Contains("runtimeActions.Wrap = true", source);
        Assert.Contains("模型与连接", source);
        Assert.Contains("可用内存", source);
        Assert.Contains("navigation", source);
        Assert.DoesNotContain("runtimeCard", source);
        Assert.Contains("runtimeRow.Add(_runtime.VCenter().M(0, 0, 12, 0), 0)", source);
        Assert.Contains("内存 ", source);
        Assert.Contains("完整分片会合并为一项下载", source);
        Assert.Contains("私有及受限仓库暂不支持", source);
        Assert.Contains("镜像会收到模型搜索与下载请求", source);
        Assert.Contains("ModelRules.Endpoint(_endpoint.Text)", source);
    }

    [Fact]
    public void LocalModelSettingsAreNotOverwrittenByRuntimeDiscoveryAndHaveDedicatedTab()
    {
        string source = Source("src/HelL/Pages/Ai/LocalModelsPanel.cs");
        Assert.Contains("(\"settings\", \"设置\")", source);
        Assert.Contains("private void RenderSettings()", source);
        Assert.DoesNotContain("_endpoint.Text = _state.Endpoint", source);
        Assert.Contains("_endpoint.Text = ModelRules.Endpoint(configuredEndpoint)", source);
        Assert.Contains("_endpoint.Tag = _endpoint.Text", source);
        Assert.Contains("private async Task SaveEndpoint()", source);
        Assert.Contains("private async Task SaveModelSource()", source);
        Assert.Contains("SettingsSourceUrl()", source);
        Assert.Contains("private void OpenConnection() => RunUi(() => SelectTab(\"settings\"))", source);
        Assert.Contains("private void RenderInstalled()", source);
        Assert.Contains("private async Task RenderCatalog()", source);
        Assert.Contains("private void RenderHub()", source);
        Assert.Contains("private async Task ShowFiles(string repository)", source);
        Assert.Contains("private async Task BeginJob(Func<Task<JsonElement>> work)", source);
        Assert.Contains("if (_loading) { Feedback(\"已有操作正在进行，请稍后再试。\"); return; }", source);
        Assert.Contains("private async Task CancelJob()", source);
        Assert.Contains("private async Task RetryJob()", source);
        Assert.Contains("private async Task PollJob()", source);
        Assert.Contains("private void RenderJob(ModelJob job)", source);
    }

    [Fact]
    public void TestOutputCopiesNet8JsonRuntimeForTheNet48LauncherReference()
    {
        string project = Source("tests/HelL.Tests/HelL.Tests.csproj");
        Assert.Contains("CopyLocalSystemTextJson", project);
        Assert.Contains("System.Text.Json.dll", project);
        Assert.Contains("$(TargetDir)", project);
    }

    [Fact]
    public void RuntimeDiscoveryDoesNotPersistAnEndpointSwitchWithoutUserConfirmation()
    {
        string source = Source("src/HelL/Core/BackendApi.LocalAi.cs");
        Assert.DoesNotContain("if (detect && status.Online && status.Endpoint != LocalEndpoint) CONFIG.Update", source);
    }

    [Fact]
    public void SmokeCanOpenModelWorkspaceWithoutAllowingDownloadsOrMessages()
    {
        string source = Source("src/HelL/Smoke.cs");
        int safeStart = source.IndexOf("SafeButtons", StringComparison.Ordinal);
        int unsafeStart = source.IndexOf("NeverClick", safeStart, StringComparison.Ordinal);
        Assert.True(safeStart >= 0 && unsafeStart > safeStart);
        string safeButtons = source.Substring(safeStart, unsafeStart - safeStart);
        string neverClick = source.Substring(unsafeStart);
        Assert.Contains("\"模型库\"", safeButtons);
        Assert.DoesNotContain("\"下载模型\"", safeButtons);
        Assert.DoesNotContain("\"发送\"", safeButtons);
        Assert.Contains("\"安装\"", neverClick);
        Assert.Contains("\"发送\"", neverClick);
        Assert.Contains("dictionary[\"post_click_shot\"] = Shoot(win, pageId + \"-model-library\")", source);
        Assert.Contains("dictionary[\"post_click_cjk_texts\"] = CjkOnScreen(win)", source);
    }

    [Fact]
    public void ModelPullErrorsAreShownInsteadOfBeingSwallowed()
    {
        string source = Source("src/HelL/Pages/Ai/LocalModelsPanel.cs");
        Assert.Contains("catch (Exception ex)", source);
        Assert.Contains("Feedback(ex.Message, true)", source);
        Assert.Contains("private async Task BeginJob", source);
        Assert.Contains("ai_local_pull", source);
        Assert.Contains("ai_local_download", source);
    }
}
