# API reference

ByteBridge exposes the configured database connections over a small
JSON API on loopback, so a Cloudflare Tunnel running on the same
machine has something to forward requests to.

The listener starts with the machine. Its status, port and API key are
shown in the **Web Server** window, on the control panel's menu bar.
The gateway itself runs as the `ByteBridge` Windows service, so it is
up whether or not that window is open.

## Endpoints

| Method | Path         | Auth | Purpose                                  |
| ------ | ------------ | ---- | ---------------------------------------- |
| GET    | `/health`    | no   | Liveness. Use it to test the tunnel.     |
| GET    | `/databases` | yes  | List the configured connections.         |
| POST   | `/query`     | yes  | Run a `SELECT` / `WITH` and get rows.    |
| POST   | `/execute`   | yes  | Run an `INSERT` / `UPDATE` / `DELETE`. Only while **Allow writing** is on. |

ByteBridge only reads unless an administrator ticks **Options → Allow
writing** in the app. `/query` takes a `SELECT` or a `WITH` and nothing
else, and runs it in a transaction that Firebird itself holds
read-only, so a statement sent to it cannot change rows however it is
written. Generators change outside transactions, so `GEN_ID` with a
step other than 0 and `NEXT VALUE FOR` are refused as well. A procedure
that moves a generator inside its own body cannot be seen from here;
limit the Firebird user if that matters.

`/execute` is for writes and answers `403` while **Allow writing** is
off, which is how ByteBridge ships. The setting takes effect on the
next request, with no restart.

## Authentication

Every endpoint except `/health` requires the API key generated on
first run:

```
X-API-Key: <key>
```

`Authorization: Bearer <key>` is accepted as well. Copy the key from
the **Copy API Key** button. **New Key** rotates it without restarting
the gateway: the running gateway picks the new key up within a few
seconds, and refuses the old one from then on.

`/health` is deliberately open so the tunnel can be verified before
any key is involved. It returns no data from any database.

## Examples

List the connections:

```bash
curl -H "X-API-Key: $KEY" https://your-tunnel.example.com/databases
```

Run a query. `database` accepts either the connection name shown in
the app or its id:

```bash
curl -X POST https://your-tunnel.example.com/query \
  -H "X-API-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{
        "database": "Sales",
        "sql": "SELECT ID, NAME FROM CUSTOMERS WHERE ID = @id",
        "parameters": { "id": 42 },
        "maxRows": 200
      }'
```

```json
{
  "columns": ["ID", "NAME"],
  "rows": [[42, "Acme Ltd"]],
  "rowCount": 1,
  "truncated": false,
  "elapsedMs": 12
}
```

`truncated` is `true` when the result hit the row cap and more rows
were left unread — narrow the query or page through it.

Write, with **Allow writing** on:

```bash
curl -X POST https://your-tunnel.example.com/execute \
  -H "X-API-Key: $KEY" \
  -H "Content-Type: application/json" \
  -d '{
        "database": "Sales",
        "sql": "UPDATE CUSTOMERS SET NAME = @name WHERE ID = @id",
        "parameters": { "id": 42, "name": "Acme Limited" }
      }'
```

```json
{ "rowsAffected": 1, "elapsedMs": 8 }
```

Always pass values through `parameters` rather than concatenating
them into `sql`; they are bound as Firebird parameters, so a value
cannot turn into SQL.

## Types

| Firebird              | JSON                                |
| --------------------- | ------------------------------------ |
| `INTEGER`, `BIGINT`   | number                              |
| `NUMERIC`, `DECIMAL`  | number, scale preserved             |
| `VARCHAR`, `CHAR`     | string                              |
| `DATE`, `TIMESTAMP`   | ISO-8601 string                     |
| `BLOB SUB_TYPE TEXT`  | string                              |
| `BLOB` (binary)       | base64 string                       |
| `NULL`                | `null`                              |

## Status codes

| Code | Meaning                                                           |
| ---- | ------------------------------------------------------------------ |
| 400  | Bad request body, a write sent to `/query`, or invalid SQL.       |
| 400  | `database` names more than one connection; send its `id` instead. |
| 401  | Missing or wrong API key.                                         |
| 403  | `/execute` while **Allow writing** is off.                        |
| 404  | Unknown endpoint, or no connection matches `database`.            |
| 405  | Wrong HTTP method for the endpoint.                               |
| 409  | The connection exists but is **Offline** in the app.              |
| 413  | Request body over the 1 MB limit.                                 |
| 500  | Unexpected server error.                                          |

Errors are `{ "error": "..." }`.
