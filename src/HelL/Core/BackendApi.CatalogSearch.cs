using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    private List<object> SearchCatalog(string kind, object query, object source, object extra)
    {
        CommunityNames.Ensure();
        var hits = CatalogSearch.Search(kind, Py.S(query), _catalog_source(source), ExtraOr(extra));
        return hits.OfType<PyDict>().Select(hit => (object)_content_row(hit, Py.S(hit.Get("source")))).ToList();
    }

    public object catalog_language_status(bool refresh = false)
    {
        CommunityNames.Ensure(refresh);
        return PyDict.Of("count", ResourceNames.All.Count(), "community_count", CommunityNames.Entries.Count,
            "updated", CommunityNames.Updated?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", "error", CommunityNames.LastError,
            "source", CommunityNames.Attribution);
    }
}
