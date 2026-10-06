# Contributing to ByteBridge

Thank you for taking the time to contribute. ByteBridge is maintained in
the open, and a contribution of any size helps.

Please read this guide first — most of it is short, and it will save a
round of review comments.

## Setting up

ByteBridge is .NET 10 and C#. Two parts run on your machine: a Windows
service that serves the gateway, and a WPF control panel that configures
it.

**You need:**

- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- **Windows** to build the control panel. It is WPF, and WPF does not
  build elsewhere.
- A database server to run the live tests against: Firebird 4 or
  PostgreSQL. Optional — the suite is green without one.

**The rest of the solution builds anywhere**, including Linux, which is
how CI runs the tests. If you are working on the gateway, the request
log or the storage layer, you never need Windows.

```powershell
git clone https://github.com/kenanwahbeh/ByteBridge
cd ByteBridge

dotnet build ByteBridge.slnx
dotnet test tests/ByteBridge.Tests

dotnet run --project app/ByteBridge.csproj   # the control panel
```

For how the projects fit together and where a change belongs, read
[architecture.md](docs/developers/architecture.md). It is the map, and
it is worth ten minutes before you touch anything.

## Tests

Every change that changes behaviour needs a test that fails without it.
Run the suite before you push:

```powershell
dotnet test tests/ByteBridge.Tests
```

The tests drive a real listener on a spare port with its settings in a
temporary folder, so they never read or overwrite the connections of
whoever is running them. They target plain `net10.0`, so the same
command works on Linux.

Two kinds of test are skipped unless you point them at a server, which
is deliberate: a clone with no database still gets a green run.

```powershell
$env:BYTEBRIDGE_TEST_FIREBIRD = "127.0.0.1:3050:SYSDBA:masterkey:C:\db\test.fdb"
$env:BYTEBRIDGE_TEST_POSTGRES = "127.0.0.1:5432:postgres:password:shop"

dotnet test tests/ByteBridge.Tests --filter Category=Firebird
dotnet test tests/ByteBridge.Tests --filter Category=PostgreSql
```

**Point them at a scratch database, never a real one.** They create their
own tables and work only on rows they own.

Follow the style of the file you are adding to: `GatewayServerTests` for
routing, `RequestLogTests` for the log, `DatabaseTypeTests` for the
engines. A test that proves a security property has to fail if the
property is removed — check that by removing it.

## Style

The repo's conventions, in short:

- **Comments explain why**, in full sentences, in a block above the thing
  they describe. What the code does is not worth a comment; why it does
  it that way, and what breaks otherwise, is. Every security decision is
  written down where it is made — if you change one, change its comment.
- **Names say what they mean.** `IsOnline`, not `IsGood`; a comment that
  has to explain a name is a name that needs changing.
- **Be honest at the edges.** If something cannot be guaranteed, say so
  where a reader will find it. The documentation claims are checked
  against what the code actually does.
- No new dependency without a number: what it costs the publish, and what
  it replaces.

## Making a change

1. Branch from `main`.
2. Make the change, with its tests.
3. Add a line to `CHANGELOG.md` under **Unreleased** describing what a
   user would notice. It is the single source of truth for what shipped.
4. Open a pull request.

## Decisions, and when one needs writing

Some choices are not visible in the code. Why `HttpListener` and not
Kestrel, why secrets are encrypted and where the key lives, why an
engine was taken back out — those are answers to a question nobody can
answer by reading the implementation, and they are the first thing lost
when a file is rewritten.

If your change settles something a future contributor would reasonably
have asked about, write it down in `docs/decisions/` before the code.
One file, numbered, following `0001-standalone-product.md`:

- **Context** — the situation, and the question it raised
- **Decision** — what was chosen, in the present tense
- **Consequences** — what this makes important, what it costs, and what
  it deliberately does not do
- **Alternatives considered** — and why each was rejected. This is the
  part that saves the next person the argument.

Write the reasoning, not the summary. The *what* is in the code; the
*why* is the only part that cannot be re-derived from it.

[ROADMAP.md](ROADMAP.md) is what follows from those decisions.

Version numbers are [semantic](https://semver.org/), and the rules for
what counts as a MAJOR bump are in the README. A pull request that
removes or renames a response field is a MAJOR change, not a patch.

If your change is visible to a user, the illustrated guide under
`docs/guide/` and `docs/ar/` needs it too — and its drawings, which
`python scripts/docs-screens.py` regenerates.

## Security

ByteBridge holds database passwords and is built to be reached from the
public internet through a tunnel. Treat it accordingly.

- **Never hardcode a secret**, in source or in a test.
- Anything that reaches an engine must go through a bound `parameters`
  entry. Never concatenated into SQL.
- `/query` is read-only, and so is the transaction it runs in — a
  statement that got past the text check must still be refused by the
  database. Check that any change to that path keeps both.
- Stored secrets are wrapped by `core/Security/`. They enter and leave
  the settings file only through `SqliteDatabase.GetSetting` /
  `SetSetting`, which wrap them transparently. A new stored secret goes
  in `ProtectedSettingKeys`, never in raw.

**Found a vulnerability? Do not open a public issue.** Report it
privately, as [SECURITY.md](SECURITY.md) describes.

## If you are stuck

Open a draft pull request and ask in the description. A half-finished
attempt with a question in it is easier to help with than an idea.

For a question rather than a change, [GitHub
Discussions](https://github.com/kenanwahbeh/ByteBridge/discussions) is
the place — architecture, a feature you are weighing up, or how
something is meant to work. Use an issue for something that is broken.