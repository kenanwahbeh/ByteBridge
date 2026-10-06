# ADR 0002 — Version numbers are meaningful from here, and this costs a major bump

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** whether `3.1.0` is the next version, or `4.0.0`

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

**From the next release onward, the numbers are meaningful, and getting
there costs one more major bump: `4.0.0`.**

- `4.0.0` carries PostgreSQL support, the secrets-at-rest work, the
  release attestation, and this decision.
- It is not a MAJOR bump because of PostgreSQL. Adding an engine is
  MINOR under the policy in the README, and nothing in `4.0.0` removes
  or renames a response field: `/health`, `/databases`, `/query` and
  `/execute` keep their shapes. The bump is for the **numbering itself**,
  which is a real change to what a version means and therefore to what a
  reader can assume.
- After that, every release follows the README's policy as written.
- The `3.x` line is **not** supported for security fixes. Fixes go to
  `4.x`. `SECURITY.md` will say so.

## Consequences

**Costs, stated plainly:**

- A second consecutive major bump, on a project a month old, looks
  worse than the alternative. It is also the honest description: the
  meaning of the number changed.
- Anyone who wrote `v3` into a script has to change it. There is one
  release and no announced users, so the cost is theoretical today and
  would not be later.

**Buys:**

- After `4.0.0`, the MAJOR/MINOR/PATCH rules in the README become
  enforceable rather than aspirational. A caller can decide whether to
  pin a version by reading the changelog.
- The changelog stops needing an asterisk.

## Alternatives considered

**Ship `3.1.0` and carry on.** Rejected. It leaves the numbering
permanently symbolic: the README states a policy the history visibly
contradicts, and every future MAJOR bump looks like version inflation
rather than a signal.

**Renumber the existing tags to match.** Rejected. It rewrites published
history and breaks every existing clone and reference, to fix a
cosmetic problem.

## See also

- The versioning policy in `README.md`
- `SECURITY.md` — which versions carry security fixes
- ADR 0001 — what the project is for