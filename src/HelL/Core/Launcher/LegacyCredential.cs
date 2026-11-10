using System;
using System.Security.Cryptography;
using System.Text;

namespace HelL.Core.Launcher;

internal static class LegacyCredential
{
    // Fingerprints allow old bundled keys to be removed without redistributing the credentials.
    internal static bool Matches(string field, string value)
    {
        string expected = field == "curseforge_api_key" ? "25e1ca023ff05347ac80914d9aceadd73b5703c034f669ba4db09c2ff9f4481f"
            : field == "ai_api_key" ? "d363c65617551c75e859ab0ba3030a045651cd3a663db79a7674eca195300451" : "";
        if (expected.Length == 0 || string.IsNullOrEmpty(value)) return false;
        using var sha = SHA256.Create();
        string digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()))).Replace("-", "").ToLowerInvariant();
        return digest == expected;
    }
}
