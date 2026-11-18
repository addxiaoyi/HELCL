using HelL.Android;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class CredentialEnvelopeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Schema\":1,\"Iv\":null,\"Ciphertext\":null}")]
    [InlineData("{\"Schema\":1,\"Iv\":\"AA==\",\"Ciphertext\":\"AA==\"}")]
    [InlineData("{\"Schema\":1,\"Iv\":\"!\",\"Ciphertext\":\"!\"}")]
    [InlineData("{\"Schema\":2,\"Iv\":\"AA==\",\"Ciphertext\":\"AA==\"}")]
    public void CorruptPreferencesProduceContextualError(string encoded) =>
        Assert.Throws<InvalidDataException>(() => CredentialEnvelope.Read(encoded));

    [Fact]
    public void RejectsOversizedPreferencesBeforeDecoding() =>
        Assert.Throws<InvalidDataException>(() => CredentialEnvelope.Read(new string('A', 16385)));

    [Fact]
    public void RoundTripPreservesIvAndAuthenticatedCiphertext()
    {
        byte[] iv = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        byte[] encrypted = Enumerable.Range(0, 48).Select(value => (byte)value).ToArray();
        var restored = CredentialEnvelope.Read(CredentialEnvelope.Write(iv, encrypted));
        Assert.Equal(iv, restored.Iv);
        Assert.Equal(encrypted, restored.Ciphertext);
    }

    [Fact]
    public void RejectsOversizedCiphertextEvenWithValidJson()
    {
        string encoded = System.Text.Json.JsonSerializer.Serialize(new
        {
            Schema = 1, Iv = Convert.ToBase64String(new byte[12]),
            Ciphertext = Convert.ToBase64String(new byte[CredentialEnvelope.MaxSecretBytes + 17])
        });
        Assert.Throws<InvalidDataException>(() => CredentialEnvelope.Read(encoded));
    }
}
