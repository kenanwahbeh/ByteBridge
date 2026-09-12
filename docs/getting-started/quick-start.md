# Quick start

1. **Add a database.** Click **+ Add Data**, fill in the database
   server, port, user, password and database path or alias, and use
   **Test Connection** before saving.
2. **Turn it Online.** The card's toggle tests the connection first and
   stays Offline if it fails. Only Online connections answer requests.
3. **Check the gateway.** The **Gateway API** panel should read
   *Answering — http://127.0.0.1:8080*, with *Service: running* beneath
   it. Press **Copy API Key**.
4. **Start the tunnel.**

   ```
   cloudflared tunnel --url http://127.0.0.1:8080
   ```

5. **Confirm the whole path.** `/health` needs no key, so it is the
   first thing to try:

   ```
   curl https://<your-hostname>/health
   ```

   ```json
   { "status": "ok", "service": "ByteBridge", "connections": 1, "online": 1, ... }
   ```

6. **Query.**

   ```bash
   curl -X POST https://<your-hostname>/query \
     -H "X-API-Key: <your key>" \
     -H "Content-Type: application/json" \
     -d '{ "database": "Sales", "sql": "SELECT ID, NAME FROM CUSTOMERS WHERE ID = @id", "parameters": { "id": 42 } }'
   ```

See the [API reference](../reference/api-reference.md) for the full
endpoint list, request/response shapes, and type mapping.
