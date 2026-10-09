# Linux

Linux is a supported target for the **gateway**. It is the same
service as on Windows, run by systemd instead of the Windows service
manager, and administered from a terminal because there is no control
panel. CI publishes the `linux-x64` bundle on every change, checks that
it carries `bytebridge.unit` and `install.sh`, starts it once, and
checks the data folder is owner-only.

## What differs from Windows

| | Windows | Linux |
| - | ------- | ----- |
| Installer | `.exe` / `.msi` from the release | `.deb` or `.tar.gz` from the release |
| Control panel | WPF window | None; use the `bytebridge` command |
| Run by | Windows service `ByteBridge` | systemd unit `bytebridge` |
| Data folder | `C:\ProgramData\ByteBridge` | `/var/lib/bytebridge`, mode `0700` |
| Key that opens the secrets | DPAPI | Key file in `/var/lib/bytebridge-keys`, beside the data folder, not inside it |
| Service output | the service host | `journalctl -u bytebridge` |
| Request log | `C:\ProgramData\ByteBridge\logs\` | `logs/` inside the data folder |
| Runs as | The Windows service account | A `bytebridge` system account with no login shell |

The databases it reaches are the same: Firebird and PostgreSQL.

## Installing

Both files are on the [latest release](https://github.com/kenanwahbeh/ByteBridge/releases/latest).
The bundle is self-contained, so the machine needs no .NET.

**Debian or Ubuntu** — the `.deb`:

```
sudo apt install ./bytebridge_<version>_amd64.deb
```

It creates the `bytebridge` account, puts the service in
`/opt/bytebridge`, installs the systemd unit and the `bytebridge`
command, and starts it. Removing the package stops the service and
keeps the data folder and the key.

**Any other distribution** — the tarball:

```
tar -xzf ByteBridge-<version>-linux-x64.tar.gz
cd ByteBridge-<version>-linux-x64
sudo ./install.sh
```

Checksums are in `SHA256SUMS-linux.txt`, and both files carry a build
attestation: `gh attestation verify <file> --repo kenanwahbeh/ByteBridge`.

**From source** — needs the .NET 10 SDK on the build machine:

```
dotnet publish service/ByteBridge.Service.csproj -c Release -r linux-x64 --self-contained -o publish
sudo ./publish/install.sh
```

`install.sh` creates the `bytebridge` account, copies the bundle to
`/opt/bytebridge`, installs the systemd unit, and starts it. It also
installs a `bytebridge` command in `/usr/local/bin`.

The unit is named `bytebridge.unit` in the bundle, not `.service`, on
purpose: on a case-insensitive filesystem `bytebridge.service` would
overwrite the `ByteBridge.Service` executable. `install.sh` puts it in
place under the right name.

## Administering it

Use the `bytebridge` command. It runs as the service account, so the
settings file is never left owned by root where the service could not
read it.

```
bytebridge status
bytebridge db add --name Sales --server 127.0.0.1 --path /data/sales.fdb --user SYSDBA --password secret
bytebridge key show
bytebridge port 8080
```

Changes apply within a few seconds; the service does not need
restarting. `bytebridge --help` lists everything.

## Moving the data or running without root

Set `BYTEBRIDGE_DATA` to put the data folder somewhere else, and
`BYTEBRIDGE_KEY_DIR` to put the key file somewhere else. The key file
stays outside the data folder either way, so a backup of the data
alone is ciphertext. See
[Backups and moving to a new machine](../reference/security.md#backups-and-moving-to-a-new-machine).

## Reaching it from outside

The gateway binds to loopback only, as on Windows. Run `cloudflared`
on the same machine and point it at `http://127.0.0.1:8080`; see
[Cloudflare Tunnel](../reference/cloudflare-tunnel.md).

## Not available on Linux

- The control panel (WPF is Windows-only).
- The `.exe` and `.msi` installers (the `.deb` and `.tar.gz` replace them).
- The Windows service and DPAPI. The systemd unit and the key file
  take their places.
