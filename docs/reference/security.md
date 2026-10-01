# Security

The tunnel makes this reachable from the public internet, so treat the
API key as a database credential.

- Every endpoint except `/health` requires the key, in `X-API-Key` or
  as `Authorization: Bearer`. That includes `/stats`, which lists every
  connection and, unlike the rest, is not open to a signed-in Access
  session on its own. The key is 32 random bytes, generated on first run and
  compared in constant time.
- Wrong keys are slowed down: by default, once a caller has sent 10
  wrong keys in a minute, its next requests get a `429` for the next
  minute (see [Failed-key lockout](#failed-key-lockout)).
- **New Key** rotates it without restarting the gateway. The running
  gateway picks the new key up within a few seconds, and from then on
  every client still sending the old one is refused.
- Send values in `parameters`, never concatenated into `sql`; they are
  bound as Firebird parameters, so a value cannot become SQL.
- ByteBridge only reads until an administrator ticks **Options → Allow
  writing**. `/query` refuses anything that is not a `SELECT` or `WITH`
  either way, and runs what it accepts in a transaction that Firebird
  itself holds read-only, so a statement sent to it cannot change rows.
  Generators change outside transactions, so `GEN_ID` with a step other
  than 0 and `NEXT VALUE FOR` are refused as well; a procedure that
  moves one inside its own body can only be stopped by limiting the
  Firebird user. While writing is off, `/execute` answers `403`. While
  it is on, anyone holding the API key, or signed in through ByteBridge's
  Cloudflare login, can run any statement: change and delete data and
  change the structure of your databases. Cloudflare Access in front of
  the tunnel is an extra layer, not a replacement for the key. Leave
  writing off unless you need it, and turn it off again afterwards.
- The listener binds to `127.0.0.1` only, and a request body over 1 MB
  is refused.
- Anything holding the key can read whatever the Firebird user of an
  Online connection can read. If the data is sensitive, put
  [Cloudflare Access](https://developers.cloudflare.com/cloudflare-one/policies/access/)
  in front of the hostname as well, so callers are authenticated at
  Cloudflare's edge before a request reaches the machine at all — see
  [Locking the tunnel to just you](#locking-the-tunnel-to-just-you) below.
- Every request, served or rejected, is appended to
  `C:\ProgramData\ByteBridge\logs\`. Bound parameter values are never
  written; the statement is. See [The request log](request-log.md).

Connections are stored in
`C:\ProgramData\ByteBridge\bytebridge.db`. Firebird passwords are kept
there in plain text, so that file deserves the same care as the
credentials themselves.

## Failed-key lockout

A caller that sends too many wrong API keys is refused with
`429 Too Many Requests` and a `Retry-After` header, before its key is
looked at, so the block holds even if the next key is right. The wrong
key that reaches the limit is still answered `401`; it is the next
request that meets the `429`. Requests that send no key at all are not
counted, a correct key clears the tally, and `/health` is never
blocked. `/auth/me`, which also answers a right key, counts like any
other endpoint.

The defaults are 10 wrong keys inside 60 seconds, blocking for 60
seconds. Change them, or turn the limit off, whichever way suits you:

- **In the app:** *Web Server* has *Lock out after wrong keys*
  (`0` turns it off) and *Lock out for (minutes)*.
- **From a terminal:**

  ```bat
  ByteBridge.Service.exe lockout show
  ByteBridge.Service.exe lockout set --attempts 5 --window 30 --block 900
  ByteBridge.Service.exe lockout off
  ByteBridge.Service.exe lockout on
  ```

A change is picked up within a few seconds; the service does not need
restarting, though it briefly re-binds its listener when it does.

Callers are told apart by the address Cloudflare reports in
`CF-Connecting-IP`, which Cloudflare overwrites, so a caller cannot
choose it. Without a tunnel, that is the socket address. The gateway
listens on loopback only, so the one caller who can send that header
itself is a program already running on this machine: it can dodge the
count, or run up another address's, but it still needs the key to get
anything. The header is honoured only from a loopback peer. The tally is
kept in memory: a restart forgives everyone.

If you use Cloudflare's own rate limiting in front of the hostname as
well, that is stronger, since it stops the traffic at the edge and never
reaches this machine.

## Locking the tunnel to just you

The gateway binds to loopback and `cloudflared` reaches it locally, so
nothing is exposed on the LAN. But the tunnel's hostname is on the
public internet: anyone who discovers it reaches the API, and the key is
then the only thing in the way.

[Cloudflare Access](https://developers.cloudflare.com/cloudflare-one/policies/access/)
closes that gap by authenticating at Cloudflare's edge, before a request
ever reaches the machine.

For a program rather than a person, use a **service token**:

1. In Zero Trust, go to **Access → Service auth** and create a service
   token. Keep the Client ID and Client Secret.
2. Go to **Access → Applications**, add a **Self-hosted** application
   for the gateway's hostname.
3. Add a policy with action **Service Auth** and the rule
   *Service Token* → the token you just made.
4. Add a second policy with action **Bypass** for the path `/health`
   only, if you want liveness checks to stay reachable without the
   token.

Callers then send two extra headers:

```bash
curl https://<hostname>/query \
  -H "CF-Access-Client-Id: <client-id>" \
  -H "CF-Access-Client-Secret: <client-secret>" \
  -H "X-API-Key: <key>" \
  -H "Content-Type: application/json" \
  -d '{ "database": "Sales", "sql": "SELECT 1 FROM RDB$DATABASE" }'
```

The API key stays in place. Access decides who may reach the gateway at
all; the key decides what they may do once they have. Losing one still
leaves the other.
