using System.Collections.Generic;

namespace HelL;

public sealed class NavGroup
{
	public string Title { get; set; } = "";

	public List<string> Keys { get; set; } = new List<string>();

	public NavGroup Clone()
	{
		return new NavGroup
		{
			Title = Title,
			Keys = new List<string>(Keys)
		};
	}
}
