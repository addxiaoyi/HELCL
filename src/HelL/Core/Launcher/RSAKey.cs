using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace HelL.Core.Launcher;

public sealed class RSAKey
{
	public readonly BigInteger n;

	public readonly BigInteger e;

	public readonly BigInteger d;

	public readonly BigInteger p;

	public readonly BigInteger q;

	public readonly BigInteger dp;

	public readonly BigInteger dq;

	public readonly BigInteger qi;

	public long key_size => RsaLite.BitLength(n);

	public RSAKey(BigInteger n, BigInteger e, BigInteger d, BigInteger p, BigInteger q)
	{
		this.n = n;
		this.e = e;
		this.d = d;
		this.p = p;
		this.q = q;
		dp = RsaLite.PyMod(d, p - 1);
		dq = RsaLite.PyMod(d, q - 1);
		qi = RsaLite.ModInverse(q, p);
	}

	public byte[] private_pem()
	{
		byte[] body = RsaLite.Seq(RsaLite.Int(0), RsaLite.Int(n), RsaLite.Int(e), RsaLite.Int(d), RsaLite.Int(p), RsaLite.Int(q), RsaLite.Int(dp), RsaLite.Int(dq), RsaLite.Int(qi));
		byte[] der = RsaLite.Seq(RsaLite.Int(0), RsaLite.Tlv(48, RsaLite.RSA_OID), RsaLite.Tlv(4, body));
		return RsaLite.Pem("PRIVATE KEY", der);
	}

	public byte[] public_pem()
	{
		byte[] array = RsaLite.Seq(RsaLite.Int(n), RsaLite.Int(e));
		byte[] array2 = new byte[array.Length + 1];
		Buffer.BlockCopy(array, 0, array2, 1, array.Length);
		byte[] der = RsaLite.Seq(RsaLite.Tlv(48, RsaLite.RSA_OID), RsaLite.Tlv(3, array2));
		return RsaLite.Pem("PUBLIC KEY", der);
	}

	public byte[] sign_sha1(byte[] message)
	{
		int num = (int)((RsaLite.BitLength(n) + 7) / 8);
		byte[] second;
		using (SHA1 sHA = SHA1.Create())
		{
			second = sHA.ComputeHash(message);
		}
		byte[] array = RsaLite.SHA1_PREFIX.Concat(second).ToArray();
		if (num < array.Length + 11)
		{
			throw new ValueError("RSA 密钥太短");
		}
		byte[] array2 = new byte[num];
		array2[0] = 0;
		array2[1] = 1;
		for (int i = 2; i < num - array.Length - 1; i++)
		{
			array2[i] = byte.MaxValue;
		}
		array2[num - array.Length - 1] = 0;
		Buffer.BlockCopy(array, 0, array2, num - array.Length, array.Length);
		BigInteger bigInteger = RsaLite.FromBE(array2);
		BigInteger bigInteger2 = BigInteger.ModPow(bigInteger, dp, p);
		BigInteger bigInteger3 = BigInteger.ModPow(bigInteger, dq, q);
		BigInteger bigInteger4 = RsaLite.PyMod(qi * (bigInteger2 - bigInteger3), p);
		BigInteger bigInteger5 = bigInteger3 + bigInteger4 * q;
		if (BigInteger.ModPow(bigInteger5, e, n) != bigInteger)
		{
			bigInteger5 = BigInteger.ModPow(bigInteger, d, n);
		}
		return RsaLite.ToBE(bigInteger5, num);
	}
}
