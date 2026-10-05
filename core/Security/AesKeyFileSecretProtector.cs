using System;
using System.IO;
using System.Security.Cryptography;

namespace ByteBridge.Security;

/*
 * Linux: AES-256-GCM with a random key in a file beside the database.
 *
 * Linux has no DPAPI, so the machine key is made by hand: 32 random
 * bytes in <dataDirectory>/.secrets-key, created once and never
 * overwritten -- overwriting it would orphan every secret encrypted
 * with it. The file is owner-read/write only, and the directory above
 * it is owner-only already (DataFolderSecurity), so the key is
 * reachable by root and the service account and nobody else.
 *
 * Stored blob layout: 12-byte nonce, 16-byte tag, then ciphertext.
 * A fresh nonce per value, so two equal secrets do not encrypt to
 * equal text and leak that they match.
 */
public sealed class AesKeyFileSecretProtector : SecretProtectorBase
{
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public AesKeyFileSecretProtector(string dataDirectory)
    {
        _key = LoadOrCreateKey(
            Path.Combine(dataDirectory, ".secrets-key"));
    }

    protected override byte[] ProtectBytes(byte[] plaintext)
    {
        var blob =
            new byte[NonceBytes + TagBytes + plaintext.Length];

        var nonce = blob.AsSpan(0, NonceBytes);
        var tag = blob.AsSpan(NonceBytes, TagBytes);
        var ciphertext = blob.AsSpan(NonceBytes + TagBytes);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagBytes);

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return blob;
    }

    protected override byte[] UnprotectBytes(byte[] protectedBytes)
    {
        if (protectedBytes.Length < NonceBytes + TagBytes)
        {
            throw new CryptographicException(
                "The stored secret is truncated.");
        }

        var nonce = protectedBytes.AsSpan(0, NonceBytes);
        var tag = protectedBytes.AsSpan(NonceBytes, TagBytes);
        var ciphertext =
            protectedBytes.AsSpan(NonceBytes + TagBytes);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagBytes);

        /*
         * Throws CryptographicException on a wrong key or a tampered
         * value; the base class turns that into "cannot read here".
         */
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static byte[] LoadOrCreateKey(string path)
    {
        /*
         * CreateNew, never truncate: an existing key is the only way
         * to read what was encrypted with it. Two processes starting
         * together may race the creation; the loser of CreateNew just
         * reads what the winner wrote.
         */
        try
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                var fresh = RandomNumberGenerator.GetBytes(KeyBytes);

                stream.Write(fresh, 0, fresh.Length);
            }

            RestrictToOwner(path);
        }
        catch (IOException)
        {
            // Someone else created it first; fall through and read it.
        }

        var key = File.ReadAllBytes(path);

        if (key.Length != KeyBytes)
        {
            throw new InvalidOperationException(
                $"The secrets key at {path} is {key.Length} bytes, " +
                $"expected {KeyBytes}. It is corrupt; the secrets it " +
                "protected cannot be read until it is restored.");
        }

        return key;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            /*
             * Best effort, the way DataFolderSecurity treats its own
             * ACL work: the owner-only directory above still shields
             * the file, and refusing to run over a chmod is worse.
             */
        }
    }
}
