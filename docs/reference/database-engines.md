# Database engines

A connection is one of three engines. Pick it when you add the database
(the control panel's wizard asks, and the command line takes `--type`).

| Engine | `--type` | Default port | The "database" field is |
|---|---|---|---|
| Firebird | `firebird` (default) | 3050 | the database file or alias |
| PostgreSQL | `postgresql` | 5432 | the database name |
| SQL Server | `sqlserver` | 1433 | the database name |

```
db add --name Shop --type postgresql --server 10.0.0.5 --database shop --user app --password ...
```

Everything else is the same for all three: the API key, `/query` and
`/execute`, named parameters written `@name`, the row cap, and the
read-only default.

## What keeps `/query` from writing

`/query` only accepts `SELECT` and `WITH`, on every engine. Behind that,
each engine does what it can:

| Engine | Held read-only by the engine | Also refused by ByteBridge |
|---|---|---|
| Firebird | Yes, a read-only transaction | `GEN_ID` with a step other than 0, `NEXT VALUE FOR` |
| PostgreSQL | Yes, `SET TRANSACTION READ ONLY` | `nextval` and `setval` (a sequence still moves in a read-only transaction) |
| SQL Server | **No.** SQL Server has no read-only transaction | More than one statement, `INTO`, `INSERT`/`UPDATE`/`DELETE`/`MERGE`, `EXEC`, `OPENROWSET`/`OPENQUERY`, `xp_` procedures, and `NEXT VALUE FOR`. The query also runs in a transaction that is never committed |

**On SQL Server, give the gateway a login that can only read** (for example
a user in the `db_datareader` role). The text check is a safety net, not a
guarantee, because the engine itself will not stop a write.

## Notes per engine

- **PostgreSQL**: a parameter that arrives as a JSON string is sent as text,
  so compare it to a non-text column with a cast, for example
  `WHERE created > @since::timestamp`. Numbers and booleans need none.
- **SQL Server**: the connection is encrypted but the server certificate is
  not verified, because a server on a LAN usually has a self-signed one.
  Put the gateway on a network you trust, or use a certificate from a CA.
- Every engine is reached over the network from the gateway, so a remote
  database can sit behind a Cloudflare Tunnel like any other service.

## Tests

The PostgreSQL tests run against a real server in CI. SQL Server has no
live job (its image wants more memory than a hosted runner offers), so its
guard is covered by unit tests only. Run the PostgreSQL ones yourself with:

```
BYTEBRIDGE_TEST_POSTGRES=127.0.0.1:5432:postgres:password:shop dotnet test --filter Category=PostgreSql
```
