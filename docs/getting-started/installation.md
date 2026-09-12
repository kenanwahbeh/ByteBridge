# Installation

Grab an installer from the
[latest release](https://github.com/kenanwahbeh/ByteBridge/releases/latest).

| File | .NET | Use it when |
| ---- | ---- | ----------- |
| `ByteBridge-<version>-x64-setup.exe` | included | **Start here.** Normal desktop install. |
| `ByteBridge-<version>-x64.msi` | included | Group Policy, Intune, or a scripted rollout. |
| `ByteBridge-<version>-x64-framework-setup.exe` | fetched | Much smaller download; Setup installs the runtime if the machine lacks it. |
| `ByteBridge-<version>-x64-framework.msi` | required | Scripted rollout where the runtime is managed separately. |

The `-framework` builds need the
[.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0);
the `-framework` **.exe** downloads and installs it when it is missing,
while the `.msi` expects your deployment tool to handle it. The bundled
builds need nothing else installed.

Both `.exe` installers also offer to install `cloudflared`, skipping the
offer when it is already present. That gets you the connector; pointing
it at a tunnel still needs your own token, which is the whole point —
see [Cloudflare Tunnel](../reference/cloudflare-tunnel.md).

Requires 64-bit Windows 8.1 or later. All installers are per-machine
and ask for administrator rights once.

## Silent install

Both `.exe` installers accept Inno Setup's standard silent switches:

```
ByteBridge-<version>-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

The `.msi` files install unattended the standard MSI way:

```
msiexec /i ByteBridge-<version>-x64.msi /quiet /norestart
```

In silent mode, missing prerequisites (the .NET Desktop Runtime,
`cloudflared`) are downloaded and installed automatically instead of
prompting — there is nobody to answer a prompt during an unattended
rollout.

## It runs as a service

The gateway is a Windows service, `ByteBridge`, installed and started
for you. It starts with the machine and serves with nobody signed in,
so an unattended server is a supported target and closing the window
does not take the gateway down with it.

The window is a control panel for that service. It shows two things
separately, because they are not the same: whether Windows is running
the service, and whether the gateway is actually answering, which it
checks by calling `/health` over loopback. A running service whose
gateway could not bind is exactly what is behind a `502`, so it is
named rather than reported as healthy.

It asks for administrator rights, because
`C:\ProgramData\ByteBridge` holds your database passwords in the clear
beside the API key, and that key is all that stands between the public
internet and those databases. The folder is restricted to
Administrators and the service account, so no other account on the
machine can read it.

### Windows Server Core

Server Core has no desktop, so the control panel cannot run there. The
service executable doubles as an admin tool:

```
ByteBridge.Service.exe status
ByteBridge.Service.exe db add --name Sales --server 127.0.0.1 ^
    --path C:\data\sales.fdb --user SYSDBA --password secret
ByteBridge.Service.exe key show
ByteBridge.Service.exe port 8080
```

Changes apply within a few seconds; the service does not need
restarting. Run `ByteBridge.Service.exe --help` for the full list. On a
machine with a desktop you never need any of this.
