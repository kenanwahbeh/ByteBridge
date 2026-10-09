# Changelog

Everything worth knowing about each release of ByteBridge.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the version numbers follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
as described under [Versioning](README.md#versioning).

Work that is finished but not yet released sits under **Unreleased**.
`scripts/new-release.ps1` promotes it to a version heading, and the
release workflow copies that section into the GitHub Release, so this
file is the single source of truth for what shipped.

## [Unreleased]

## [3.2.0] - 2026-10-09

### Added

- **Linux packages and an apt repository.** Each release now carries
  `bytebridge_<version>_amd64.deb`, a `linux-x64` tarball with
  `install.sh`, and `SHA256SUMS-linux.txt`, all with build attestations.
  On Debian and Ubuntu, add the signed repository at
  <https://kenanwahbeh.github.io/ByteBridge/> once and
  `apt install bytebridge` / `apt upgrade` do the rest. The repository
  only signs packages whose attestation checks out. See the
  [Linux guide](docs/getting-started/linux.md).

- **`enroll`, `claim` and `unenroll` work on Linux.** The connector is
  the `cloudflared` systemd unit instead of a Windows service, found
  with `systemctl`. The tunnel token goes in a root-only file
  (`/etc/cloudflared/token`) that the unit reads with `--token-file`,
  never on a command line. The `bytebridge` command runs these three
  as root and gives the data folder back to the service account
  afterwards.

### Fixed

- **`install.sh` now says what is missing.** Without the ICU library the
  service crashed at start with a message that did not say how to fix
  it; the installer checks first and names the package.
- **The service started under systemd only on small machines.** The host
  watched its working directory for configuration changes, and systemd
  starts a service in `/`, so start-up walked the whole disk and could
  outlast systemd's timeout. It now watches its own install folder.

## [3.1.0] - 2026-10-09

### Added

- **[ROADMAP.md](https://github.com/kenanwahbeh/ByteBridge/blob/main/ROADMAP.md)
  and a decision record per significant choice.** Why `HttpListener`
  rather than Kestrel, why the secrets are encrypted and where the key
  lives, and why SQL Server was written and then taken back out, are
  answers no one can get by reading the code — and the first thing lost
  when a file is rewritten. The records say what was rejected and why,
  which is the part that saves the next contributor the argument.

- **PostgreSQL next to Firebird.** Each connection now has an engine,
  chosen in the add/edit wizard or with `db add --type`. Both are tested
  against a real server, and both hold `/query` read-only themselves, so
  a statement that got past the text check is refused by the database.
  A parameter that arrives as text needs a cast to compare against a
  non-text column, which
  [database-engines.md](docs/reference/database-engines.md) explains.
  The driver adds about 1 MB to the self-contained publish, measured.
- **Release files carry a signed statement of where they were built.**
  Every installer and `SHA256SUMS.txt` now gets a build-provenance
  attestation from the release workflow, verifiable with
  `gh attestation verify <file> --repo kenanwahbeh/ByteBridge`. A file
  that was tampered with or re-uploaded anywhere else fails that check.
- **Connect a machine to ByteBalance without touching Cloudflare.** The new
  `enroll` command (and a **Connect to ByteBalance** dialog in the control
  panel, and an optional page in the installer) asks ByteBalance for a tunnel,
  waits for it to be approved, and installs the `cloudflared` connector.
  Only the owner's email is let through to the tunnel. `claim` continues an
  interrupted enrolment, `enrollment` shows its state, `sync-key` and `unenroll`
  cover the rest. Silent installs take `/ByteBalanceEmail=owner@example.com`.
  See [Connecting to ByteBalance](docs/reference/bytebalance.md).
- **The API key follows the gateway to ByteBalance.** After connecting, the key is
  sent to ByteBalance (encrypted there) so the storefront can call this gateway,
  and `key new` sends the replacement automatically.
- **The gateway can require the Cloudflare Access token itself.** `oauth edge on`
  makes a request that arrives through Cloudflare carry a valid Access token in
  addition to the API key; enrolment turns it on. Local requests are not asked
  for one, and it does not switch the Access login on.
- **Browse for the database file, and an eye on the password.** The add
  wizard's second page has a **Browse** button for a Firebird database on
  this computer (`.fdb`, `.gdb` and Sahlisoft's `.tcbfile`), and its third
  page an eye that shows the password as it is typed. A saved password is
  never shown: editing a connection leaves the eye off until the box has
  been emptied and a new password typed.
- **The service runs on Linux under systemd.** A Linux publish carries
  `bytebridge.unit` and `install.sh`; data lives in `/var/lib/bytebridge`
  (or `BYTEBRIDGE_DATA`) with owner-only permissions, and logs go to the
  journal.

### Changed

- `status` shows whether the machine is connected to ByteBalance.
- An existing Cloudflare connector service is never replaced by enrolment unless
  `--replace-connector` is passed.
- The control panel project moved from the repository root to `app/`. Its SDK
  glob now only ever sees its own sources, so the hand-maintained exclusion
  list for `core/`, `service/` and `tests/` is gone, and a new project folder
  can no longer be compiled into the app by accident. Nothing about the
  installed app changes.

### Fixed

- **The `-framework` installer no longer insists on downloading .NET
  when you already have it.** It looked for the runtime in one fixed
  folder only, so a runtime installed elsewhere was reported missing.
  It now also checks the location the .NET installer recorded and
  `DOTNET_ROOT`, and if it still finds nothing you can choose to
  install without downloading instead of being forced to.

### Removed

- **The browser mock-up of the control panel.** The React + Express
  prototype in `src/`, `server.ts` and `index.html`, with its npm and
  Vite setup, was never part of the product and nothing built or
  shipped it. The gateway, the Windows service and the control panel
  are unchanged.

### Security

- **The secrets in `bytebridge.db` are encrypted at rest.** Firebird
  passwords, the API key and the enrolment claim secret used to sit in
  the settings file in plain text, so a stolen copy of the data folder
  handed them over. They are now wrapped with a key that never leaves
  the machine and is kept outside the data folder: DPAPI on Windows;
  on Linux an owner-only key file in `/var/lib/bytebridge-keys`
  (override with `BYTEBRIDGE_KEY_DIR`), so a backup of the data folder
  alone opens nothing. Existing files are migrated on first open, and
  a file copied to another machine no longer yields its secrets —
  passwords read empty until re-entered, and the API key is re-minted.
  A full-machine image still contains both halves and stays
  decryptable, and on the running machine an administrator can still
  unwrap the secrets, which is why the data folder stays restricted to
  Administrators and the service account.

## [3.0.0] - 2026-10-01

### Changed

- **ByteBridge only reads, unless you turn writing on.** `/execute` is
  off and answers `403`, so a script that wrote through the gateway
  stops working until an administrator ticks **Options → Allow
  writing**, which asks for confirmation and takes effect on the
  next request. `/query` now runs in a
  transaction that Firebird itself holds read-only, so a `SELECT` that
  calls a procedure which writes is refused by the database rather than
  trusted not to. Statements that move a generator (`GEN_ID` with a
  step other than 0, `NEXT VALUE FOR`) are refused as well, because a
  generator changes outside any transaction. The guide no longer says the API key lets its holder
  change data.

## [2.0.0] - 2026-09-30

### Added

- **Help → User Guide**, also on **F1**, opens a step-by-step guide
  with pictures of every screen, written for people who have never
  programmed. It opens in Arabic when the window is in Arabic. The
  guide also explains the idea behind ByteBridge in everyday words,
  every button and option, and what each status message and error
  number means.
- **Help → Visit ByteBalanceTech.com** opens the maker's website, the
  same one *About* links to. The guide and the documentation site
  carry the ByteBalanceTech name and logo too.
- **A guide for contributors,** *How the code fits together*, mapping
  the projects, the processes, how the control panel and the service
  share one settings file, and where each kind of change belongs.

### Security

- **Wrong API keys now cost the caller.** The 10th wrong key from
  one caller inside a minute is still answered `401`, but blocks that
  caller: its next request gets a `429` with a `Retry-After` for the
  next minute, before its key is even looked at. This applies wherever
  a key is tested, `/auth/me` included, which used to be answered ahead
  of the limit. A request that sends no key at all does not count, and
  a correct key clears the tally. The limit is yours to set: **Web
  Server** in the app has *Lock out after wrong keys* and *Lock out for
  (minutes)* (0 turns it off), and `ByteBridge.Service.exe lockout show
  | on | off | set --attempts <n> --window <seconds> --block <seconds>`
  does the same from a terminal. Callers are told apart by the address
  Cloudflare reports; `X-Forwarded-For` is ignored because its first
  entry is written by the caller.
- **`/stats` needs the API key.** It listed every connection to anyone
  who could reach the hostname. The app sends its key when it
  polls, so nothing changes there; a script that read `/stats` without
  a key now gets `401`, and a signed-in Cloudflare Access session on
  its own gets `403`. Only `/health` is still open.
- **Request counts are kept per connection.** The name a caller typed
  used to be the counting key, so a client could grow the table without
  bound with names that match nothing, and a connection reached once by
  name and once by id was counted under two keys. They are now keyed by
  the connection's id, which is also what `/stats` lists, so two
  connections that share a name from an older settings file are counted
  apart.
- **Cloudflare Access sessions are checked for cross-site requests.** A
  `POST` that arrives on the session cookie alone has to be
  `application/json` and, if the browser names an `Origin`, that origin
  has to be the gateway's own host or the public hostname set for
  sign-in. Requests on the API key are unchanged.
- **Only RS256 tokens are accepted** from Cloudflare Access, and the
  signed-in identity is the token's email address rather than its
  opaque `sub` id.
- **An unexpected error no longer echoes its message to the caller.**
  A `500` says to look in the request log, where the detail is. Errors
  Firebird reports for a statement still come back in full.

### Changed

- **The empty window points at the right menu.** With no databases
  added, the window used to say to click *+ Add Data*, a button that is
  no longer there. It now says *File → New Database…*.

- **A new app icon.** The ByteBalanceTech logo, taken from its website,
  replaces the generic database icon everywhere the app shows one: the
  program file, the window and its taskbar button, the notification
  area, and the installers.
- **The notification-area icon is there from the moment the app starts,**
  not only after *Minimize to Tray*, and stays while the window is open.
  Left-clicking it brings the window forward.
- **About names its maker.** *About* now shows the ByteBalanceTech logo
  and a link to ByteBalanceTech.com beside the version.
- **The window-close dialog is two choices.** *Minimize to Tray* or
  *Exit*, with a *Don't ask again* box; the × or Esc cancels. *Ask
  before closing* in Settings turns the question back on.

## [1.2.0] - 2026-09-21

### Added

- **Cloudflare Access sign-in now works in the background service.**
  The service used to start without any Access support, so a team
  domain and audience saved in Settings did nothing there. It now
  applies them at startup and whenever they change, with no restart.
  The API key keeps working whether Access login is on or off.
- **`oauth` commands for machines with no desktop.** `oauth set
  --team-domain <team>.cloudflareaccess.com --audience <AUD tag>
  --public-hostname <host>` stores the settings, `oauth on` and
  `oauth off` switch login, and `oauth show` prints what is
  configured.

### Fixed

- **`/databases` no longer reports a working database as `"online":
  false`.** The flag was only written when someone turned a connection
  on from the window, so a connection enabled any other way, or one
  whose server came back after a failed test, stayed offline in the
  API while queries against it worked. The service now checks every
  enabled connection once a minute and keeps the flag current.
- **A signing key Cloudflare had revoked stayed trusted until the
  service restarted.** The key list is now replaced on every refresh
  instead of only added to, and a cached key is re-checked once its
  time is up. If a refresh fails or comes back empty, the last good
  keys stay in use.
- **Cloudflare Access sign-in rejected every token.** Keys were read
  as certificates, but Cloudflare publishes plain RSA keys, so none
  could be parsed and the failure was swallowed silently. A malformed
  entry no longer costs the valid keys beside it, and a failed refresh
  is now reported.
- **Access settings with only a team domain and an audience could
  never verify a token.** The key address was meant to default to the
  team domain's, but the default never applied.

## [1.1.0] - 2026-09-15

### Added

- **A real menu bar replaces the always-open Gateway and Cloudflare
  cards.** File, Edit, Web Server, Cloudflare Tunnel, Options and Help
  now tuck configuration behind dialogs instead of leaving it open at
  the top of the window competing with the connections list, which is
  now the whole page.
- **Adding a database is a four-step wizard** (name, server details,
  credentials, test & finish) instead of one long scrolling form.
- **Each connection card shows how many requests it has answered**
  since the gateway last started, so it's visible at a glance whether
  a database is actually being used.
- **The app now looks more like Windows 11.** Fluent-styled buttons,
  menus and checkboxes throughout, via the WPF-UI library, on top of
  the native window chrome.

### Fixed

- **Arabic no longer renders numbers as Eastern Arabic-Indic digits**
  (٠١٢٣). Ports, request counts and everything else numeric now show
  in the digits people actually type, in either language.
- **Settings never switched to right-to-left layout in Arabic,**
  unlike the rest of the app, and one of its labels was never
  translated at all.
- **Every dialog can now be dismissed with Escape,** not just its
  Cancel or Close button.

## [1.0.1] - 2026-09-13

### Fixed

- **Minimizing no longer just makes the window disappear.** "Minimize
  to Tray" set the window to minimized and hid it from the taskbar,
  but nothing in the app ever put up a tray icon, so there was
  nothing left to click to bring it back -- relaunching from the
  Start menu was the only way out. A real system tray icon now
  appears near the clock; clicking or double-clicking it restores the
  window, and its menu offers Open and Exit.
- **The close dialog no longer breaks under Arabic.** Three
  fixed-width buttons crammed into one 400px-wide row barely survived
  in English and overflowed once the labels were the longer Arabic
  ones. It is now a single column of full-width buttons that sizes
  itself to its content, and it flows right-to-left for Arabic
  instead of forcing RTL text through an LTR layout.
- **Cloudflare Access login could never complete.** With no public
  hostname configured, the gateway redirected visitors back to
  `http://127.0.0.1:<port>/auth/callback` after they signed in -- an
  address that only means anything on the machine running the
  gateway, so nobody arriving through the tunnel could ever land
  there. Settings now has a required Public Hostname field, and the
  gateway refuses to start the login flow with a clear error instead
  of redirecting somewhere unreachable.

## [1.0.0] - 2026-09-13

### Security

- **The settings folder is locked down by whatever creates it.** The
  folder holding the API key and the Firebird passwords was restricted
  to SYSTEM and Administrators only when the service started. The
  control panel and the command line can each be the first to create
  it, and both write a key into it straight away, so until the service
  next ran -- or for good, where it never did -- that file kept
  `C:\ProgramData`'s permissions and was readable by every account on
  the machine.
- **The listener can no longer be pointed off loopback.** A stored
  `Gateway.Host` that was not a loopback address was bound as it was,
  and the service reserves whatever address it is refused, so a value
  such as `+` or `0.0.0.0` put the gateway on every network interface.
  Anything other than a `127.x.x.x` address or `localhost` is now
  ignored in favour of `127.0.0.1`, the service only ever reserves a
  loopback address, and `netsh` receives its arguments separately so
  nothing inside one can become another.

### Fixed

- **A request can no longer reach the wrong database by name.** Two
  connections could share a name, and a request naming it went to
  whichever sorted first -- a write included. Names now have to be
  unique. A name two connections already share, in a settings file from
  before, is answered with `400` and a pointer to their ids rather than
  a guess, and the command line refuses it the same way.
- **The gateway keeps accepting requests after a failed accept.** An
  error while waiting for the next request ended the accept loop for
  good while the service still believed the gateway was up, so every
  later request hung. Only a real stop ends it now.
- **Saving a setting no longer undoes a key rotation.** Changing the
  port or the on/off switch wrote back the API key read a moment
  earlier, so a key rotated in between was quietly restored. The
  settings are now written in one transaction as well.
- **An upload cut off part-way is a `400`,** not a `500` recorded
  against the gateway.
- **`GET /health` and `GET /databases` sent with a body** no longer
  leave it in the keep-alive connection to corrupt the next request.
- **Saving an edit to a connection that was removed meanwhile** says it
  is gone, instead of looking saved and changing nothing.
- **The last-tested time survives a culture with another calendar.** It
  was read back in that calendar, which shifted the year or lost the
  time.
- **`db add --help` explains how to add a database,** as `status`
  suggests, rather than failing over a missing `--name`. `db add` also
  refuses a Firebird port outside 1 to 65535.
- **The documentation matches the code.** A new API key takes effect
  within a few seconds rather than immediately, the status-code table
  lists `413`, and the test table lists every test class.

## [1.1.0] - 2026-09-04

### Added

- **The gateway runs as a Windows service.** It starts with the machine
  and serves with nobody signed in, which is what makes an unattended
  Windows Server a supported target. Closing the control panel no
  longer stops it: the window used to *be* the server, so tidying the
  desktop took the gateway down and a tunnel started returning `502`
  with nothing obviously wrong.

  The window is otherwise the one that was there. Databases, the API
  key, the port and the on/off switch are all in the same place; the
  switch now records what the gateway should be doing and the service
  acts on it within a few seconds.

  It also reports two things separately that are easy to confuse:
  whether Windows is running the service, and whether the gateway is
  actually answering, checked by calling `/health` over loopback. A
  running service whose gateway could not bind is exactly the state
  behind a `502`, so it is named rather than shown as healthy.

- **Configuration from a terminal**, for Windows Server Core, which has
  no desktop and so cannot run the control panel at all. The service
  executable doubles as an admin tool: `status`, `on`, `off`, `port`,
  `key show`, `key new`, and `db list/add/enable/disable/remove`. On a
  machine with a desktop you never need it.

- **The settings folder is locked to Administrators and Local System.**
  It holds the Firebird passwords in the clear beside the API key, and
  that key is all that stands between the public internet and those
  databases; inherited from `ProgramData` it would have been readable
  by every account on the machine. The control panel therefore asks for
  administrator rights, which starting and stopping the service needs
  anyway.

- The service reserves its own HTTP prefix when Windows refuses one, so
  changing the port does not leave the gateway unable to bind, and
  restarts itself on failure rather than staying down.

- **The `.exe` installers now fetch what they need.** The `-framework`
  build no longer stops with a link when the .NET 10 Desktop Runtime is
  missing: it asks first, then downloads it from Microsoft and installs
  it silently, and refuses to continue only if that genuinely fails.
  Both `.exe` builds also offer to install `cloudflared`, ticked by
  default and hidden entirely when the machine already has it -- checked
  in both `Program Files` locations and on `PATH`, so winget, scoop and
  a manual copy all count.

  The two are treated differently on purpose. Without .NET the
  `-framework` build cannot start, so a failed download aborts rather
  than leaving a shortcut to an app that dies on launch. `cloudflared`
  is only needed to reach the app from outside, so a failure there warns
  and carries on.

  Installing `cloudflared` gets you the connector, not a working tunnel:
  connecting one still needs your own token, which is the point.

  The `.msi` installers do neither, unchanged and deliberately: an MSI
  cannot download, and deployment tools manage prerequisites themselves.

  The release build now HEADs both URLs before packaging, so a vendor
  URL that moves fails the release instead of failing on a customer's
  machine.

- **A request log.** Every request the gateway answers is appended to
  `C:\ProgramData\ByteBridge\logs\gateway-<date>.jsonl`, one JSON
  object per line: time, method, path, status, duration, the calling
  address, whether the key was accepted, and for a query the statement,
  the connection and the row count. Rejected requests are recorded too,
  which is what an attempt on the API key looks like from the outside.
  Bound parameter values are never written -- they are the customer's
  data, and keeping them out of the statement is the point of binding
  them. Files are kept 30 days. Writing happens after the response is
  sent, so a slow disk never delays an answer.
- Documentation for putting **Cloudflare Access** in front of the
  tunnel hostname, so callers are authenticated at Cloudflare's edge
  before a request reaches the machine. See
  [GATEWAY.md](GATEWAY.md#locking-the-tunnel-to-just-you).

### Fixed

- **The `.msi` installers carried no application.** `Package.wxs`
  harvested the published folder with a path relative to the directory
  `wix` was launched from, but WiX resolves `Files/@Include` relative to
  the `.wxs` file itself, so the glob pointed at `installer\publish`,
  which does not exist. WiX only *warns* when a harvest matches nothing
  and still writes a valid installer, so the build passed and 1.0.0
  shipped two 48 KB `.msi` files containing a Start Menu shortcut and
  nothing else -- pointing at an executable that was never copied. Both
  `.exe` installers were unaffected and install correctly. The build now
  fails outright on an empty harvest, and additionally checks every
  installer against the size of the folder it is supposed to package.
- The `.wixpdb` files are no longer attached to releases. They are
  build-time symbol files for WiX itself and were published by accident.

## [1.0.0] - 2026-09-03

### Added

- **HTTP gateway** over the configured Firebird connections, bound to
  `127.0.0.1` only: `GET /health`, `GET /databases`, `POST /query` and
  `POST /execute`, all JSON. `/query` accepts `SELECT` and `WITH` only,
  so a read path cannot write by accident.
- **API key authentication** on every endpoint except `/health`. The
  key is 32 random bytes generated on first run, accepted in
  `X-API-Key` or as `Authorization: Bearer`, and compared in constant
  time. Rotating it takes effect immediately, without restarting the
  gateway.
- **Parameter binding.** Values passed in `parameters` are bound as
  Firebird parameters, so a value cannot become SQL. Blobs come back
  base64, text as text, and non-ASCII is emitted raw rather than
  `\uXXXX` escaped.
- **Gateway API panel** in the main window: whether the listener is
  running and on which URL, the port, start and stop, and copy or
  rotate the key. Windows URL-reservation and port-in-use failures are
  reported with the command that fixes them.
- **Limits** suited to something a tunnel exposes publicly: a row cap
  per query, a command timeout, and a 1 MB request body cap.
- **Installers**, all per-machine into `Program Files`: an `.msi` and
  an `.exe` that bundle .NET, and `-framework` builds of each that
  expect the .NET 10 Desktop Runtime. The `-framework` `.exe` refuses
  to install when the runtime is missing and offers the download link.
- **Release workflow** that builds all four installers on Windows,
  writes `SHA256SUMS.txt`, and attaches them to a GitHub Release when a
  `v*` tag is pushed.
- **Documentation**: [README.md](README.md) for what it does and how to
  get going, [GATEWAY.md](GATEWAY.md) for the API and the tunnel
  configuration.

### Fixed

- A request refused before its body was read -- a wrong API key, an
  unknown path, the wrong HTTP method -- left the unread bytes in the
  connection, so the *next* request on that keep-alive connection was
  parsed from the middle of the previous one and came back as a
  spurious `400`. Since `cloudflared` holds keep-alive connections to
  the origin, this surfaced as an unrelated request failing for no
  visible reason. Small bodies are now drained before the reply and the
  connection is dropped rather than reused when there is too much left.

### Notes

- Settings live in `C:\ProgramData\ByteBridge\bytebridge.db`.

[Unreleased]: https://github.com/kenanwahbeh/ByteBridge/compare/v3.2.0...HEAD
[3.2.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v3.1.0...v3.2.0
[3.1.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v3.0.0...v3.1.0
[3.0.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v2.0.0...v3.0.0
[2.0.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v1.2.0...v2.0.0
[1.2.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/kenanwahbeh/ByteBridge/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/kenanwahbeh/ByteBridge/compare/v1.1.0...v1.0.0
