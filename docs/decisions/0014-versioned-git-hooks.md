# 0014 — Git hooks live in the repo, under `scripts/hooks/`

**Date:** 2026-11-09 · **Status:** Accepted

## Context

`core.hooksPath` pointed at `D:\Fiolyn\.git\hooks`, a folder that stopped existing when the repo
moved. No hook ran, and nothing said so. Rules the docs state as instructions (run docs-lint
before committing, never edit a merged ADR) were checked only when someone remembered to.

## Decision

The hooks are versioned in `scripts/hooks/`. `scripts/setup.sh` sets `core.hooksPath` to that
relative path once per clone, so it survives the repo moving and applies in every worktree.

`pre-commit` runs three checks:

- **A merged ADR is immutable.** A `docs/decisions/NNNN-*.md` that exists in `HEAD` can't be
  deleted or renamed. A change to it may touch only its `**Status:**` line, which is how an ADR is
  marked superseded. A new ADR is always allowed.
- **`scripts/docs-lint.sh` passes.**
- **The solution builds with `-warnaserror` when C# is staged** (`.cs` or `.csproj`). NuGet audit
  advisories (`NU1901`–`NU1904`) stay warnings: a new advisory appears without any code change, and
  would otherwise block every commit until a dependency is bumped.

Plain bash was chosen over a hook manager (Husky, pre-commit.com): it needs nothing beyond git and
the SDK, and the repo has no Node or Python toolchain to hang one on.

## Consequences

- **Tests don't run on commit.** A build plus the suite would slow every C# commit. The suite runs
  in CI's `verify` job ([0013](0013-deploy-gated-on-tests-and-docs-lint.md)) and in the
  implementation loop's verify phase. A commit can still break a test locally; it can't deploy.
- docs-lint and the build read the working tree, not only the staged content. An unstaged fix can
  hide a staged breakage. CI checks the commit itself.
- Each clone has to run `setup.sh` once. Until it does, no hook runs.
- The `**Status:**` exemption also lets that line's date change unchecked.
