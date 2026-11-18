using HelL.Core.Platform;
using HelL.Remote;

namespace HelL.Mobile.Remote;

public sealed class MobileRemoteSecrets(ISecretStore secrets) : IRemoteSecretStore
{
    private const string Service = "HelL remote device encryption";
    public string Read(string key) => secrets.GetPassword(Service, key);
    public void Write(string key, string value) => secrets.SetPassword(Service, key, value);
    public void Delete(string key) => secrets.DeletePassword(Service, key);
}
