# Versioning and releasing

## Versioning

Version numbers are [semantic](https://semver.org/): `MAJOR.MINOR.PATCH`,
and they mean something
([ADR 0002](../decisions/0002-meaningful-versioning.md) says why). For
this app that means:

| Bump | When |
| ---- | ---- |
| **MAJOR** | Something that breaks an existing caller: an endpoint or response field removed or renamed, a response shape changed, the authentication scheme changed, or a settings file an older version can no longer read. |
| **MINOR** | New behaviour an existing caller can ignore: a new endpoint, an extra response field, a new option in the window, a new database engine, a new way to install it. |
| **PATCH** | Fixes and internal work with no visible change to the API or the UI. |

Anything worth mentioning goes into
[CHANGELOG.md](https://github.com/kenanwahbeh/ByteBridge/blob/main/CHANGELOG.md)
under **Unreleased** as it is made, so cutting a release is never a
remembering exercise.

## Releasing

`main` is protected: every change reaches it through a pull request, so
a release does too. The tag, not the pull request, is what publishes.

1. Check that the **Unreleased** section of
   [CHANGELOG.md](https://github.com/kenanwahbeh/ByteBridge/blob/main/CHANGELOG.md)
   describes what is about to ship, and that `main` is green.
2. On a branch cut from `main`, promote the changelog without
   committing:

   ```
   scripts/new-release.ps1 -Version x.y.z -NoCommit
   ```

   (It runs in Windows PowerShell and in `pwsh`.) That turns Unreleased
   into `## [x.y.z]` with today's date, opens a fresh Unreleased
   section and rewrites the comparison links. It refuses to run on an
   empty Unreleased section, an existing tag, or a version that is not
   semantic, and it pushes nothing. Without `-NoCommit` it also commits
   and tags, which only suits a repository where you may push to the
   branch directly.
3. Commit the changelog as `Release x.y.z`, open a pull request, and
   merge it once the checks pass.
4. Tag the merge commit and push the tag:

   ```
   git tag vx.y.z <merge-commit>
   git push origin vx.y.z
   ```

   Releases are immutable: a tag that exists is never moved, so a
   mistake is fixed by the next version.

   A version with a suffix, such as `3.3.0-beta.1`, is published as a
   GitHub pre-release: it is not "latest", so neither the update check
   nor the apt repository offers it to anyone.

## What the tag does

The tag starts the **Release** workflow, then the **APT repository**
workflow when that succeeds.

**Release**, in two jobs:

- *Build installers* (Windows) runs the test suite first, so a release
  never ships from a red commit. It then builds the four installers,
  writes `SHA256SUMS.txt`, attests every file, copies that version's
  changelog section into the release body and creates the GitHub
  Release. A tag whose version has no changelog section fails here
  rather than publishing a release with no notes.
- *Build Linux packages* (Ubuntu) waits for the first job, publishes
  the service for `linux-x64`, builds the `.deb` with
  `scripts/linux/build-deb.sh` and a tarball of the bundle, checks both,
  writes `SHA256SUMS-linux.txt` (a separate file, so the attested
  `SHA256SUMS.txt` never changes), attests all three and adds them to
  the release.

**APT repository** rebuilds the signed repository from the `.deb` files
of the last five releases and publishes it on GitHub Pages at
<https://kenanwahbeh.github.io/ByteBridge/>. Each package is verified
first with `gh attestation verify`, and only one attested by the Release
workflow for its own tag is signed in. The workflow then installs
`bytebridge` from the repository it just built, with `apt`, and checks
the service answers `/health`, before anything is published. Releases
that carry no `.deb` (before 3.2.0) are skipped.

## Checking a release

After the workflows finish:

- The release has the four Windows installers, the `.deb`, the
  `linux-x64` tarball and both checksum files.
- A file verifies:

  ```
  gh attestation verify bytebridge_x.y.z_amd64.deb --repo kenanwahbeh/ByteBridge
  ```

- On a Debian or Ubuntu machine, `apt update` shows the new version and
  `apt install bytebridge` installs it.

## Testing the packaging without publishing

Run the **Release** workflow manually from the Actions tab and give it a
version. It builds the same installers and Linux packages, prints the
release body it would have used, and leaves the files as workflow
artifacts: no tag, no release. The **APT repository** workflow can be
run by hand too, and runs on pull requests that touch the Linux
packaging; off `main` it builds and tests the repository but never
publishes it.

## The signing key

The apt repository is signed with a key held in the repository secret
`APT_GPG_PRIVATE_KEY`, and the workflow fails if it is missing. GitHub
Pages has to be set to deploy from **GitHub Actions** (Settings →
Pages). Keep an offline copy of the private key: it is the only thing
that can sign new versions under the key users already trust. If it is
lost, generate a new one, replace the secret, and tell users to fetch
`bytebridge.asc` again; until they do, `apt update` rejects the
repository.
