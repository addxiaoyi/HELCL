using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async void ImportRemoteOfferFile(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        string? json = await ReadRemoteFileAsync(4096, "选择电脑公开配对信息");
        if (json is null) return;
        remoteVerified.IsChecked = false;
        remote!.StageOffer(json);
        remotePairing.IsExpanded = true;
    });
    private async void ImportRemoteInviteFile(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        string? json = await ReadRemoteFileAsync(8192, "选择仅给本手机的私密邀请");
        if (json is null) return;
        remote!.ImportInvitation(json);
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
        await using Stream input = await file.OpenReadAsync();
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[1024];
        int count;
        while ((count = await input.ReadAsync(buffer)) > 0)
        {
            if (bytes.Length + count > limit) throw new InvalidDataException("配对文件超过允许大小，请从电脑重新导出。");
            bytes.Write(buffer, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(bytes.ToArray());
    }
    private async void ExportRemoteOffer(object? sender, RoutedEventArgs e) => await RemoteAction(async () =>
    {
        var top = TopLevel.GetTopLevel(this) ?? throw new IOException("文件选择器暂不可用。");
        using IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "导出手机公开信息", SuggestedFileName = "hell-phone-public-offer.json", DefaultExtension = "json", ShowOverwritePrompt = true });
        if (file is null) return;
        await using Stream output = await file.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        byte[] bytes = Encoding.UTF8.GetBytes(remote!.OfferJson);
        await output.WriteAsync(bytes);
        await output.FlushAsync();
    });
}
