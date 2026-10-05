using System;

namespace ByteBridge.Security;

/*
 * Protects the secrets bytebridge.db holds at rest: Firebird
 * passwords, the gateway API key and the enrolment claim secret.
 *
 * What this does and does not buy, stated once and for all:
 *
 *   A stolen copy of bytebridge.db -- a backup, a disk image, a
 *   shadow copy -- used to hand over every secret in plain text.
 *   Now it hands over ciphertext that only the originating machine
 *   can open. On Windows the key is the machine's DPAPI master key;
 *   on Linux it is a random key file beside the database, readable
 *   only by its owner (and the folder above it is owner-only too).
 *
 *   It does NOT keep secrets from someone already running code as
 *   an administrator (or root) on the live machine: LocalMachine
 *   DPAPI decrypts for any local process, and root reads any file.
 *   That boundary was and stays the data-folder ACL. What changes
 *   is that the file on its own is no longer enough.
 */
public interface ISecretProtector
{
    /*
     * Wraps a plaintext secret for storage. Already-wrapped input is
     * returned unchanged, so a value passing through twice by mistake
     * is not wrapped twice.
     */
    string Protect(string plaintext);

    /*
     * Opens a stored secret. A value that carries no protection
     * marker is a legacy plaintext value from before protection
     * existed and is returned as it is, so old settings files keep
     * working until the migration rewrites them.
     */
    string Unprotect(string stored);

    /*
     * As Unprotect, but reports failure instead of throwing. False
     * means the value is wrapped but this machine cannot open it --
     * the file was copied from another machine, or the key is gone.
     */
    bool TryUnprotect(string stored, out string plaintext);

    bool IsProtected(string? stored);
}

public static class SecretProtector
{
    /*
     * Every wrapped value starts with this. The version number leaves
     * room to change the scheme later and still read what is there.
     */
    public const string Prefix = "enc:v1:";

    public static bool HasPrefix(string? stored) =>
        stored != null &&
        stored.StartsWith(Prefix, StringComparison.Ordinal);

    /*
     * The protector for this machine: DPAPI on Windows, an AES key
     * file on Linux. The data directory is where the Linux key file
     * lives; it is ignored on Windows.
     */
    public static ISecretProtector ForMachine(string dataDirectory) =>
        OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new AesKeyFileSecretProtector(dataDirectory);
}

/*
 * The shared wrapper logic, so the two platform implementations only
 * ever deal in bytes.
 */
public abstract class SecretProtectorBase : ISecretProtector
{
    public string Protect(string plaintext)
    {
        if (plaintext == null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        if (IsProtected(plaintext))
        {
            return plaintext;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        return SecretProtector.Prefix +
            Convert.ToBase64String(ProtectBytes(bytes));
    }

    public string Unprotect(string stored)
    {
        if (TryUnprotect(stored, out var plaintext))
        {
            return plaintext;
        }

        throw new InvalidOperationException(
            "A stored secret could not be decrypted on this machine. " +
            "If the settings file was copied from another machine, the " +
            "secrets in it have to be re-entered there: decryption keys " +
            "never leave the machine they were made on.");
    }

    public bool TryUnprotect(string stored, out string plaintext)
    {
        plaintext = string.Empty;

        if (stored == null)
        {
            return false;
        }

        /*
         * Legacy plaintext passes through; it is what settings files
         * from before protection existed are full of.
         */
        if (!IsProtected(stored))
        {
            plaintext = stored;
            return true;
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(
                stored[SecretProtector.Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] opened;

        try
        {
            opened = UnprotectBytes(bytes);
        }
        catch (Exception)
        {
            /*
             * Wrong machine, lost key, tampered value: all the same
             * to the caller -- this secret cannot be read here.
             */
            return false;
        }

        try
        {
            plaintext = System.Text.Encoding.UTF8.GetString(opened);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    public bool IsProtected(string? stored) =>
        SecretProtector.HasPrefix(stored);

    protected abstract byte[] ProtectBytes(byte[] plaintext);

    protected abstract byte[] UnprotectBytes(byte[] protectedBytes);
}
