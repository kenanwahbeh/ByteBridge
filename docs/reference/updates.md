# Updates

ByteBridge tells you when a newer release exists. It does not install
one by itself.

That is deliberate. The service runs as the machine's most privileged
account and sits in front of your databases, so a service that
downloaded and ran programs on its own would turn a compromise of the
release page into code running on every gateway. Finding out is
automatic; installing is a decision.

## What it does

About once a day the service asks GitHub for the newest published
release of ByteBridge and compares it with the version that is running.
The answer is written to the settings file, and everything else reads it
from there:

- **The control panel** shows a banner when a newer version exists, with
  **Update now**, **What's new** and **Later**. **Help → Check for
  Updates** asks straight away.
- **The command line** reports it: `update status` shows the last answer
  and `update check` asks now.
- **`GET /stats`** carries `version` and an `update` object, so whatever
  watches the gateway can see which ones are behind.

Only a stable release is ever offered. A pre-release is never shown to
someone on a stable version.

## What leaves the machine

One anonymous `GET https://api.github.com/repos/kenanwahbeh/ByteBridge/releases/latest`
a day. GitHub sees the machine's address and a `User-Agent` of
`ByteBridge/<installed version>`. No key, no database name, no setting
and no identifier of yours is sent.

To stop it:

```
ByteBridge.Service.exe update off
```

(`bytebridge update off` on Linux.) The same switch is **Check for
updates** under Options in the control panel. With it off nothing is
sent unless you ask with `update check` or **Check now**.

## Installing an update

### Windows

Press **Update now** in the banner. The control panel:

1. asks for your agreement;
2. downloads the installer that matches how this machine was set up —
   the Setup program or the MSI, with or without the bundled .NET — so
   an MSI is never "upgraded" by a Setup program that would register a
   second copy beside it;
3. checks it against `SHA256SUMS.txt` published with the release, and
   throws it away if it does not match;
4. starts the installer and closes itself.

The installer then does what it always does on an upgrade: it stops the
service, replaces the files, and starts the service again. Your
connections, API key and settings are kept.

The download is kept in the `updates` folder inside ByteBridge's data
folder, which only administrators can write to, so another program
cannot swap the file between the check and the run.

If the release has no installer that matches, the panel opens the
release page instead.

### Linux

With the apt repository, updates arrive with the rest of the system:

```
sudo apt update && sudo apt install --only-upgrade bytebridge
```

apt checks every package against the repository's signing key, which is
stronger than the checksum above. To have them installed without asking,
use Debian's own `unattended-upgrades` and allow the ByteBridge
repository in its configuration; ByteBridge does not set that up for
you.

Installed from the tarball: download the new one from the release page
and run `install.sh` from it again. It replaces the program and keeps
the data.

## How far to trust the download

The checksum catches a truncated or corrupted download and a file that is
not the one named. It does not defend against someone who controls the
release itself, because the checksums sit beside the files.

If you want to check that an installer came from this repository's
release workflow, GitHub can verify its build attestation:

```
gh attestation verify ByteBridge-3.3.0-x64-setup.exe --repo kenanwahbeh/ByteBridge
```

## Troubleshooting

| Message | Meaning |
| ------- | ------- |
| `GitHub is limiting requests from this address` | Many machines behind one address share GitHub's anonymous allowance. Try again later; nothing is lost. |
| `Could not reach GitHub` | The machine has no route to `api.github.com`. Updates are checked again by themselves. |
| `GitHub has no published release to compare with` | The repository has no stable release yet. |

A machine that cannot reach GitHub keeps working exactly as before; only
the check fails.
