# Versioning and releasing

## Versioning

Version numbers are [semantic](https://semver.org/): `MAJOR.MINOR.PATCH`.
For this app that means:

| Bump | When |
| ---- | ---- |
| **MAJOR** | Something that breaks an existing caller: an endpoint or response field removed or renamed, a response shape changed, the authentication scheme changed, or a settings file an older version can no longer read. |
| **MINOR** | New behaviour an existing caller can ignore: a new endpoint, an extra response field, a new option in the window. |
| **PATCH** | Fixes and internal work with no visible change to the API or the UI. |

Anything worth mentioning goes into
[CHANGELOG.md](https://github.com/kenanwahbeh/ByteBridge/blob/main/CHANGELOG.md)
under **Unreleased** as it is made, so cutting a release is never a
remembering exercise.

## Releasing

1. Check that the **Unreleased** section of
   [CHANGELOG.md](https://github.com/kenanwahbeh/ByteBridge/blob/main/CHANGELOG.md)
   describes what is about to ship.
2. Cut the release:

   ```
   pwsh scripts/new-release.ps1 -Version 1.0.0
   ```

   That promotes Unreleased to `## [1.0.0]` with today's date, opens a
   fresh Unreleased section, rewrites the comparison links, commits the
   changelog and creates the `v1.0.0` tag. It refuses to run on an
   empty Unreleased section, an existing tag, or a version that is not
   semantic, and it pushes nothing.
3. Publish:

   ```
   git push origin HEAD --follow-tags
   ```

   The tag starts the release workflow, which builds all four
   installers on Windows, copies that version's changelog section into
   the release body, writes `SHA256SUMS.txt`, and attaches everything
   to a GitHub Release. A tag whose version has no changelog section
   fails the build rather than publishing a release with no notes.

To test the packaging without publishing anything, run the **Release**
workflow manually from the Actions tab. It builds the same four
installers, prints the release body it would have used, and leaves the
files as workflow artifacts — no tag, no release.
