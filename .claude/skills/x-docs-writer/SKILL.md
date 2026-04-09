---
name: x-docs-writer
description: Write or update files under docs/ when implementing a new feature, changing existing behavior, when the user asks to "document" something or "add to the docs", or when refactoring something already documented. The docs/ folder is written by AI for AI — keep it terse, structured, and discoverable.
---

# x-docs-writer

The `docs/` folder is BrowserMux's **AI-readable knowledge base**. Future Claude sessions will use these files to understand how subsystems work, what conventions exist, and what's intentionally not implemented. When you change the codebase in a way that contradicts or extends what's in `docs/`, you must update `docs/` in the same change.

## When this skill triggers

- **Implementing a feature** that touches user-visible behavior → update `docs/features.md` and, if the feature is a non-trivial subsystem, write or extend a reference doc.
- **Changing existing behavior** that's already described in `docs/` → update the affected doc(s) in the same task.
- **The user says** "add to the docs", "document this", "update the docs", "explain this in docs/...", "we should mention this somewhere", etc.
- **Refactoring** something documented (file moved, function renamed, table of paths affected) → fix references.
- **Reading** the codebase to answer a question → check `docs/` first; it's terser than the source and is the canonical narrative.

## Step 1 — Discover existing docs

Always start by listing the directory. Filenames are intentionally meaningful so you can pick the right file in one glance:

```bash
ls docs/
```

Then read the relevant file(s) before writing. **Never assume a doc doesn't cover something** — confirm by reading.

Cross-reading is cheap and prevents duplication: if you're touching a subsystem that has its own reference doc, also skim `features.md` to see how it's listed there, and `versionning.md` if you're touching schema or version code.

## Step 2 — Pick the doc type

There are four kinds of doc in this folder. Choose the right one for what you're writing:

| Type | Example | Use for |
|---|---|---|
| **Inventory** | `features.md` | One-line-per-feature catalog. Every user-visible behavior must have an entry here. Flat `##` area / `###` sub-feature. Items not yet built are marked `_(not implemented)_`. |
| **Subsystem reference** | `installer-inno.md`, `auto-update.md` | Deep dive on one well-bounded subsystem. Has intro, "Where it lives" file table, sections, code blocks, troubleshooting table. Create only when the subsystem is non-trivial enough to warrant standalone reading. |
| **How-to / concept** | `local-dev-mode.md` | Explains a workflow or mental model that spans the codebase. "How it works", "What lives where" tables, "Day-to-day workflow", troubleshooting Q&A. |
| **Policy / process** | `versionning.md` | Rules for how to do something correctly (version bumps, schema migrations). Numbered sections, "What not to do", workflow steps. |

**Default to updating an existing doc.** Create a new file only when the topic doesn't naturally fit into any existing one *and* is large enough to stand alone (rough threshold: >80 lines of substantive content). A new feature usually means *adding an entry to `features.md`*, not creating a new file.

## Step 3 — Follow the conventions

These are factored out of the existing docs so the AI consumer can pattern-match across files.

### Always

- **English only.** Project rule (CLAUDE.md → General rules).
- **File paths in inline code**: `` `%LOCALAPPDATA%\BrowserMux\preferences.json` ``, `` `src/BrowserMux.Core/AppInfo.cs` ``.
- **Code blocks tagged with a language**: `powershell`, `csharp`, `xml`, `pascal`, `jsonc`, `bash`, `ini`. Never untagged.
- **`---` between top-level sections** (the actual horizontal rule, not just whitespace).
- **Tables for path/file inventories.** Common column shapes:
  - `File | Role` (e.g. solution architecture, file map)
  - `Path | Purpose | Written by` (e.g. files on disk in CLAUDE.md)
  - `Symptom | Cause | Fix` (troubleshooting)
- **Reference filenames as links** when cross-referencing: `[installer-inno.md](installer-inno.md)`.
- **Be terse.** This is for AI consumption — full sentences are fine, but no marketing prose, no hedging, no "in this section we will". Lead with the fact.

### Never

- ❌ Don't use emoji in headings or body (the existing `❌` in `versionning.md` is the lone exception and is being phased out).
- ❌ Don't number top-level sections (`## 1. ...`) unless you're explicitly writing a process doc — `versionning.md` is the only file that does this.
- ❌ Don't repeat content that already lives in `CLAUDE.md` — link to it instead. CLAUDE.md is loaded into every session; `docs/` files are loaded on demand.
- ❌ Don't write tutorials for humans. If you find yourself explaining "open your terminal and type", you're in the wrong register.
- ❌ Don't hardcode the version anywhere — see `versionning.md`.

### Section patterns to reuse

**Subsystem reference docs** (like `auto-update.md`, `installer-inno.md`) work best with this skeleton:

```markdown
# <Title>

<One-paragraph intro: what this subsystem does and why it exists.>

---

## Quick overview / Quick start

<ASCII diagram OR a single command + one sentence.>

---

## Where it lives

| File | Role |
|---|---|
| `path/to/file.ext` | What it does |

---

## <Topic>

<Sections covering the moving parts: data flow, persistence, edge cases.>

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
```

**Inventory entries** in `features.md` are 1–3 sentences, present tense, third person:

```markdown
### Feature name
One- or two-sentence description of what the user sees and how it behaves. Mention persistence/scope if relevant.
```

## Step 4 — Cross-link and keep `features.md` current

Whenever you write or extend a subsystem reference doc, **also add or update the corresponding entry in `features.md`** so the inventory stays the index of truth. The inventory entry should be short and link to the reference doc when one exists. Example:

```markdown
### Update check
Queries the GitHub Releases API for a newer version, with a 6-hour cooldown. See [auto-update.md](auto-update.md).
```

## Step 5 — Verify before declaring done

Before you call the doc work finished:

1. **Read your file end-to-end** in the editor. Check that headings are consistent, tables render, code blocks have languages.
2. **Grep for stale references** to anything you renamed or moved: `Grep` the whole `docs/` tree.
3. **Check `features.md`** has an entry pointing at any new subsystem doc you wrote.
4. **Don't bump version numbers in docs** to match a release — version-bump rules live in `versionning.md` and are a separate task.

## Quick reference — current docs

Names are sensible enough that `ls docs/` is usually enough to pick a file. But if you need a one-liner each:

| File | What it covers |
|---|---|
| `features.md` | Inventory of every user-visible feature, including ones intentionally not built |
| `installer-inno.md` | Inno Setup installer: prereqs, registry keys, prerequisite download flow |
| `auto-update.md` | In-app self-updater: GitHub API check, silent reinstall, `/RELAUNCH` switch |
| `local-dev-mode.md` | Dual-channel Dev/Prod build identities and how they coexist |
| `versionning.md` | App version + config schema versioning rules and migration workflow |
