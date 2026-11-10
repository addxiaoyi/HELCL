using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HelL.Models;

namespace HelL.Core.Launcher;

public static class CatalogOrdering
{
    public static IEnumerable<CatalogItem> Sort(IEnumerable<CatalogItem> rows, string order) => order switch
    {
        "relevance" => rows,
        "downloads" => rows.OrderByDescending(row => row.Downloads).ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        "updated" => rows.OrderByDescending(row => Updated(row.Updated)),
        "name" => rows.OrderBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        _ => throw new ArgumentException("资源排序方式无效。")
    };

    private static DateTimeOffset Updated(string date) => DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var timestamp) ? timestamp : DateTimeOffset.MinValue;
}
