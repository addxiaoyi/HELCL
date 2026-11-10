using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class CatalogDownload
{
    public static void Modrinth(DownloadManager manager, IEnumerable<object> urls, string target, object metadata)
    {
        var file = metadata as PyDict ?? throw new DownloadError("Modrinth 文件资料无效。");
        var hashes = file.Get("hashes") as PyDict ?? file;
        string sha512 = Py.S(hashes.Get("sha512")), sha1 = Py.S(hashes.Get("sha1"));
        if (sha512.Length == 0 && sha1.Length == 0) throw new DownloadError("Modrinth 文件缺少校验信息，请重新查询版本。");
        if (sha512.Length > 0 && !Regex.IsMatch(sha512, @"\A[a-fA-F0-9]{128}\z") || sha1.Length > 0 && !Regex.IsMatch(sha1, @"\A[a-fA-F0-9]{40}\z"))
            throw new DownloadError("Modrinth 文件校验信息无效，请重新查询版本。");
        var addresses = urls.Select(Py.S).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToList();
        if (addresses.Count == 0) throw new DownloadError("此文件没有可用的下载地址。");
        long? size = long.TryParse(Py.S(file.Get("size")), out long length) && length > 0 ? length : (long?)null;
        manager.Download(addresses[0], target, sha1: sha1, size: size, sha512: sha512, urls: addresses, timeout: 900L);
    }

    public static void CurseForge(DownloadManager manager, IEnumerable<object> urls, string target, object metadata)
    {
        var file = metadata as PyDict ?? new PyDict();
        var addresses = urls.Select(Py.S).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().ToList();
        if (addresses.Count == 0) throw new DownloadError("此文件没有可用的下载地址。");
        string sha1 = Py.S(Py.ListOr(file.Get("hashes")).OfType<PyDict>().FirstOrDefault(hash => Py.Int(hash.Get("algo")) == 1)?.Get("value"));
        if (sha1.Length > 0 && !Regex.IsMatch(sha1, @"\A[a-fA-F0-9]{40}\z")) throw new DownloadError("CurseForge 文件校验信息无效，请重新查询文件。");
        long? size = long.TryParse(Py.S(file.Get("fileLength")), out long length) && length > 0 ? length : (long?)null;
        manager.Download(addresses[0], target, sha1: sha1, size: size, urls: addresses, timeout: 900L);
    }
}
