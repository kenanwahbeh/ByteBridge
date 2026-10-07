# ADR 0001 — ByteBridge is a standalone product

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** what ByteBridge is *for*, and therefore what gets built next

## Context

ByteBridge puts an authenticated HTTP API in front of a local database
so it can be reached from outside the machine through a Cloudflare
Tunnel, without opening a port or touching the router.

It has a second, optional path. `enroll` asks **ByteBalance** — a
storefront SaaS — to create a tunnel for this machine, waits for an
email approval, installs the `cloudflared` connector, and shares the
API key so that storefront can read from this gateway. ByteBalance is
maintained by the same author as ByteBridge.

So there was a question worth answering before the next release:
**is ByteBridge a standalone tool, or the client half of a hosted
product?**

That is not a cosmetic question. It decides whether the next month is
spent on Linux support, on more database engines, and on making the
project pleasant to contribute to — or on multi-tenancy and storefront
integration. Every roadmap item below hangs off it.

## Decision

**ByteBridge is a standalone product. ByteBalance integration is
optional.**

Concretely:

- The manual path — install `cloudflared` yourself, or use a quick
  tunnel — is the default and the one documented first. `enroll` is
  offered beside it, never instead of it.
- **Nothing in the gateway depends on ByteBalance.** `core/Enrollment/`
  depends on nothing else, and nothing outside the CLI and the control
  panel's dialog references it. The separation is already structural,
  not aspirational.
- ByteBalance is a **consumer** of ByteBridge, not a part of it.

## Consequences

**Ordered by what this makes important:**

1. **Linux is a target, not an accident.** "Standalone" means
   open-source users, and most of them are not on Windows. `core/` and
   `service/` are already portable — `service/` targets plain `net10.0`
   and ships a systemd unit and `install.sh`. What is missing is that
   **none of it is exercised by CI**, so the promise is untested. That
   is the first thing to fix.
2. **A roadmap belongs in the README.** A contributor who cannot see
   where the project is going will not build toward it.
3. **The command line gets an Arabic face.** The documentation is fully
   bilingual; `db add --help` is not. For a standalone open-source
   project with an Arabic guide, that is the gap that shows first.
4. **Community work starts now.** One contributor has written all 38
   pull requests. `good first issue` and `help wanted` exist with
   nothing behind them, and Discussions were enabled a day ago.
5. **ByteBalance work is polish, not foundation.** Enrolment is already
   implemented, tested and documented. Improving it matters, and it
   does not compete with the above.

**Accepted cost:** some work now serves two audiences that are not the
same, and the storefront path cannot be tested by an outside
contributor with no ByteBalance account. That is the price of the
optional half, and it is smaller than the cost of being one thing when
the decision is that it is two.

## Alternatives considered

**ByteBridge as the client half of ByteBalance.** Rejected. It would
make every architectural choice — multi-tenancy, who owns the settings
file, what the API key means — answer to a hosted product, and the
project's own documentation, tests and contributing guide all assume a
tool someone runs on their own machine. ByteBalance works with a
gateway that exists without it.

**Neither, and drop enrolment.** Rejected. The integration is written,
tested and works; deleting it would throw away working code and a real
use case.

## See also

- `docs/developers/architecture.md` — where a change belongs
- `docs/reference/bytebalance.md` — the optional path
- ADR 0002 — why the numbering is meaningful from 3.1.0