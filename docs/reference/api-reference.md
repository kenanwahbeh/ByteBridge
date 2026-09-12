# API reference

ByteBridge exposes the configured database connections over a small
JSON API on loopback, so a Cloudflare Tunnel running on the same
machine has something to forward requests to.

The listener starts with the machine. Its status, port and API key are
shown in the **Gateway API** panel of the control panel window.
The gateway itself runs as the `ByteBridge` Windows service, so it is
up whether or not that window is open.

## Endpoints

| Method | Path         | Auth | Purpose                                  |
| ------ | ------------ | ---- | ---------------------------------------- |
| GET    | `/health`    | no   | Liveness. Use it to test the tunnel.     |
| GET    | `/databases` | yes  | List the configured connections.         |
| POST   | `/query`     | yes  | Run a `SELECT` / `WITH` and get rows.    |
| POST   | `/execute`   | yes  | Run an `INSERT` / `UPDATE` / `DELETE`.   |

`/query` rejects anything that is not a `SELECT` or a `WITH` so a
read path cannot write by accident. Use `/execute` for writes.

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

Always pass values through `parameters` rather than concatenating
them into `sql`; they are bound as Firebird parameters, so a value
cannot turn into SQL.

Write:

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
| 404  | Unknown endpoint, or no connection matches `database`.            |
| 405  | Wrong HTTP method for the endpoint.                               |
| 409  | The connection exists but is **Offline** in the app.              |
| 413  | Request body over the 1 MB limit.                                 |
| 500  | Unexpected server error.                                          |

Errors are `{ "error": "..." }`.
