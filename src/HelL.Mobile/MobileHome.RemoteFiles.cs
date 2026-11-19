using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HelL.Mobile.Remote;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private sealed record RemoteFileContext(MobileRemoteController Owner, string? SelectedHost, int NavigationRevision);

    private async void ImportRemoteOfferFile(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        if (remote is null) return;
        var context = new RemoteFileContext(remote, remote.SelectedHostId, navigationRevision);
        string? json = await ReadRemoteFileAsync(4096, "选择电脑公开配对信息");
        if (json is null || !CanApplyRemoteFile(context)) return;
        remoteVerified.IsChecked = false;
        context.Owner.StageOffer(json);
        remotePairing.IsExpanded = true;
    });
    private async void ImportRemoteInviteFile(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        if (remote is null) return;
        var context = new RemoteFileContext(remote, remote.SelectedHostId, navigationRevision);
        string? json = await ReadRemoteFileAsync(8192, "选择仅给本手机的私密邀请");
        if (json is null || !CanApplyRemoteFile(context)) return;
        context.Owner.ImportInvitation(json);
        remoteInviteInput.Clear();
    });
    private async Task<string?> ReadRemoteFileAsync(int limit, string title)
    {
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("文件选择器暂不可用。");
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON 配对文件") { Patterns = ["*.json"], MimeTypes = ["application/json", "text/plain"] }]
        });
        if (files.Count == 0) return null;
        using var file = files[0];
        if (disposed || currentPage != "remote") return null;
        await using Stream input = await file.OpenReadAsync();
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[1024];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (disposed || currentPage != "remote") return null;
            if (bytes.Length + count > limit) throw new InvalidDataException("配对文件超过允许大小，请从电脑重新导出。");
            bytes.Write(buffer, 0, count);
        }
        if (disposed || currentPage != "remote") return null;
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }

    private bool CanApplyRemoteFile(RemoteFileContext context) => !disposed && currentPage == "remote"
        && navigationRevision == context.NavigationRevision && ReferenceEquals(remote, context.Owner)
        && context.Owner.SelectedHostId == context.SelectedHost;
    private async void ExportRemoteOffer(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("文件选择器暂不可用。");
        using IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "导出手机公开信息", SuggestedFileName = "hell-phone-public-offer.json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file is null || disposed || currentPage != "remote") return;
        await using Stream output = await file.OpenWriteAsync();
        if (disposed || currentPage != "remote") return;
        if (output.CanSeek) output.SetLength(0);
        byte[] bytes = Encoding.UTF8.GetBytes(remote!.OfferJson);
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    });
}
