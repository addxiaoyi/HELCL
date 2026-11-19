using System.Collections.Generic;
using System.Text;

namespace HelL.Pages.Ai;

internal sealed class MdInline
{
	public MdInlineKind Kind;

	public string Text = "";

	public string Url = "";

	public List<MdInline> Children = new List<MdInline>();

	public override string ToString()
	{
		switch (Kind)
		{
		case MdInlineKind.Text:
			return Text;
		case MdInlineKind.Code:
			return "`" + Text + "`";
		case MdInlineKind.Break:
			return "\n";
		default:
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (MdInline child in Children)
			{
				stringBuilder.Append(child);
			}
			return Kind switch
			{
				MdInlineKind.Bold => "**" + stringBuilder?.ToString() + "**", 
				MdInlineKind.Italic => "*" + stringBuilder?.ToString() + "*", 
				MdInlineKind.Strike => "~~" + stringBuilder?.ToString() + "~~", 
				MdInlineKind.Link => "[" + stringBuilder?.ToString() + "](" + Url + ")", 
				_ => stringBuilder.ToString(), 
			};
		}
		}
	}
}
