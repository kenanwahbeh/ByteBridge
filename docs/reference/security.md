# Security

The tunnel makes this reachable from the public internet, so treat the
API key as a database credential.

- Every endpoint except `/health` requires the key, in `X-API-Key` or
  as `Authorization: Bearer`. It is 32 random bytes, generated on first
  run and compared in constant time.
- **New Key** rotates it without restarting the gateway. The running
  gateway picks the new key up within a few seconds, and from then on
  every client still sending the old one is refused.
- Send values in `parameters`, never concatenated into `sql`; they are
  bound as Firebird parameters, so a value cannot become SQL.
- `/query` refuses anything that is not a `SELECT` or `WITH`, so a read
  path cannot write by accident.
- The listener binds to `127.0.0.1` only, and a request body over 1 MB
  is refused.
- Anything holding the key can run arbitrary SQL against the Online
  connections. If the data is sensitive, put
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
