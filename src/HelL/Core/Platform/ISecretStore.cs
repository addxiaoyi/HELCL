namespace HelL.Core.Platform;

public interface ISecretStore
{
	string GetPassword(string service, string username);

	void SetPassword(string service, string username, string password);

	void DeletePassword(string service, string username);
}
