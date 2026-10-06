# Database engines

A connection is one of two engines. Pick it when you add the database
(the control panel's wizard asks, and the command line takes `--type`).

| Engine | `--type` | Default port | The "database" field is |
|---|---|---|---|
| Firebird | `firebird` (default) | 3050 | the database file or alias |
| PostgreSQL | `postgresql` | 5432 | the database name |

```
db add --name Shop --type postgresql --server 10.0.0.5 --database shop --user app --password ...
```

Everything else is the same for both: the API key, `/query` and
`/execute`, named parameters written `@name`, the row cap, and the
read-only default.

## What keeps `/query` from writing

`/query` only accepts `SELECT` and `WITH`. Behind that, **the engine
itself holds the transaction read-only**, so a statement that got past
the text check — a `SELECT` that calls a procedure which writes, say —
is refused by the database rather than trusted not to write.

| Engine | Held read-only by the engine | Also refused by ByteBridge |
|---|---|---|
| Firebird | Yes, a read-only transaction | `GEN_ID` with a step other than 0, `NEXT VALUE FOR` |
| PostgreSQL | Yes, `SET TRANSACTION READ ONLY` | `nextval` and `setval`; more than one statement; session-level advisory locks |

The sequence cases are refused separately because a sequence moves
outside any transaction: PostgreSQL still lets one move inside a
read-only one, so the read-only transaction does not cover it.

### Why PostgreSQL refuses one statement

The read-only transaction is not the whole answer on PostgreSQL, and
the difference is worth stating plainly.

Npgsql sends `SELECT 1; DELETE FROM t` as one batch, so both statements
run inside the read-only transaction, and the engine refuses the second
one. But a transaction-control statement in the middle ends it: in
`SELECT 1; COMMIT; DELETE FROM t` the `COMMIT` closes the read-only
transaction, and the `DELETE` after it runs in a new one that is not
read-only. There the text check is the only thing standing in the way,
so `/query` takes exactly one statement.

A single trailing semicolon is fine. Literals, comments, quoted names
and dollar-quoted bodies are skipped first, so a function body written
in `$$ ... $$` may hold semicolons of its own.

### Why PostgreSQL connections are not pooled

A pooled connection is reset when it is next handed out, not when it is
returned, so anything session-scoped outlives the request. A
session-level advisory lock belongs to the session, so rolling the
transaction back does not release it: `/query` refuses
`pg_advisory_lock` and its variants, and the connection is not pooled
either, so a lock taken inside a function the statement calls cannot
outlive the request either. The transaction-scoped `pg_advisory_xact_lock`
is left allowed — it dies with the transaction.

The cost is a connection per request, which is nothing beside the
tunnel round trip every one of these requests already pays.

## Why there are only two

A third engine was written and then taken back out, and the reason is
worth keeping on the record because it is the bar any next one has to
clear.

**SQL Server has no read-only transaction.** Nothing in the engine
refuses a write, so refusing one would come down entirely to a text
check — and a text check cannot see every shape a `SELECT` can take. A
`UNION ALL` reads a table the statement never named while looking like
an ordinary `SELECT`; no keyword list catches that. On the two engines
here the text check is a courtesy. There it would be the whole of it.

**And none of it had ever run against a SQL Server.** Its image wants
more memory than a hosted CI runner offers, so its connection string and
guard were covered by unit tests alone — shipping an engine as the only
thing between a public tunnel and somebody's database, on tests that
never reached a server.

What would bring it back: a guard that sees through `UNION`, and one
real server to run against. `SqlServerIntegrationTests` is written and
runs nothing until `BYTEBRIDGE_TEST_SQLSERVER` names one.

## A settings file from a build with a third engine

Such a file opens. The row keeps its name, server, credentials and
password, so the control panel can show it and let an administrator pick
a supported engine — but nothing is dispatched through it: a request for
it is answered `409` naming the engine, and the health probe skips it
rather than reporting a database that is working as broken.

It is not counted as online anywhere. `/health`, `/databases`, the
control panel and the companion agent all read one property,
`DatabaseConfig.IsOnline`, which requires a supported engine as well as
an enabled flag and a successful test — a stored test result from
before the engine went is not the present. On the card it says
**Engine not supported** rather than Offline, because the connection is
enabled and the server may well be fine.

And the wizard will not save one: the engine box is disabled with
nothing selected, so editing the name or the password cannot quietly
turn the row into a working Firebird connection against an endpoint
that does not speak it. Choosing an engine is the only way out.

## Notes

- **PostgreSQL**: a parameter that arrives as a JSON string is sent as text,
  so compare it to a non-text column with a cast, for example
  `WHERE created > @since::timestamp`. Numbers and booleans need none.
- Every engine is reached over the network from the gateway, so a remote
  database can sit behind a Cloudflare Tunnel like any other service.
- Both drivers ship with the app. Measured, PostgreSQL adds about 1 MB
  to the self-contained publish (85 MB to 86 MB) — each next engine is a
  measured cost, not a guess.

## Tests

The PostgreSQL tests run against a real server in CI. Run them yourself
with:

```
BYTEBRIDGE_TEST_POSTGRES=127.0.0.1:5432:postgres:password:shop dotnet test --filter Category=PostgreSql
```