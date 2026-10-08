# ADR 0002 — Version numbers are meaningful from here

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** that `3.1.0` is the next version, not `4.0.0`

## Context

ByteBridge released six versions in three weeks:

| Tag | Date | What it was |
| --- | --- | --- |
| `v1.0.0` | 2026-09-15 | first release |
| `v1.0.1`, `v1.1.0`, `v1.2.0` | 2026-09-15 … 09-21 | the read-only gateway, the illustrated guide |
| `v2.0.0` | 2026-09-30 | the write switch moved behind an option |
| `v3.0.0` | 2026-10-01 | that change, plus Cloudflare Access sign-in |

Six releases, three majors, in the project's third week. The README
documents a proper semantic-versioning policy: MAJOR for a removed or
renamed response field, MINOR for an addition, PATCH for a fix.

Two of those majors were real — v2.0.0 and v3.0.0 both changed whether
`/execute` answers `403`. But v1.0.0 arrived on day three, before
anything about the product's shape was known, and everything since has
been measured against it. A version number is a promise about what a
reader of the changelog can rely on, and a 1.0.0 that turns out to
describe a moving target is a promise already broken.

## Decision

**The next release is `3.1.0`. From there the numbers are meaningful.**

- `3.1.0` carries PostgreSQL support, the secrets-at-rest work, the
  release attestation, and this decision.
- Adding an engine is MINOR under the policy in the README, and nothing
  in it removes or renames a response field: `/health`, `/databases`,
  `/query` and `/execute` keep their shapes.
- From `3.1.0` on, every release follows the README's policy as written.
- The `3.x` line stays supported for security fixes, as `SECURITY.md`
  says.

This ADR first proposed `4.0.0`, a major bump for the numbering itself.
The owner chose `3.1.0` instead on 2026-10-07.

## Consequences

**Costs, stated plainly:**

- The history still holds three majors in three weeks. The README's
  policy is true from `3.1.0` onward, not before; the changelog says so
  rather than hiding it.

**Buys:**

- No second consecutive major bump, and nobody who wrote `v3` into a
  script has to change it.
- A caller can decide whether to pin a version by reading the changelog
  from here on.

## Alternatives considered

**Ship `4.0.0` to mark the new meaning of the numbers.** Rejected by the
owner. Nothing breaks for a caller, so a major bump would signal a break
that is not there.

**Renumber the existing tags to match.** Rejected. It rewrites published
history and breaks every existing clone and reference, to fix a
cosmetic problem.

## See also

- The versioning policy in `README.md`
- `SECURITY.md` — which versions carry security fixes
- ADR 0001 — what the project is for