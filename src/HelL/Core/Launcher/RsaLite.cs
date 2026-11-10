using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HelL.Core.Launcher;

public static class RsaLite
{
	internal static readonly byte[] RSA_OID = Hex("06092a864886f70d0101010500");

	internal static readonly byte[] SHA1_PREFIX = Hex("3021300906052b0e03021a05000414");

	private static byte[] Hex(string h)
	{
		byte[] array = new byte[h.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
		}
		return array;
	}

	internal static BigInteger PyMod(BigInteger a, BigInteger m)
	{
		BigInteger bigInteger = BigInteger.Remainder(a, m);
		if (bigInteger.Sign >= 0)
		{
			return bigInteger;
		}
		return bigInteger + m;
	}

	internal static BigInteger ModInverse(BigInteger a, BigInteger m)
	{
		BigInteger bigInteger = PyMod(a, m);
		BigInteger bigInteger2 = 1;
		BigInteger bigInteger3 = 0;
		BigInteger bigInteger4 = bigInteger;
		BigInteger bigInteger5 = m;
		while (bigInteger5 != 0L)
		{
			BigInteger bigInteger6 = BigInteger.Divide(bigInteger4, bigInteger5);
			BigInteger bigInteger7 = bigInteger5;
			bigInteger5 = bigInteger4 - bigInteger6 * bigInteger5;
			bigInteger4 = bigInteger7;
			BigInteger bigInteger8 = bigInteger3;
			bigInteger3 = bigInteger2 - bigInteger6 * bigInteger3;
			bigInteger2 = bigInteger8;
		}
		if (bigInteger4 != 1L)
		{
			throw new ValueError("base is not invertible for the given modulus");
		}
		return PyMod(bigInteger2, m);
	}

	internal static BigInteger FromBE(byte[] be)
	{
		byte[] array = new byte[be.Length + 1];
		for (int i = 0; i < be.Length; i++)
		{
			array[i] = be[be.Length - 1 - i];
		}
		return new BigInteger(array);
	}

	internal static byte[] ToBE(BigInteger v, int len = -1)
	{
		byte[] array = v.ToByteArray();
		int num = array.Length;
		while (num > 1 && array[num - 1] == 0)
		{
			num--;
		}
		if (v.IsZero)
		{
			num = 1;
		}
		if (len < 0)
		{
			len = num;
		}
		if (num > len)
		{
			throw new OverflowError("int too big to convert");
		}
		byte[] array2 = new byte[len];
		for (int i = 0; i < num; i++)
		{
			array2[len - 1 - i] = array[i];
		}
		return array2;
	}

	internal static long BitLength(BigInteger v)
	{
		if (v.Sign < 0)
		{
			v = -v;
		}
		if (v.IsZero)
		{
			return 0L;
		}
		byte[] array = ToBE(v);
		long num = (long)(array.Length - 1) * 8L;
		for (int num2 = array[0]; num2 > 0; num2 >>= 1)
		{
			num++;
		}
		return num;
	}

	internal static byte[] DerLen(int n)
	{
		if (n < 128)
		{
			return new byte[1] { (byte)n };
		}
		byte[] array = ToBE(new BigInteger(n));
		return new byte[1] { (byte)(0x80 | array.Length) }.Concat(array).ToArray();
	}

	internal static byte[] Tlv(int tag, byte[] body)
	{
		return new byte[1] { (byte)tag }.Concat(DerLen(body.Length)).Concat(body).ToArray();
	}

	internal static byte[] Int(BigInteger v)
	{
		int len = (int)Math.Max(1L, (BitLength(v) + 8) / 8);
		return Tlv(2, ToBE(v, len));
	}

	internal static byte[] Seq(params byte[][] items)
	{
		return Tlv(48, items.SelectMany((byte[] x) => x).ToArray());
	}

	internal static (int tag, byte[] body, int next) Read(byte[] data, int pos)
	{
		if (pos + 1 >= data.Length)
		{
			throw new IndexError("index out of range");
		}
		int item = data[pos];
		int num = data[pos + 1];
		pos += 2;
		if ((num & 0x80) != 0)
		{
			int num2 = num & 0x7F;
			long num3 = 0L;
			for (int i = 0; i < num2 && pos + i < data.Length; i++)
			{
				num3 = (num3 << 8) | data[pos + i];
			}
			num = (int)num3;
			pos += num2;
		}
		int num4 = Math.Min(pos, data.Length);
		int num5 = Math.Min(pos + num, data.Length);
		byte[] array = new byte[Math.Max(0, num5 - num4)];
		Buffer.BlockCopy(data, num4, array, 0, array.Length);
		return (tag: item, body: array, next: pos + num);
	}

	internal static List<(int tag, byte[] val)> Items(byte[] body)
	{
		int num = 0;
		List<(int, byte[])> list = new List<(int, byte[])>();
		while (num < body.Length)
		{
			int item;
			byte[] item2;
			(item, item2, num) = Read(body, num);
			list.Add((item, item2));
		}
		return list;
	}

	internal static byte[] Pem(string label, byte[] der)
	{
		string text = Convert.ToBase64String(der);
		List<string> list = new List<string>();
		for (int i = 0; i < text.Length; i += 64)
		{
			list.Add(text.Substring(i, Math.Min(64, text.Length - i)));
		}
		return Encoding.ASCII.GetBytes("-----BEGIN " + label + "-----\n" + string.Join("\n", list) + "\n-----END " + label + "-----\n");
	}

	internal static (string label, byte[] der) Unpem(byte[] data)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (byte b in data)
		{
			if (b < 128)
			{
				stringBuilder.Append((char)b);
			}
		}
		string text = stringBuilder.ToString();
		int num = text.IndexOf("-----BEGIN ", StringComparison.Ordinal);
		if (num < 0)
		{
			throw new ValueError("substring not found");
		}
		int num2 = text.IndexOf("-----", num + 11, StringComparison.Ordinal);
		if (num2 < 0)
		{
			throw new ValueError("substring not found");
		}
		string item = text.Substring(num + 11, num2 - num - 11);
		int num3 = text.IndexOf("\n", num, StringComparison.Ordinal);
		int num4 = text.IndexOf("-----END ", StringComparison.Ordinal);
		if (num3 < 0 || num4 < 0)
		{
			throw new ValueError("substring not found");
		}
		string obj = ((num4 > num3 + 1) ? text.Substring(num3 + 1, num4 - num3 - 1) : "");
		StringBuilder stringBuilder2 = new StringBuilder();
		string text2 = obj;
		foreach (char c in text2)
		{
			if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '=')
			{
				stringBuilder2.Append(c);
			}
		}
		byte[] item2;
		try
		{
			item2 = Convert.FromBase64String(stringBuilder2.ToString());
		}
		catch (FormatException)
		{
			throw new ValueError("Incorrect padding");
		}
		return (label: item, der: item2);
	}

	public static RSAKey generate(int bits = 2048, int e = 65537)
	{
		BigInteger bigInteger;
		BigInteger bigInteger2;
		BigInteger bigInteger4;
		BigInteger d;
		while (true)
		{
			RSAParameters rSAParameters;
			using (RSA rSA = RSA.Create())
			{
				rSA.KeySize = bits;
				rSAParameters = rSA.ExportParameters(includePrivateParameters: true);
			}
			if (FromBE(rSAParameters.Exponent) != e)
			{
				continue;
			}
			bigInteger = FromBE(rSAParameters.P);
			bigInteger2 = FromBE(rSAParameters.Q);
			if (bigInteger == bigInteger2)
			{
				continue;
			}
			if (bigInteger < bigInteger2)
			{
				BigInteger bigInteger3 = bigInteger;
				bigInteger = bigInteger2;
				bigInteger2 = bigInteger3;
			}
			bigInteger4 = bigInteger * bigInteger2;
			if (BitLength(bigInteger4) == bits)
			{
				BigInteger m = (bigInteger - 1) * (bigInteger2 - 1);
				try
				{
					d = ModInverse(e, m);
				}
				catch (ValueError)
				{
					continue;
				}
				break;
			}
		}
		return new RSAKey(bigInteger4, e, d, bigInteger, bigInteger2);
	}

	public static RSAKey load_pem_private_key(byte[] data)
	{
		(string label, byte[] der) tuple = Unpem(data);
		string item = tuple.label;
		List<(int, byte[])> list = Items(Read(tuple.der, 0).body);
		if (item == "PRIVATE KEY")
		{
			if (list.Count < 3)
			{
				throw new IndexError("list index out of range");
			}
			byte[] item2 = list[1].Item2;
			if (item2.Length < 11 || !item2.Take(11).SequenceEqual(RSA_OID.Take(11)))
			{
				throw new ValueError("不是 RSA 私钥");
			}
			list = Items(Read(list[2].Item2, 0).body);
		}
		else if (item != "RSA PRIVATE KEY")
		{
			throw new ValueError("不支持的 PEM 类型: " + item);
		}
		List<BigInteger> list2 = (from x in list.Take(6)
			where x.Item1 == 2
			select FromBE(x.Item2)).ToList();
		if (list2.Count != 6)
		{
			throw new ValueError($"not enough values to unpack (expected 6, got {list2.Count})");
		}
		RSAKey rSAKey = new RSAKey(list2[1], list2[2], list2[3], list2[4], list2[5]);
		if (BigInteger.ModPow(BigInteger.ModPow(2, rSAKey.e, rSAKey.n), rSAKey.d, rSAKey.n) != 2L)
		{
			throw new ValueError("RSA 私钥损坏");
		}
		return rSAKey;
	}
}
