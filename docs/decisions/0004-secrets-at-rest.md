# ADR 0004 — Secrets at rest, and where the key lives

- **Status:** accepted
- **Date:** 2026-10-06
- **Decides:** what protects the stored credentials, and what honestly
  does not

## Context

`bytebridge.db` holds Firebird passwords, the gateway API key and the
enrolment claim secret. For most of the project's life it held them in
plain text, protected only by an ACL on the data folder. That folder is
restricted to Administrators and the service account, which is a real
boundary on the running machine — and no boundary at all against a
stolen disk, a backup, a VM snapshot or a file copied off a machine.

The API key is the credential that matters most: it stands between the
public internet, by way of the tunnel, and somebody's database.

## Decision

**Wrap the secrets with a key that never leaves the machine.**

- **Windows:** DPAPI, `LocalMachine` scope. The scope is deliberate —
  the service runs as Local System while the panel and the command line
  run as an administrator, and `CurrentUser` would encrypt for one of
  them and lock the others out.
- **Linux:** AES-256-GCM with a random 32-byte key in a file, created
  once and never overwritten, readable only by its owner.
- **The Linux key lives in a sibling of the data folder, never inside
  it** — `/var/lib/bytebridge-keys` beside `/var/lib/bytebridge`, or
  `$BYTEBRIDGE_KEY_DIR`. This was corrected after review: the key was
  originally written into the data folder, which meant **a backup of
  the data folder carried the key that opened it**, and the ciphertext
  was worth nothing.
- **A migration on open** wraps anything an older version left plain,
  then checkpoints and rebuilds the database, because committing the
  migration does not remove the old bytes from the file: they survive in
  the write-ahead log and in the freed space of the pages the new rows
  landed in.
- **`secure_delete` on every connection**, so a later key rotation or
  password change zeroes what it overwrites.

## What this does not do

Stated here because the honest boundary is the point, and because a
security document that overclaims is worse than one that admits a limit:

- **An administrator on the running machine can still read them.**
  `LocalMachine` DPAPI decrypts for any local process; root reads any
  file. The data folder's ACL remains the real wall.
- **A full-machine image still decrypts them.** A disk clone or a VM
  snapshot contains the data folder *and* the key, on either OS. Such
  an image is a credential in its own right.
- **A full restore onto the same machine works**, which is the recovery
  story: the key never moved.
- **Moving to a new machine means re-entering secrets**, not copying
  them. Passwords read empty until they are typed again, and the API
  key is re-minted.

## Consequences

- **The settings file on its own is no longer enough.** That is the
  whole gain.
- **The data folder must be backed up separately from the key**, and the
  documentation has to say so, or an operator will do the obvious wrong
  thing.
- **A copied file cannot be restored elsewhere without re-entering
  secrets.** That is a cost, accepted: it is the difference between a
  backup and a breach.
- **The migration touches a live database at open**, so it checkpoints
  and rebuilds, which needs exclusive access it may not get while the
  service is running. It is best effort for that reason, and a
  deferred scrub leaves nothing exposed — the values are wrapped either
  way, and `secure_delete` covers the next overwrite.

## Alternatives considered

**A passphrase the administrator enters.** Rejected: an unattended
service that serves with nobody signed in cannot ask for one.

**Windows Credential Manager, `libsecret` on Linux.** Rejected as
per-user by default, which has the same scope problem as `CurrentUser`
DPAPI and would leave the service unable to read its own settings.

**Not encrypting, and documenting that the folder is the boundary.**
This was the status quo. It is a real boundary on a live machine and no
boundary at all on a disk image — which is where database credentials
actually leak.

## See also

- `docs/reference/security.md` — "Backups and moving to a new machine"
- ADR 0001 — what the project is for