using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public sealed class LayoutDoc
{
	public List<LayoutItem> items;

	public long grid;

	public LayoutDoc(List<LayoutItem> items = null, long grid = 8L)
	{
		this.items = ((items != null) ? new List<LayoutItem>(items) : new List<LayoutItem>());
		this.grid = grid;
	}

	public PyDict ToDict()
	{
		Normalize();
		List<object> list = new List<object>();
		foreach (LayoutItem item in items)
		{
			list.Add(item.ToDict());
		}
		return PyDict.Of("version", 1L, "grid", grid, "items", list);
	}

	public static LayoutDoc FromDict(object data)
	{
		if (!(data is PyDict pyDict))
		{
			return UiLayout.DefaultDoc();
		}
		long val;
		try
		{
			val = Py.Int(pyDict.Get("grid", 8L));
		}
		catch (TypeError)
		{
			val = 8L;
		}
		catch (ValueError)
		{
			val = 8L;
		}
		LayoutDoc layoutDoc = new LayoutDoc(null, Math.Max(0L, val));
		object obj = pyDict.Get("items");
		if (obj is List<object> list)
		{
			foreach (object item in list)
			{
				if (item is PyDict data2)
				{
					layoutDoc.items.Add(LayoutItem.FromDict(data2));
				}
			}
		}
		if (layoutDoc.items.Count == 0 && !(obj is List<object>))
		{
			return UiLayout.DefaultDoc();
		}
		layoutDoc.Normalize();
		return layoutDoc;
	}

	public void Normalize()
	{
		HashSet<string> hashSet = new HashSet<string>();
		for (int i = 0; i < items.Count; i++)
		{
			LayoutItem layoutItem = items[i];
			string text = ((!string.IsNullOrEmpty(layoutItem.id)) ? layoutItem.id : $"{layoutItem.type}-{i}");
			string text2 = text;
			int num = 2;
			while (hashSet.Contains(text2))
			{
				text2 = $"{text}-{num}";
				num++;
			}
			layoutItem.id = text2;
			hashSet.Add(text2);
		}
		long num2 = 0L;
		foreach (LayoutItem item in Py.Sorted(items, (LayoutItem layoutItem2) => layoutItem2.z))
		{
			item.z = num2++;
		}
	}

	public LayoutDoc Clone()
	{
		return FromDict(PyJson.Loads(PyJson.Dumps(ToDict())));
	}

	public LayoutItem Get(string item_id)
	{
		return items.FirstOrDefault((LayoutItem it) => it.id == item_id);
	}

	public long NextZ()
	{
		return ((items.Count == 0) ? (-1) : items.Max((LayoutItem it) => it.z)) + 1;
	}

	public List<LayoutItem> VisibleItems()
	{
		return (from it in Py.Sorted(items, (LayoutItem i) => i.z)
			where !it.hidden
			select it).ToList();
	}
}
