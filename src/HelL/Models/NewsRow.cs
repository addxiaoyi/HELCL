namespace HelL.Models;

public sealed class NewsRow
{
	public string Title { get; set; } = "";

	public string Body { get; set; } = "";

	public string Version { get; set; } = "";
    public string Id { get; set; } = "";
    public string Image { get; set; } = "";
    public string Url { get; set; } = "";
    public string Date { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string OriginalBody { get; set; } = "";
    public string Localization { get; set; } = "";
}
