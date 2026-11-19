namespace HelL.Models;

public sealed class ServerRow
{
	public string Name { get; set; } = "";

	public string Ip { get; set; } = "";

	public int Port { get; set; } = 25565;

	public string Description { get; set; } = "";
}
