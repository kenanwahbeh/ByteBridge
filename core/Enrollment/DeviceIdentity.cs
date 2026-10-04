using System.Security.Cryptography;
using System.Text;

namespace ByteBridge.Enrollment;

/*
 * What identifies this installation to the ByteBalance control plane.
 *
 * Two different values, for two different jobs:
 *
 *   deviceKey    Public. Names the machine, so enrolling twice from the
 *                same machine is one device, not two tunnels. Derived
 *                from the operating system's machine id, so it survives
 *                a reinstall.
 *
 *   claimSecret  Private. Random, kept in the settings file and never
 *                sent until the approved token is collected. Only its
 *                SHA-256 goes out at enrolment, so knowing a deviceKey
 *                is not enough to take somebody else's tunnel.
 */
public interface IMachineId
{
    string Get();
}

public sealed class SystemMachineId : IMachineId
{
    public string Get()
    {
        if (OperatingSystem.IsWindows())
        {
            var guid = ReadWindowsMachineGuid();

            if (!string.IsNullOrWhiteSpace(guid))
            {
                return guid;
            }
        }

        return Environment.MachineName;
    }

    /*
     * Registry64 explicitly: a 32-bit process would otherwise be
     * redirected to a different, empty hive and fall back to the
     * machine name, which is not unique.
     */
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        try
        {
            using var hive = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine,
                Microsoft.Win32.RegistryView.Registry64);

            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");

            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class DeviceIdentity
{
    /*
     * Hashed with a fixed label so the raw machine id, which other
     * software uses to identify the machine, is not handed to a server.
     * "bb-" plus 40 hex characters sits inside the 8 to 128 the control
     * plane accepts.
     */
    public static string DeviceKey(string machineId)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            throw new ArgumentException(
                "A machine id is required.", nameof(machineId));
        }

        return "bb-" + Sha256Hex("bytebridge-agent:" + machineId.Trim())[..40];
    }

    /*
     * 32 random bytes as base64url, the same shape the control plane
     * uses for its own secrets.
     */
    public static string NewClaimSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /*
     * Lower-case hex of the UTF-8 bytes, which is what the control
     * plane computes over the secret it is later shown.
     */
    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
