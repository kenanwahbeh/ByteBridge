# Connecting to ByteBalance

ByteBridge only listens on `127.0.0.1`. To let a ByteBalance storefront read
from this machine it needs a Cloudflare Tunnel, and ByteBalance creates that
tunnel for you: you ask, ByteBalance approves, and the connector is installed
here. Nothing on your router or firewall changes.

Use whichever is convenient; they run the same code.

| Where | How |
| --- | --- |
| Installer | The "Connect to ByteBalance" page after the task choices. Silent installs: `/ByteBalanceEmail=owner@example.com` |
| Control panel | **Connect to ByteBalance** in the menu bar |
| Command line | `ByteBridge.Service.exe enroll --email owner@example.com` |

## What happens

1. `enroll` remembers a fresh secret on this machine, then asks ByteBalance for a
   tunnel. Only a hash of the secret is sent. ByteBalance emails its approver.
2. Once approved, ByteBalance creates a tunnel for this machine and puts a
   Cloudflare Access application in front of it. **Only the email you gave can
   get through**; Cloudflare mails that address a one-time code to sign in.
3. This machine collects the tunnel's token with the secret from step 1. Before
   the tunnel comes up, the gateway is told to check the Access token itself
   (see below), then `cloudflared service install` runs with the token.
4. The gateway's API key is sent to ByteBalance, encrypted at rest there, so the
   storefront can call this gateway.

Each step can be repeated. If a run is interrupted, or you close the window,
run `claim` (or open the dialog again): it continues where it stopped and never
starts a second request.

## Commands

Run from an **elevated** terminal (they install or remove a Windows service):

```
enroll --email <you@example.com> [--name <name>] [--server <url>]
       [--timeout <minutes>] [--no-wait] [--replace-connector]
claim  [--timeout <minutes>] [--replace-connector]
enrollment                 the enrolment, the connector, whether the key is shared
sync-key                   send the current API key to ByteBalance again
unenroll                   remove the connector and forget the enrolment
```

`status` also shows a one-line summary. `enroll --no-wait` returns exit code `2`
("request sent, waiting"), which scripts can tell apart from a failure.

- `--email` is required and **cannot be changed** by running `enroll` again. Only
  the ByteBalance administrator removing the device lets a machine start over.
- `--server` defaults to `https://bytebalancetech.com` (or `BYTEBALANCE_URL`).
  Only `https://` is accepted, or `http://` to this machine for development.
- An existing Cloudflare connector service is **never replaced by surprise**: it
  may be carrying another tunnel. Pass `--replace-connector` only when that is
  intended.
- Rotating the key with `key new` sends the new key to ByteBalance straight
  away. If ByteBalance cannot be reached, the rotation still succeeds and a
  warning says to run `sync-key`; until then the storefront is locked out.

## The gateway checks the Access token too

Cloudflare Access already stops anyone but the owner at the edge. After
enrolment the gateway also requires the Access token itself on every request
that arrives through Cloudflare, so a request that somehow got past the edge is
still refused (`403`). It is a requirement **on top of** the API key, never a
substitute for it.

- Requests that did not come through Cloudflare, such as a local report calling
  `127.0.0.1`, are not asked for a token; they still need the key.
- `/health` stays open. `/stats` needs the API key like everything else, and
  a request for it that came through Cloudflare needs the token too.
- It is separate from the Cloudflare Access *login* (`oauth on`). Enrolment never
  switches login on, and leaves alone login you set up for another application.
- `oauth edge on | off` turns it on or off by hand; `oauth show` says which.

Cloudflare puts a `Cf-Ray` header on every request it forwards and a caller
cannot remove it; that is how "through Cloudflare" is recognised.

## What is stored, and where

In the settings file (`C:\ProgramData\ByteBridge\bytebridge.db`, readable by
administrators and the service only), under `Enrollment.`: the server, a device
key, the **claim secret**, your email, the phase and the tunnel's hostname. The
claim secret is as sensitive as the API key: with it someone could replace the
key ByteBalance holds for this gateway. The tunnel token is never stored here;
cloudflared keeps it, as it does for every tunnel.

## Leaving

`unenroll` removes the connector this enrolment installed and forgets the
enrolment here. It cannot tell ByteBalance: the device stays there until its
administrator removes it, and until then this machine cannot enrol again,
because the secret that proves it is the one just deleted.
