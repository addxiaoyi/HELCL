namespace HelL.Remote;

public sealed record InvitationFeedDescriptor(string Channel, string ReaderToken, string EncryptionKey, long Expires)
{
    public override string ToString() => "InvitationFeedDescriptor { Credentials = [redacted] }";
}

public sealed record InvitationFeedEnvelope(long Revision, string Nonce, string Ciphertext, string Tag);
public sealed record InvitationFeedWrite(string ReaderHash, long Expires, InvitationFeedEnvelope Envelope);
public sealed record InvitationFeedUpdate(long Revision, RemoteHostInvite Invitation);
