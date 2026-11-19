namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private static string TaskTitle(string operation) => operation switch
    {
        "CreateInstance" => "创建实例",
        "RefreshVersions" => "获取 Minecraft 版本",
        "InstallVersion" => "安装 Minecraft",
        "RefreshLoaderVersions" => "获取加载器版本",
        "SaveLaunchOptions" => "保存启动设置",
        "DetectJava" => "检测 Java",
        "BrowseJava" => "选择 Java",
        "LaunchDemo" => "运行 Minecraft",
        "RestoreAccount" => "恢复账号",
        "LoginMicrosoft" => "登录 Microsoft 账号",
        "RefreshAccount" => "刷新账号授权",
        "LogoutAccount" => "退出账号",
        "Search" => "搜索资源",
        "LoadReleases" => "获取资源版本",
        "DownloadSelected" => "下载资源",
        "PickCover" => "更新实例封面",
        "OpenFolder" => "打开实例目录",
        "OpenWebsite" => "打开项目主页",
        "SaveInstance" => "保存实例设置",
        "PinInstance" => "固定实例",
        "ResetCover" => "恢复实例封面",
        "RefreshLibrary" => "刷新实例库",
        _ => "客户端任务"
    };
}
