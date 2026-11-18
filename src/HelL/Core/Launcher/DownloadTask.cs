namespace HelL.Core.Launcher;

public sealed class DownloadTask
{
	public object Url;

	public string Dest;

	public string Sha1;

	public long? Size;

	public string Sha512;

	public DownloadTask(object url, string dest, string sha1 = null, long? size = null, string sha512 = null)
	{
		Url = url;
		Dest = dest;
		Sha1 = sha1;
		Size = size;
		Sha512 = sha512;
	}
}
