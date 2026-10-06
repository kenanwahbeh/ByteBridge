# Troubleshooting

**502 from the tunnel, or Cloudflare error 1033** — nothing is
listening on the port `cloudflared` forwards to. Check the status line in
the window says **Answering**, and that its port matches the tunnel's
`service:` URL. On the machine itself:

```
netstat -ano | findstr :8080
curl http://127.0.0.1:8080/health
```

An empty `netstat` means the gateway is stopped.

**"Port 8080 is already in use"** — something else holds it. Change
the port in **Web Server** and update the tunnel config to
match, or free the port:

```
netstat -ano | findstr :8080
tasklist /fi "pid eq <pid>"
```

**"Windows refused to reserve ..."** — HTTP.SYS wants a URL
reservation. Run ByteBridge as administrator once, or grant it from
an elevated prompt:

```
netsh http add urlacl url=http://127.0.0.1:8080/ user="%USERNAME%"
```

**409 on every query** — the connection is Offline. Turn it Online in
the app; the gateway re-reads the connection list on every request,
so the change applies immediately.

**The database server itself is unreachable** — `/health` still
answers, because it does not touch the database. `/query` returns the
engine's own error, which is the one to act on.

**409 naming a database engine** — the connection was added by a build
that supported an engine this one does not. Its details are kept, so open
it in the app and choose Firebird or PostgreSQL in the **Database type**
box; it is counted as neither Online nor Offline until you do.

**The card says "Engine not supported"** — the same thing, without a
request involved. The engine box opens with nothing selected on purpose,
so that an unrelated edit cannot turn the row into a working Firebird
connection against an endpoint that does not speak it.
