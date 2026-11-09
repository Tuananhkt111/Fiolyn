# 0015 — Claude Code guards in one Python dispatcher, failing open

**Date:** 2026-11-09 · **Status:** Accepted

## Context

Several rules reached Claude Code only as instructions in `.claude/CLAUDE.md`, the docs and the
owner's memory: never hand-edit `docs/reference/openapi.json`, money and quantities are `long`
([0001](0001-money-as-long-not-decimal.md)), push only from the `Tuananhkt111` gh account, and run
docs-lint after a docs change. An instruction gets skipped under load. The git hook
([0014](0014-versioned-git-hooks.md)) catches some of this, but only at commit time, after the work
is done.

## Decision

`.claude/hooks/fiolyn_hooks.py` is one script that handles every hook event. It is registered in
the committed `.claude/settings.json`, so it applies to every session in this repo and its
worktrees, and nowhere else.

| Event | Guard |
|---|---|
| PreToolUse `Edit` / `Write` | deny a hand edit of `docs/reference/openapi.json` |
| PreToolUse `Bash` / `PowerShell` | ask before `git push` when `gh api user` isn't `Tuananhkt111` |
| PostToolUse `Edit` / `Write` on `*.cs` | report `decimal` on an amount, balance, price, total or quantity name |
| Stop | when `docs/`, `.claude/CLAUDE.md` or `README.md` has uncommitted changes, docs-lint must pass |

The script is Python, not bash. It reuses the design of the owner's hooks in another project: a fast exit before the
imports for a call that can't match a rule, quoted text and heredocs blanked before a command is
matched, and a test file (`test_fiolyn_hooks.py`) that runs every case against a scratch clone.

A crash in the script exits 0. A broken guard lets the call through rather than blocking work.

## Consequences

- Python must be on the `PATH` of whatever runs Claude Code here. The app itself doesn't need it.
- The money check reads only the text an edit adds, not the whole file, so an existing `decimal` is
  not reported again on every edit. It matches names, so a money field with an unrelated name
  (`decimal Fee`) passes, and `decimal Percentage` (the one legitimate case) is never reported.
- The push check calls `gh` on each `git push` and asks rather than denies, so the owner can still
  push from another account on purpose.
- The Stop check runs on any uncommitted docs change, not only one made in that turn.
- Regenerating `openapi.json` through the shell (`curl … > docs/reference/openapi.json`) is
  unaffected: only the edit tools are denied.
