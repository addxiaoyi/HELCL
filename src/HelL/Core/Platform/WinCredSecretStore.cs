using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace HelL.Core.Platform;

public sealed class WinCredSecretStore : ISecretStore
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct CREDENTIAL
	{
		public uint Flags;

		public uint Type;

		public IntPtr TargetName;

		public IntPtr Comment;

		public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;

		public uint CredentialBlobSize;

		public IntPtr CredentialBlob;

		public uint Persist;

		public uint AttributeCount;

		public IntPtr Attributes;

		public IntPtr TargetAlias;

		public IntPtr UserName;
	}

	private sealed class Cred
	{
		public string UserName;

		public byte[] Blob;

		public string Value => DecodeBlob(Blob);
	}

	public const uint CRED_TYPE_GENERIC = 1u;

	public const uint CRED_PERSIST_ENTERPRISE = 3u;

	public const string COMMENT = "Stored using python-keyring";

	private const int ERROR_NOT_FOUND = 1168;

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredReadW", SetLastError = true)]
	private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredWriteW", SetLastError = true)]
	private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW", SetLastError = true)]
	private static extern bool CredDelete(string target, uint type, uint flags);

	[DllImport("advapi32.dll")]
	private static extern void CredFree(IntPtr buffer);

	private static string CompoundName(string username, string service)
	{
		return (username ?? "None") + "@" + service;
	}

	private static Exception Win32Error(int code, string func)
	{
		return new OSError($"({code}, '{func}', '{new Win32Exception(code).Message}')");
	}

	private static Cred ReadCredential(string target)
	{
		if (!CredRead(target, 1u, 0u, out var credential))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error == 1168)
			{
				return null;
			}
			throw Win32Error(lastWin32Error, "CredRead");
		}
		try
		{
			CREDENTIAL cREDENTIAL = (CREDENTIAL)Marshal.PtrToStructure(credential, typeof(CREDENTIAL));
			byte[] array = new byte[cREDENTIAL.CredentialBlobSize];
			if (cREDENTIAL.CredentialBlobSize != 0 && cREDENTIAL.CredentialBlob != IntPtr.Zero)
			{
				Marshal.Copy(cREDENTIAL.CredentialBlob, array, 0, array.Length);
			}
			return new Cred
			{
				UserName = ((cREDENTIAL.UserName == IntPtr.Zero) ? null : Marshal.PtrToStringUni(cREDENTIAL.UserName)),
				Blob = array
			};
		}
		finally
		{
			CredFree(credential);
		}
	}

	private static void WriteCredential(string target, string username, string password)
	{
		byte[] bytes = Encoding.Unicode.GetBytes(password ?? "");
		IntPtr intPtr = Marshal.StringToHGlobalUni(target);
		IntPtr intPtr2 = Marshal.StringToHGlobalUni("Stored using python-keyring");
		IntPtr intPtr3 = ((username == null) ? IntPtr.Zero : Marshal.StringToHGlobalUni(username));
		IntPtr intPtr4 = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
		try
		{
			Marshal.Copy(bytes, 0, intPtr4, bytes.Length);
			CREDENTIAL credential = new CREDENTIAL
			{
				Flags = 0u,
				Type = 1u,
				TargetName = intPtr,
				Comment = intPtr2,
				CredentialBlobSize = (uint)bytes.Length,
				CredentialBlob = intPtr4,
				Persist = 3u,
				UserName = intPtr3
			};
			if (!CredWrite(ref credential, 0u))
			{
				throw Win32Error(Marshal.GetLastWin32Error(), "CredWrite");
			}
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
			Marshal.FreeHGlobal(intPtr2);
			Marshal.FreeHGlobal(intPtr4);
			if (intPtr3 != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(intPtr3);
			}
		}
	}

	private static void DeleteCredential(string target)
	{
		if (!CredDelete(target, 1u, 0u))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error != 1168)
			{
				throw Win32Error(lastWin32Error, "CredDelete");
			}
		}
	}

	public static string DecodeBlob(byte[] b)
	{
		if (b == null)
		{
			return "";
		}
		try
		{
			if (b.Length % 2 != 0)
			{
				throw new ArgumentException("odd length");
			}
			bool flag = false;
			int num = 0;
			if (b.Length >= 2 && b[0] == byte.MaxValue && b[1] == 254)
			{
				num = 2;
			}
			else if (b.Length >= 2 && b[0] == 254 && b[1] == byte.MaxValue)
			{
				num = 2;
				flag = true;
			}
			return (flag ? new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true) : new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)).GetString(b, num, b.Length - num);
		}
		catch (ArgumentException)
		{
			try
			{
				return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(b);
			}
			catch (ArgumentException)
			{
				throw new UnicodeDecodeError("'utf-8' codec can't decode credential blob");
			}
		}
	}

	private Cred ResolveCredential(string service, string username)
	{
		Cred cred = ReadCredential(service);
		if (cred == null || (!string.IsNullOrEmpty(username) && cred.UserName != username))
		{
			cred = ReadCredential(CompoundName(username, service));
		}
		return cred;
	}

	public string GetPassword(string service, string username)
	{
		return ResolveCredential(service, username)?.Value;
	}

	public void SetPassword(string service, string username, string password)
	{
		Cred cred = ReadCredential(service);
		if (cred != null)
		{
			WriteCredential(CompoundName(cred.UserName, service), cred.UserName, cred.Value);
		}
		WriteCredential(service, username, password ?? "None");
	}

	public void DeletePassword(string service, string username)
	{
		bool flag = false;
		string[] array = new string[2]
		{
			service,
			CompoundName(username, service)
		};
		foreach (string target in array)
		{
			Cred cred = ReadCredential(target);
			if (cred != null && cred.UserName == username)
			{
				flag = true;
				DeleteCredential(target);
			}
		}
		if (!flag)
		{
			throw new SecretNotFoundException(service);
		}
	}
}
