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

## Cutting a release — the 4 commands

From a clean `main` branch, after the commit you want to ship is pushed:

```bash
git tag v1.2.3
git push origin v1.2.3
```

That's it. The workflow:
1. Strips the `v` → `1.2.3`
2. Runs `scripts/set-version.ps1 -Version 1.2.3` (rewrites both files)
3. Builds `BrowserMux.sln` + AOT-publishes the Handler
4. Builds the Inno installer + portable zip
5. Creates a GitHub Release with auto-generated notes and both assets attached

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

## Pre-flight checklist

Before pushing a tag, verify:
- [ ] Local build is green (`pwsh build.ps1`) — see `x-build`
- [ ] App launches and basic flows work (picker, settings, rules)
- [ ] CI is green on the commit you're about to tag
- [ ] No uncommitted changes (`git status` clean)
- [ ] You're on `main` and up to date with `origin/main`

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
- `scripts/set-version.ps1` — rewrites version in both source-of-truth files
- `Directory.Build.props` — `<Version>` consumed by all C# projects
- `installer/setup.iss` — `#define AppVersion` consumed by Inno Setup
- `src/BrowserMux.Core/AppInfo.cs` — reads the version from assembly metadata
  at runtime, no edit needed

## Code signing

Releases are currently **unsigned** (SmartScreen warning on first run).
The signing step is stubbed at the bottom of `release.yml`, commented out
until an EV cert is acquired. When the cert lands: uncomment the step,
add `CODE_SIGN_CERT` (base64 .pfx) and `CODE_SIGN_PASSWORD` to the repo
secrets, ship a patch release.
