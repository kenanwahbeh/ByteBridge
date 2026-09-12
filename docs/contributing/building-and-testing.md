# Building and testing

## Build from source

Needs the .NET 10 SDK and Windows.

```
dotnet build ByteBridge.slnx
dotnet run --project ByteBridge.csproj
```

To produce the installers the way the release does, see
[`.github/workflows/release.yml`](https://github.com/kenanwahbeh/ByteBridge/blob/main/.github/workflows/release.yml).
WiX and Inno Setup both only run on Windows.

## Tests

```
dotnet test tests/ByteBridge.Tests
```

The suite drives the gateway over a real socket on a spare port, with
its settings in a temporary folder, so it never reads or overwrites the
connections of whoever is running it.

| Area | What it covers |
| ---- | -------------- |
| `GatewayServerTests` | Routing, the API key, CORS, body validation, the read-only guard, the size cap, and that an early reply leaves the connection usable. |
| `GatewayLifecycleTests` | Start, stop, restart, a port already in use, key rotation, and connection changes taking effect without a restart. |
| `SqliteDatabaseTests` | Storage, duplicate details and names, gateway settings including the loopback-only host, and a key rotation surviving another writer's save. |
| `FirebirdExecutorTests` | The read-only guard on its own, including comments, word boundaries and unterminated blocks. |
| `FirebirdIntegrationTests` | A real server end to end: type mapping, NULLs, UTF-8, parameter binding, the row cap, writes, and concurrency. |
| `CliTests` | The Server Core commands: status, on and off, the port, showing and replacing the key, and adding, enabling, disabling and removing a database. |
| `RequestLogTests` | The log file itself: one JSON line per request, parameter values kept out, long statements truncated, concurrent writes, and pruning old files. |
| `RequestLoggingTests` | The log as the running gateway fills it: served and refused requests, the statement and connection, the forwarded client address, and every request landing exactly once under load. |
| `SharedDatabaseTests` | The settings file open in two processes at once: WAL mode, one seeing what the other wrote, a write waiting for a lock, and both writing together. |

`FirebirdIntegrationTests` is skipped unless you point it at a server,
so a fresh clone still gets a green run:

```powershell
$env:BYTEBRIDGE_TEST_FIREBIRD = "127.0.0.1:3050:SYSDBA:masterkey:C:\db\test.fdb"
dotnet test tests/ByteBridge.Tests
```

It creates its own `EFS_TEST_CUSTOMERS` table and works only on rows it
owns, but point it at a scratch database rather than anything real.

[CI](https://github.com/kenanwahbeh/ByteBridge/blob/main/.github/workflows/ci.yml)
runs on every push and pull request, in two jobs: the build and the
whole suite on `windows-latest`, where the live Firebird tests skip for
want of a server, and those same tests on `ubuntu-latest` against
Firebird 4 in a service container. The test project targets plain
`net10.0`, so it runs on Linux unchanged even though the app itself is
Windows-only.
