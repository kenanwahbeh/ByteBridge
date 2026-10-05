using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ByteBridge.Security;

/*
 * Windows: DPAPI with the LocalMachine scope.
 *
 * LocalMachine rather than CurrentUser because the three processes
 * that share bytebridge.db run as different accounts: the service as
 * Local System, the control panel and the command line as whichever
 * administrator is signed in. CurrentUser would encrypt for one of
 * them and lock the other two out.
 *
 * The cost is known and accepted: any process on the machine can ask
 * DPAPI to decrypt, so this is not a barrier against code already
 * running locally -- the data-folder ACL is. What it stops is the
 * file being useful anywhere else: the DPAPI master key never leaves
 * the machine, so a copied bytebridge.db is ciphertext.
 */
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : SecretProtectorBase
{
    protected override byte[] ProtectBytes(byte[] plaintext) =>
        ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);

    protected override byte[] UnprotectBytes(byte[] protectedBytes) =>
        ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
}
