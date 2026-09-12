# Cloudflare Tunnel

The gateway binds to loopback only. `cloudflared` runs on the same
machine and reaches it over `127.0.0.1`, so nothing needs to be
opened on the LAN or the router.

Quick tunnel, for testing:

```bash
cloudflared tunnel --url http://127.0.0.1:8080
```

Named tunnel, in `config.yml`:

```yaml
tunnel: <tunnel-uuid>
credentials-file: C:\Users\<you>\.cloudflared\<tunnel-uuid>.json

ingress:
  - hostname: bytebridge.example.com
    service: http://127.0.0.1:8080
  - service: http_status:404
```

Then confirm the whole path end to end:

```bash
curl https://bytebridge.example.com/health
```

```json
{ "status": "ok", "service": "ByteBridge", "connections": 2, "online": 1, ... }
```

Anyone who reaches the hostname can try the API, so treat the key as
a database credential. Cloudflare Access in front of the hostname is
worth adding if the data is sensitive — see
[Locking the tunnel to just you](security.md#locking-the-tunnel-to-just-you).
