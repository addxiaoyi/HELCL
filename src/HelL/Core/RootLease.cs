using System;
using System.IO;

namespace HelL.Core;

public sealed class RootLease : IDisposable
{
    private readonly FileStream lease;
    public string Root { get; }

    private RootLease(string root, FileStream lease) { Root = root; this.lease = lease; }

    public static RootLease Acquire(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A data root is required.", nameof(root));
        root = Path.GetFullPath(root);
        try
        {
            Directory.CreateDirectory(root);
            // Never unlink the lease: Unix owners must keep locking the same inode.
            var lease = new FileStream(Path.Combine(root, ".launcher.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            return new RootLease(root, lease);
        }
        catch (UnauthorizedAccessException ex) { throw new IOException("The launcher data directory is not writable.", ex); }
        catch (IOException ex) { throw new IOException("The launcher data directory is already in use or unavailable.", ex); }
    }

    public void Dispose() => lease.Dispose();
}
