namespace HelL.Core.Platform;

public interface IImageCodec
{
	IDecodedImage Decode(byte[] data);
}
