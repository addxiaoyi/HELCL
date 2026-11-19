using System.Runtime.CompilerServices;

namespace HelL;

public sealed record NavEntry(NavEntryKind Kind, string Key = "", string Label = "", bool Top = false, bool Draggable = false)
{
	public static readonly NavEntry Stretch = new NavEntry(NavEntryKind.Stretch);

	public static NavEntry Header(string label)
	{
		return new NavEntry(NavEntryKind.Header, "", label);
	}

	[CompilerGenerated]
	private NavEntry(NavEntry original)
	{
		Kind = original.Kind;
		Key = original.Key;
		Label = original.Label;
		Top = original.Top;
		Draggable = original.Draggable;
	}
}
