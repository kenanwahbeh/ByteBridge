# How the code fits together

This page is the map a new contributor needs before opening any file:
what the projects are, which process does what, how they talk, and
where a given change belongs. For building and running the tests, see
[Building and testing](../contributing/building-and-testing.md); for
the HTTP contract itself, the [API reference](../reference/api-reference.md).

## The one-paragraph version

ByteBridge is three .NET projects around one SQLite file. A **Windows
service** hosts an `HttpListener` on loopback that turns JSON requests
into Firebird queries. A **WPF control panel** edits the settings the
service runs from. Neither talks to the other: both open
`C:\ProgramData\ByteBridge\bytebridge.db`, the panel writes, and the
service notices within five seconds. Everything that is not UI or
service hosting lives in a portable **core** library, which is also
what the tests exercise.

```mermaid
flowchart LR
    subgraph Machine["The Windows machine"]
        UI["ByteBridge.exe<br/>WPF control panel"]
        SVC["ByteBridge.Service.exe<br/>Windows service"]
        DB[("bytebridge.db<br/>SQLite, WAL")]
        LOG["logs\\gateway-&lt;date&gt;.jsonl"]
        FB[("Firebird")]
        CF["cloudflared"]
    end
    UI -- "writes settings" --> DB
    SVC -- "polls every 5 s" --> DB
    UI -. "GET /health, /stats<br/>(loopback)" .-> SVC
    CF -- "http://127.0.0.1:8080" --> SVC
    SVC -- "FirebirdClient" --> FB
    SVC -- "appends" --> LOG
```

## The projects

| Project | Path | Target | What it is |
| --- | --- | --- | --- |
| `ByteBridge` | repo root (`*.xaml`, `*.cs`) | `net10.0-windows`, WPF + WPF-UI | The control panel. Runs elevated (`app.manifest`), one copy per Windows session, lives in the tray. |
| `ByteBridge.Core` | `core/` | `net10.0` | The gateway, the Firebird executor, storage, the request log, localisation, and the admin CLI. No Windows APIs, so the tests run on Linux. |
| `ByteBridge.Service` | `service/` | `net10.0-windows` | The service host. With no arguments it is the service; with arguments it is the admin CLI (`Cli.Run`) for Server Core. |
| `ByteBridge.Tests` | `tests/ByteBridge.Tests/` | `net10.0` | xUnit. Drives a real listener on a spare port against a temporary data folder. |

Two things at the root are **not** part of the product:

- `src/`, `server.ts`, `index.html`, `package.json` — a browser mock-up
  of the control panel (React + Express, with in-memory fake data in
  `src/server/gatewayEngine.ts`). It was the design prototype. Nothing
  in the build, CI or the installers uses it.
- `installer/` — the Inno Setup (`.exe`) and WiX (`.msi`) definitions,
  built only by the release workflow.

The root `ByteBridge.csproj` globs every `.cs` under the repo, so it
excludes `core/`, `service/` and `tests/` by hand. **A new project
directory needs adding to that exclude list**, or its sources compile
into the app twice.

## Where things live in `core/`

| Folder | Holds |
| --- | --- |
| `Gateway/GatewayServer.cs` | The listener: routing, auth, CORS, body limits, JSON shapes, the Cloudflare Access login routes. |
| `Gateway/FirebirdExecutor.cs` | Opens a connection, binds `parameters`, runs the statement, maps Firebird types to JSON. Also the read-only guard for `/query`. `internal` — callers go through `GatewayServer`. |
| `Gateway/AuthFailureLimiter.cs` | The wrong-key lockout, keyed by `CF-Connecting-IP`. |
| `Gateway/CloudflareAccessValidator.cs`, `OAuthSessionManager.cs` | Optional Cloudflare Access (JWT) sign-in and the session cookie it produces. |
| `Gateway/ConnectionHealthMonitor.cs` | Probes every enabled connection once a minute so "online" reflects now. |
| `Gateway/RequestLog.cs` | One JSON line per request, written after the response; parameter values never logged. |
| `data/SqliteDatabase.cs` | The only thing that touches `bytebridge.db`: connections, gateway settings, OAuth settings, sessions. |
| `data/DataFolderSecurity.cs` | Restricts the data folder to SYSTEM and Administrators, on every open. |
| `Configuration/` | Plain records: `DatabaseConfig`, `GatewayConfig`, `OAuthConfig`. |
| `Localization/Strings.cs` | Every UI string, English and Arabic. |
| `Admin/Cli.cs` | The `ByteBridge.Service.exe <command>` admin tool. |

## The life of a request

Take `POST /query` arriving through the tunnel:

1. `cloudflared` forwards it to `http://127.0.0.1:<port>/`. The
   listener never binds anything but loopback; the service reserves
   that prefix with HTTP.SYS itself (`service/UrlReservation.cs`) the
   first time a bind is refused.
2. `GatewayServer` checks the size cap (1 MB) and CORS, and answers
   `/health` without a key.
3. The caller's address (`CF-Connecting-IP`, never
   `X-Forwarded-For`) is checked against `AuthFailureLimiter`; a locked
   out caller gets `429` before its key is even read.
4. The key (`X-API-Key` or `Authorization: Bearer`) is compared in
   constant time. With Cloudflare Access enabled, a valid Access JWT or
   session cookie is accepted instead.
5. The connection is looked up **from SQLite, on this request** — so
   toggling Online/Offline in the panel applies immediately. Unknown
   name → `404`; Offline → `409`.
6. `FirebirdExecutor` refuses anything but `SELECT`/`WITH` on `/query`,
   binds `parameters` as Firebird parameters, runs it, and maps rows to
   JSON.
7. The response is written, and only then is a line appended to the
   request log.

## How the panel and the service stay in step

There is deliberately no IPC. The panel writes to SQLite (WAL mode, so
both processes can hold it open) and `service/GatewayWorker.cs`
re-reads the gateway settings every five seconds. A changed port or
on/off switch rebinds the listener; a changed API key, lockout or
Access setting is handed to the running listener without a rebind.
Connections are different: the gateway reads them on every request, so
they need no poll at all. The consequence to keep in mind: **a setting takes effect when
the service next polls, not when the button is clicked**, which is why
the panel refreshes its status on a two-second timer instead of
trusting its own clicks.

What the panel *does* ask the service, it asks over HTTP on loopback,
the same way a tunnel would: `/health` to tell "the service is
running" apart from "the gateway is answering"
(`GatewayServiceControl.cs`), and `/stats` for the request counts on
each card. Starting and stopping the service goes through the Service
Control Manager (`GatewaySwitch.cs`).

## The control panel

| File | Screen |
| --- | --- |
| `App.xaml.cs` | Start-up: single-instance mutex per session, language, the tray icon. |
| `MainWindow.xaml(.cs)` | Menu bar, status line, one card per connection. Cards are built in code (`CreateConnectionCard`). |
| `AddDatabaseWizardWindow` | The four-step add/edit wizard, with Test Connection. |
| `WebServerWindow` | Gateway on/off, port, API key copy/rotate, lockout settings. |
| `CloudflareTunnelWindow` | Cloudflare Access (OAuth) settings. |
| `SettingsWindow` | Options: language, start with Windows, ask before closing. |
| `AboutWindow`, `CloseDialogWindow` | About (maker, version, log folder) and the close prompt. |

Windows set `FlowDirection` from `Strings.CurrentLanguage`, so Arabic
mirrors the whole layout; nothing else is needed for RTL.

## Common changes, and where they go

**A new UI string.** Add the key to both the `"en"` and `"ar"`
dictionaries in `core/Localization/Strings.cs`, then set it from the
window's `ApplyLocalization` (or equivalent) with `Strings.Get`. A
missing Arabic entry falls back to English rather than failing, so
check both are there.

**A new setting.** Add the field to the right record in
`core/Configuration/`, read and write it in `SqliteDatabase`
(`GetGatewayConfig` / `SaveGatewayConfig` use the key–value `settings`
table, so no schema change is needed), expose it in the window and in
`core/Admin/Cli.cs` so Server Core can reach it too, and — if the
running listener has to react — compare it in `GatewayWorker`.

**A new endpoint.** Route it in `GatewayServer`, decide explicitly
whether it needs the key (everything except `/health` does), record it
in the request log, add tests in `GatewayServerTests`, and document it
in `docs/reference/api-reference.md` and `GATEWAY.md`. A new endpoint
is a MINOR version bump.

**Another database engine.** Everything engine-specific is in
`FirebirdExecutor` and `FirebirdConnectionTester`; `DatabaseConfig`
has no engine field yet. That is the seam a second engine would go
through.

**A change a user can see.** Update the user guide under `docs/guide/`
and `docs/ar/`, and, if a window changed, the drawings:
`python scripts/docs-screens.py` regenerates
`docs/assets/screens/{en,ar}/` from the labels in that script.

**Anything worth telling users.** A line under **Unreleased** in
`CHANGELOG.md`, as you make it. See
[Versioning and releasing](../contributing/versioning-and-releasing.md).

## Conventions

- Comments explain **why**, in full sentences, as block comments above
  the thing they describe. Read a few files before adding one.
- Security decisions are written down where they are made (the data
  folder ACL, the lockout keying, the loopback-only bind). If you change
  one, change its comment.
- Tests use `new SqliteDatabase(tempFolder)` and a spare port; never
  the machine's real data folder.
