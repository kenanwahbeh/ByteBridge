# The request log

Every request the gateway answers is appended to
`C:\ProgramData\ByteBridge\logs\gateway-<date>.jsonl`, one JSON object
per line:

```json
{"at":"2026-09-03T21:40:11.7Z","method":"POST","path":"/query","status":200,
 "elapsedMs":12,"localPeer":"127.0.0.1","clientIp":"203.0.113.7",
 "authenticated":true,"database":"Sales",
 "sql":"SELECT NAME FROM CUSTOMERS WHERE ID = @id","rows":1}
```

A tunnel puts this listener on the public internet, so "what ran, and
who asked for it" has to be answerable afterwards — above all if the
API key leaks. Rejected requests are recorded too, with
`"authenticated": false`; a run of those from one address is what an
attempt on the key looks like.

**Bound parameter values are never written.** The statement is recorded,
the values are not: they are the customer's data, and keeping them out
of the statement is the whole point of binding them. The statement is
truncated past 2000 characters.

`clientIp` comes from Cloudflare's `CF-Connecting-IP` header, since
behind a tunnel the socket peer is always `cloudflared` on loopback.
`localPeer` is kept as well: it is the evidence that a request really
did arrive the expected way.

Files are kept for 30 days and older ones are pruned. Writing happens
after the response is sent, so a slow disk never delays an answer —
which does mean the last entries can be lost if the process is killed
outright.
