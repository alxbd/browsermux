---
name: x-release
description: Cut a new BrowserMux release. Use this skill whenever the user wants to ship a version, publish a release, bump a version, or push a tag. Enforces the tag-driven release flow and avoids the classic version-mismatch traps.
---

# x-release — BrowserMux release & versioning

BrowserMux uses a **tag-driven release flow**. You do NOT bump version files
manually — pushing a `v*` tag triggers `.github/workflows/release.yml`, which
runs `scripts/set-version.ps1` to rewrite `Directory.Build.props` and
`installer/setup.iss` from the tag, then builds + publishes the release.

## Versioning policy (semver)

`MAJOR.MINOR.PATCH`:
- **PATCH** — bugfix only, no UX change
- **MINOR** — new feature, backwards-compatible (prefs/rules schema unchanged)
- **MAJOR** — breaking change (incompatible prefs schema, removed feature, UX overhaul)

For pre-releases use a hyphen suffix: `v1.2.0-beta.1`. The workflow auto-marks
any tag containing `-` as a GitHub pre-release.

## Cutting a release

From a clean `main` branch, after the commit you want to ship is pushed.
**Write the release notes first**, then put them in the tag:

```bash
# 1. Write the notes (see "Release notes" below for the required sections)
#    Anywhere outside the repo — the scratchpad is fine, the file is not committed.
# 2. Tag with -a and -F so the notes become the tag message
git tag -a v1.2.3 -F notes.md
git push origin v1.2.3
```

The workflow:
1. Strips the `v` → `1.2.3`
2. Runs `scripts/set-version.ps1 -Version 1.2.3` (rewrites both files)
3. Builds `BrowserMux.sln` + AOT-publishes the Handler
4. Builds the Inno installer + portable zip
5. Reads the annotated tag message and publishes it as the release body, with both
   assets attached

**Never use a bare `git tag v1.2.3`.** A lightweight tag has no message, so the
workflow falls back to GitHub's auto-generated commit list and users see raw commit
subjects. The fallback exists so a release is never empty, not as a normal path — it
emits a CI warning when it fires.

Existing users' auto-updater polls `api.github.com/repos/alxbd/browsermux/releases/latest`
and offers the upgrade.

## Critical rules

- **Tag must start with `v`**. The workflow trigger is `tags: ["v*"]` and the
  auto-updater calls `TrimStart('v')`. `1.2.3` (no v) ships nothing.
- **Never re-tag a published version**. If you shipped a broken `v1.2.3`,
  delete the release + tag and ship `v1.2.4`. Re-tagging desyncs users who
  already updated.
- **Never bump `Directory.Build.props` or `installer/setup.iss` by hand**
  before tagging. The workflow overwrites them from the tag — your manual
  bump is at best redundant, at worst causes a confusing diff in the release commit.
- **Tag the right commit**. `git tag v1.2.3` tags `HEAD`. Make sure `HEAD` is
  the commit you want to release (usually `main` after merging the relevant PR).
- **`git push` alone does not push tags**. Always `git push origin <tagname>`
  (or `git push --tags` if you want to push all unpushed tags — riskier).

## Release notes

Written **before** tagging and shipped inside the annotated tag. The release is
correct the moment it is published, so users and the in-app updater never see raw
commit subjects.

### How to write release notes

1. List commits since the previous tag:
   ```bash
   git log v1.0.2..HEAD --oneline
   ```
2. Group changes into sections. **This list is closed — never invent a section.**
   Use only the ones that apply, always in this order:

   | Section | For |
   |---|---|
   | `## Breaking Changes` | Anything that breaks existing config or behavior. First, so it cannot be missed. |
   | `## What's New` | New user-facing features |
   | `## Improvements` | Enhancements to features that already existed |
   | `## Bug Fixes` | Bug fixes |
   | `## Upgrading` | Only when upgrading itself needs an action or a caveat (manual step, one-off migration, a version that must be installed first). Omit it when the upgrade is uneventful. |

3. Each bullet describes the **user-visible effect**, not the code change.
   Bad:  "Switch Content.KeyDown to Content.PreviewKeyDown"
   Good: "Fixed Enter key not launching the selected browser in the picker"
4. Skip internal-only commits (docs, chores, CI, refactors with no visible effect).
   A release whose commits are all internal gets a one-line "Maintenance release"
   note rather than an empty body.
5. Follow the repo writing rules: English, no emoji, no em dashes, no bold inside
   list items.

### Shipping them

```bash
git tag -a v1.0.3 -F notes.md
git push origin v1.0.3
```

The workflow reads the tag message (`git tag -l --format='%(contents)'`) and passes
it to the release as `body_path`. It falls back to `generate_release_notes` only
when the tag has no message, and warns in the CI log when it does.

### Fixing notes after publishing

The tag message is immutable once pushed, and re-tagging is forbidden. To correct
the text of an already published release, edit the release only:

```bash
gh release edit v1.0.3 --notes-file corrected.md
```

The tag keeps the original wording. That is accepted: the release page is what
users read.

## Pre-flight checklist

Before pushing a tag, verify:
- [ ] Local build is green (`pwsh build.ps1`) — see `x-build`
- [ ] App launches and basic flows work (picker, settings, rules)
- [ ] CI is green on the commit you're about to tag
- [ ] No uncommitted changes (`git status` clean)
- [ ] You're on `main` and up to date with `origin/main`
- [ ] Release notes written, and the tag is annotated (`git tag -a ... -F notes.md`)
- [ ] `git tag --sort=-v:refname | head -1` — the new version is actually higher than
      the last one. The version files in the repo stay at their placeholder value
      because CI rewrites them from the tag, so **never read the version from
      `Directory.Build.props` or `setup.iss`** to work out what shipped last.

## Deleting a botched release

If a tag was pushed but the release is broken:

```bash
# Delete locally
git tag -d v1.2.3
# Delete on remote
git push origin :refs/tags/v1.2.3
# Delete the GitHub Release via gh CLI
gh release delete v1.2.3 --yes
```

Then ship `v1.2.4` with the fix. Do not reuse `v1.2.3`.

## Files involved

- `.github/workflows/release.yml` — the trigger, runs on `push` of `v*` tags
- `scripts/set-version.ps1` — rewrites version in `Directory.Build.props` and `setup.iss`
- `Directory.Build.props` — `Version`, `FileVersion`, `InformationalVersion` consumed by all C# projects
- `installer/setup.iss` — `#define AppVersion` consumed by Inno Setup
- `src/BrowserMux.Core/AppInfo.cs` — reads `InformationalVersion` from assembly metadata
  at runtime, no edit needed (the version comes from `Directory.Build.props`)

## Code signing

Releases are currently **unsigned** (SmartScreen warning on first run).
The signing step is stubbed at the bottom of `release.yml`, commented out
until an EV cert is acquired. When the cert lands: uncomment the step,
add `CODE_SIGN_CERT` (base64 .pfx) and `CODE_SIGN_PASSWORD` to the repo
secrets, ship a patch release.
