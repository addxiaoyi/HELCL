using System;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ResourceNameImport Preview, DateTime Expires)> _nameImports = new();

    public object export_resource_names(string path) => PyDict.Of("count", CustomResourceNames.Export(path));

    public object preview_resource_names(string path)
    {
        foreach (var expired in _nameImports.Where(pair => pair.Value.Expires < DateTime.UtcNow).ToArray()) _nameImports.TryRemove(expired.Key, out _);
        if (_nameImports.Count >= 8) throw new InvalidOperationException("已有多个导入预览，请稍后重试。");
        var preview = CustomResourceNames.PreviewImport(path);
        string token = Guid.NewGuid().ToString("N");
        _nameImports[token] = (preview, DateTime.UtcNow.AddMinutes(10));
        return PyDict.Of("token", token, "rows", preview.Rows.Select(row => (object)PyDict.Of("key", row.Key, "name", row.Incoming.English,
            "incoming", row.Incoming.Chinese, "current", row.Current?.Chinese ?? "", "aliases", row.Incoming.Aliases.Cast<object>().ToList(),
            "current_aliases", row.Current?.Aliases.Cast<object>().ToList() ?? new(), "status", row.Status)).ToList());
    }

    public object import_resource_names(string token, object choices)
    {
        if (!_nameImports.TryGetValue(token, out var saved) || saved.Expires < DateTime.UtcNow) throw new InvalidOperationException("导入预览已过期，请重新选择文件。");
        var decisions = Py.DictOr(choices).ToDictionary(pair => pair.Key, pair => Py.S(pair.Value));
        int changed = CustomResourceNames.ApplyImport(saved.Preview, decisions);
        _nameImports.TryRemove(token, out _);
        return PyDict.Of("count", changed);
    }

    public object catalog_custom_name(string source, string slug = "", string id = "")
    {
        var row = CustomResourceNames.Find(source, slug, id);
        return PyDict.Of("chinese", row?.Chinese ?? "", "aliases", row?.Aliases.Cast<object>().ToList() ?? new(), "error", CustomResourceNames.LastError);
    }

    public object save_catalog_name(object project, string chinese = "", object aliases = null)
    {
        var row = (project as PyDict)?.Copy() ?? throw new ArgumentException("项目资料无效。");
        CustomResourceNames.Save(new CustomResourceName { Kind = Py.S(row.Get("kind")), Source = Py.S(row.Get("source")).ToLowerInvariant(),
            Slug = Py.S(row.Get("slug")), Id = Py.S(row.Get("id")), English = Py.S(row.Get("name")), Chinese = chinese,
            Aliases = Py.ListOr(aliases).Select(Py.S).ToArray() });
        CatalogPresentation.Localize(row);
        return row;
    }
}
