# ADR 0003 — `HttpListener`, not Kestrel

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** what the gateway is built on

## Context

The gateway is an HTTP server in front of a database. The obvious choice
on .NET is Kestrel, and it would be better at it: a real HTTP stack,
`HttpContext` instead of `HttpListenerContext`, no hand-rolled body
handling, and the ASP.NET Core pipeline for anything larger.

It was not chosen. The reason is what ships.

Kestrel arrives as `Microsoft.AspNetCore.App`, a **shared framework**
that has to be installed on the machine — or carried inside the publish.
ByteBridge already carries .NET in its bundled installers and expects the
.NET 10 **Desktop** Runtime in its framework-dependent ones. That runtime
does **not** include ASP.NET Core. Adopting Kestrel would mean either
every framework-dependent install grew by a second runtime download, or
every one of them silently needed something new at install time.

The project already declined an ASP.NET Core dependency once, for the
same reason: the README says of the current listener that it was chosen
"so the app keeps its current dependency set and its current publish
layout: no ASP.NET Core runtime to ship alongside the WPF app."

The cost is real. `GatewayServer.cs` is 1,900 lines, and it contains
body-size caps, drain limits, CORS handling and routing by hand — code
that Kestrel would have provided. The early-reply drain logic in
particular is where HTTP bugs live, and it is code this project wrote
itself.

## Decision

**Stay on `HttpListener`.**

When another HTTP engine is added — PostgreSQL, SQL Server — it is a
database provider behind the same four methods. It does not touch the
listener. The listener has exactly one job and one implementation.

## Consequences

- **The dependency set stays as it is.** A framework-dependent install
  needs the Desktop Runtime and nothing else. No second prompt at
  install time, no new prerequisite to document or to fail on.
- **The hand-written HTTP stays hand-written**, and it is the part of
  this codebase most likely to harbour a subtle bug. The tests cover
  the size cap, CORS, an early reply leaving the connection usable, and
  that a refused request drains rather than truncates — but that is a
  test suite written by the same people who wrote the server.
- **Kestrel stays the answer if the listener ever needs more than the
  job above.** Adding a third engine does not change this; the two are
  independent.

## Alternatives considered

**Kestrel, self-contained only.** Rejected. It halves the installer
matrix in the useful direction — nobody downloads the `-framework` build
to save space — and splits the supported configuration into two paths
with different prerequisites, one version apart, which is exactly the
kind of support surface this project does not have the reach for yet.

**A third-party listener such as EmbedIO or WatsonWebserver.** Rejected
for the same reason as Kestrel, and with an added dependency to audit.

**Kestrel in the Linux bundle only.** Tempting — the Linux publish is
already self-contained and carries whatever it needs. Rejected: it makes
the two platforms behave differently for no gain, and "it is different
on Linux" is how a security-relevant difference gets shipped unnoticed.

## See also

- ADR 0001 — what the project is for
- `.github/workflows/release.yml` — the two publish configurations