using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HelL.Core.Launcher;

internal sealed class MultiHasher : IDisposable
{
	public HashAlgorithm h1;

	public HashAlgorithm h256;

	public HashAlgorithm h512;

	private string _d1;

	private string _d256;

	private string _d512;

	private bool _fin;

	public bool Any
	{
		get
		{
			if (h1 == null && h256 == null)
			{
				return h512 != null;
			}
			return true;
		}
	}

	public string Sha1
	{
		get
		{
			Finish();
			return _d1;
		}
	}

	public string Sha256
	{
		get
		{
			Finish();
			return _d256;
		}
	}

	public string Sha512
	{
		get
		{
			Finish();
			return _d512;
		}
	}

	public MultiHasher(string sha1, string sha256, string sha512)
	{
		if (!string.IsNullOrEmpty(sha1))
		{
			h1 = SHA1.Create();
		}
		if (!string.IsNullOrEmpty(sha256))
		{
			h256 = SHA256.Create();
		}
		if (!string.IsNullOrEmpty(sha512))
		{
			h512 = SHA512.Create();
		}
	}

	public void Update(byte[] b, int off, int n)
	{
		h1?.TransformBlock(b, off, n, null, 0);
		h256?.TransformBlock(b, off, n, null, 0);
		h512?.TransformBlock(b, off, n, null, 0);
	}

	private static string Fin(HashAlgorithm h)
	{
		if (h == null)
		{
			return null;
		}
		h.TransformFinalBlock(new byte[0], 0, 0);
		StringBuilder stringBuilder = new StringBuilder();
		byte[] hash = h.Hash;
		foreach (byte b in hash)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private void Finish()
	{
		if (!_fin)
		{
			_fin = true;
			_d1 = Fin(h1);
			_d256 = Fin(h256);
			_d512 = Fin(h512);
		}
	}

	public void UpdateFile(string path)
	{
		if (!Any)
		{
			return;
		}
		using FileStream fileStream = new FileStream(Utils.AbsPath(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		byte[] array = new byte[1048576];
		int n;
		while ((n = fileStream.Read(array, 0, array.Length)) > 0)
		{
			Update(array, 0, n);
		}
	}

	public void Dispose()
	{
		h1?.Dispose();
		h256?.Dispose();
		h512?.Dispose();
	}
}
