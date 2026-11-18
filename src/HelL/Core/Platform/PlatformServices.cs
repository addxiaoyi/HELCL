namespace HelL.Core.Platform;

public static class PlatformServices
{
	public static IImageCodec ImageCodec { get; set; }

	public static ISecretStore SecretStore { get; set; }
}
