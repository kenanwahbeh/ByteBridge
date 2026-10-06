# Security Policy

ByteBridge holds database credentials and is built to be reachable from
the public internet through a tunnel. That makes the API key a
credential in its own right, so reports about it are taken seriously.

## Supported versions

Security fixes land on the current major version. Older ones are not
patched: the fix would not reach anyone, and a release that silently
carries a known weakness is worse than one that is simply behind.

| Version | Supported |
| ------- | --------- |
| 3.x     | Yes |
| < 3.0   | No |

To stay on a supported version, take the [latest
release](https://github.com/kenanwahbeh/ByteBridge/releases/latest).

## Reporting a vulnerability

**Report it privately.** Do not open a public issue.

Use **Security → Report a vulnerability** on this repository
([new advisory](https://github.com/kenanwahbeh/ByteBridge/security/advisories/new)),
which opens a private advisory only the maintainer can see. Issues are
public and permanent, so a report there cannot be taken down once
someone else reads it.

A report that helps includes:

- what an attacker can do, and what they need in order to do it
- the version, and the OS
- the configuration involved — gateway settings, which endpoints, whether
  writing was on, and whether the connection was Online
- how to reproduce it, ideally as a curl request

### What to expect

- An acknowledgement within a few days.
- A fix and a release, or a reason there will not be one. Nothing is left
  silent: if a report is declined, that decision comes with its reasoning.
- Credit in the release notes, unless you would rather stay unnamed.

### What is in scope

- Anything that lets a caller read or change a database they should not
  reach, without the API key.
- A way around the read-only guarantee of `/query`, or around the
  \`Allow writing\` switch that gates `/execute`.
- A weakness in how a secret is stored or handled: the Firebird
  passwords, the API key, the enrolment claim secret, or the request log.
- The authentication path itself: the API key comparison, the
  failed-key lockout, or the Cloudflare Access sign-in.
- A way to make the gateway serve something it should not, or to reach
  the settings file.

### What is not a vulnerability

- **A database user's own privileges.** Anything the Firebird user of an
  Online connection can read, its holder of the API key can read. The key
  grants exactly that and no more.
- **The tunnel being public.** Reaching the gateway is expected; the API
  key is the gate. \`/health\` answers without a key by design and reveals
  only a service name and counts.
- **A full-machine image decrypting the settings file.** The secrets are
  encrypted with a key that never leaves the machine, so a copy of the
  data folder alone opens nothing. An image of the whole machine, or an
  administrator or root on the running machine, can still read them —
  see [Backups and moving to a new
  machine](docs/reference/security.md#backups-and-moving-to-a-new-machine).
  The data folder stays restricted to Administrators and the service
  account for that reason.
- **SQL Server not being supported yet.** It has no read-only
  transaction, so refusing a write there would rest on a text check
  alone. That is why it is absent rather than half-supported — see
  [Database engines](docs/reference/database-engines.md).

## Hardening your own install

These are documented, tested and all still true after an update:

- Leave **Options → Allow writing** off unless you need it, and turn it
  off again afterwards. While it is on, anyone holding the API key can
  run any statement, including schema changes.
- Give the gateway a Firebird user that can only read what it needs.
- Put [Cloudflare
  Access](https://developers.cloudflare.com/cloudflare-one/policies/access/)
  in front of the hostname as well, so callers are authenticated at
  Cloudflare's edge before a request reaches the machine.
- Send values in `parameters`, never concatenated into `sql`.
- Verify a downloaded installer before running it:

  ```
  gh attestation verify ByteBridge-x.y.z-x64-setup.exe --repo kenanwahbeh/ByteBridge
  ```

  Each release file carries a signed statement of the workflow and commit
  that built it. The checksums alone do not prove this: whoever replaces a
  file can replace `SHA256SUMS.txt` with it.

## Security-relevant behaviour to know about

Documented here because a report about any of it is not a finding:

- Secrets in \`bytebridge.db\` are encrypted at rest — DPAPI on Windows,
  an owner-only key file in a sibling folder on Linux. On the live machine
  an administrator can still unwrap them.
- Every request, served or refused, is appended to
  \`C:\\ProgramData\\ByteBridge\\logs\\\`. The statement is recorded; bound
  parameter values are not.
- Wrong API keys are rate limited per caller, identified by
  \`CF-Connecting-IP\`. \`X-Forwarded-For\` is ignored on purpose, because
  the caller writes it.