# ADR 0005 — SQL Server was written, and then taken back out

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** why the third engine is absent, and the bar any next one
  clears

## Context

SQL Server support was implemented: a `SqlServerProvider` alongside
Firebird and PostgreSQL, a stricter text guard, wizard and CLI support,
a connection-string test, and unit tests for the guard — roughly 400
lines, in a pull request that otherwise stood up.

It is not in the released build.

## The two reasons

**1. SQL Server has no read-only transaction.**

On Firebird and PostgreSQL the guarantee is the engine's: the query runs
in a transaction the database itself holds read-only, so a statement
that fooled the text check is refused by the database rather than
trusted not to write. That is what makes the text check a courtesy.

SQL Server has no equivalent. Refusing a write would fall entirely to a
keyword list — and a keyword list cannot see every shape a `SELECT` can
take. The clearest case needs no cleverness at all:

```sql
SELECT 1 AS n WHERE 1 = 0 UNION ALL SELECT balance FROM Secrets
```

No word on any forbidden list. It starts with `SELECT`, it reads a table
it never named, and it would answer. The guard had no answer to it.

The honest alternative was to require a read-only login
(`db_datareader`) and treat the database's permissions as the real
guarantee — which is how the first attempt's own documentation described
it, in a sentence that said "the text check is a safety net, not a
guarantee". That is a weaker product than the other two engines, sold
under the same name.

**2. It had never run against a real server.**

The image wants more memory than a hosted CI runner offers comfortably,
so the connection string and the guard were covered by unit tests alone.
Shipping an engine as the only thing between a public tunnel and
somebody's database, on tests that never reached a server, is not a
trade worth a wider feature list.

## Decision

**Take it back out. Record the bar.**

The code is kept — `SqlServerIntegrationTests` is written and runs
nothing until `BYTEBRIDGE_TEST_SQLSERVER` names a server — so that
resuming it is a matter of a working test rather than a rewrite.

An engine joins when **both** hold:

1. **It holds `/query` read-only itself.** A text check may be an
   addition; it may not be the whole of the protection.
2. **It has run against a real server in CI.** Not a unit test. Not a
   developer's laptop.

Two further things are required, and are not the bar but the process:

3. **A measured number for the publish.** Every engine's client ships
   with the app. Measured, not estimated: PostgreSQL is 85 MB to 86 MB.
4. **Documentation, including what does not work.** PostgreSQL's single
   statement rule and its refusal of session-level advisory locks are
   documented beside the guarantees they belong to.

## Consequences

- **Two engines, both tested live.** Every supported engine now has a
  live CI job against a real server.
- **The cost of an unfinished engine is paid early rather than late.**
  The code existed and looked finished; the two conditions are what
  caught it.
- **`docs/reference/database-engines.md` keeps the reasoning**, so a
  contributor who adds a fourth engine reads the bar before writing the
  provider, not after a review.

## Alternatives considered

**Ship it with the text guard and a warning in the docs.** Rejected. A
weaker guarantee presented as the same guarantee is the failure mode
this project has been most careful about elsewhere: the whole point of
holding the transaction read-only in the engine is that the guard is not
load-bearing.

**Require a read-only login as the documented configuration.** This is
what the first attempt recommended, and it is a reasonable design. It
was rejected because it makes the security model depend on a
configuration step the product cannot verify — and because a connection
that is misconfigured that way writes on the first `/query`.

**Drop the guard entirely and ship neither.** Rejected: the work is
kept, and the gap is two specific things rather than a vague
incompleteness.

## See also

- `docs/reference/database-engines.md` — the engine guide, and this bar
- `docs/developers/architecture.md` — "Another database engine"
- ADR 0001 — what the project is for