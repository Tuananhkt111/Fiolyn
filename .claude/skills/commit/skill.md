---
name: commit
description: Stage and commit changes following the project's conventional commit message rules. Use when asked to commit changes, create a commit, or stage and commit.
allowed-tools: Bash
---

# Commit

Run `git status --short && git diff HEAD` in one command, then stage all modified tracked files (or only files specified by the user) and commit.

## Commit Message Rules

Format: `<prefix>: <short description>` — max 70 characters, imperative mood, lowercase after prefix, no trailing period.

Prefixes: `feat` | `fix` | `refactor` | `perf` | `docs` | `style` | `test` | `chore` | `build` | `ci`

## Constraints

- Never use `--no-verify` or `--amend` (unless explicitly asked)
- Never stage `.env`, credential files, or binary artifacts
- Never stage untracked files unless explicitly requested
- Stop and report if pre-commit hook fails
- Do NOT include `Co-Authored-By` trailer in commit messages
