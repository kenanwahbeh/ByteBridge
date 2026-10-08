# Roadmap

What ByteBridge is for is decided in
[ADR 0001](docs/decisions/0001-standalone-product.md): **a standalone
product, with ByteBalance as an optional integration.** This page is
the working list that follows from it. Where an item has a reason
behind it, that reason is in a decision record, not here.

Ordered by what it unblocks, not by what is easiest.

## Now — closing what the last release opened

PostgreSQL support, secrets-at-rest and release attestation merged as
one pull request. What they left behind:

- [x] **Cover the wizard's repair and localisation decisions with
  tests** — #35. The last decisions in the project no test can reach:
  both were logic inversions in WPF code, and both shipped.
- [x] **Tighten the dollar-quote tag to PostgreSQL's own rule** — #36.
  The scanner accepts a tag starting with a digit, which PostgreSQL
  does not. Not a bypass; a guard that reads more than the engine does.
- [ ] **Verify the control panel by hand on Windows** — #37. WPF builds
  nowhere but Windows, so this is the only place it is observable.
  The covered code is measured; this is what is left. Script: scripts/verify-panel.ps1.
- [ ] **Cut the release** — #38. `3.1.0`: adding an engine is MINOR,
  and [ADR 0002](docs/decisions/0002-meaningful-versioning.md) records why not `4.0.0`.

## Next — standalone means Linux

A standalone open-source tool has users who are not on Windows, and
the gateway has been portable since before it was called that. What is
missing is that **CI never exercises it**.

- [x] **Exercise the Linux publish in CI.** Build
  `service/ByteBridge.Service.csproj` for `linux-x64`, check the
  bundle carries `bytebridge.unit` and `install.sh`, and start it once.
  The promise is in the changelog and in the systemd files; nothing
  tests it. *Highest-value item here: it converts a documented promise
  into a measured one.*
- [ ] **Document Linux as a supported target.** What it means, how to
  install it, and what differs from Windows — the data directory, the
  key file beside it, and the absence of the control panel.

## Then — making it pleasant to contribute

- [ ] **Arabic command line.** `--help`, `db add`, and the error
  messages. The documentation is fully bilingual; the tool a contributor
  types into is not. Small, and the first thing an Arabic-speaking
  contributor meets.
- [ ] **Seed issues.** Five to eight, small, labelled
  `good first issue`. There is one contributor so far; a labelled
  project with nothing behind the labels is a project that looks open
  and is not.
- [ ] **`CODE_OF_CONDUCT.md`.** Missing from a project asking for
  contributions.

## Later — operations, once someone asks

Not started, and deliberately so: there are no announced users, and
building for a hypothetical operator is how a tool grows the wrong
shape. Recorded here so the reasoning is not lost.

- [ ] **Export and import the settings file.** Moving to a new machine
  today means re-typing every password, because the key that opens the
  file does not travel.
- [ ] **A backup and restore note that names the key.** The encryption
  makes this a real trap: a data-folder backup is ciphertext, and an
  operator who does not know why will assume it is broken.
- [ ] **`/metrics`.** Requests per connection, refusals, error rates —
  for anyone running this in front of a real workload rather than their
  own database.

## Being decided

- [ ] **Which engine next, if any.**
  [ADR 0005](docs/decisions/0005-sql-server-removed.md) sets the bar:
  it must hold `/query` read-only itself, and it must have run against
  a real server. SQLite is not a candidate — it is not a server, and
  the gateway's job is to reach one.
- [ ] **Per-connection read-only database users.** The gateway cannot
  verify what the Firebird user of a connection may do, and the API key
  grants exactly that. Worth revisiting when the first real multi-user
  deployment asks.

## Not planned

- **Exposing the database port.** The tunnel is the point.
- **A hosted version.** See
  [ADR 0001](docs/decisions/0001-standalone-product.md).
- **An engine without engine-enforced read-only.** See
  [ADR 0005](docs/decisions/0005-sql-server-removed.md).

## How this document changes

A significant choice gets a decision record in `docs/decisions/`, and
this page follows from it. See
[CONTRIBUTING.md](CONTRIBUTING.md) for what makes something significant
enough to write one.