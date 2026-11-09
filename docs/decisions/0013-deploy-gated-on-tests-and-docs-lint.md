# 0013 — Deploy only after tests and docs-lint pass

**Date:** 2026-11-09 · **Status:** Accepted

## Context

The pipeline built the image, pushed it to GHCR and restarted Northflank on every push to `master`.
Nothing ran the test suite or `scripts/docs-lint.sh` on the way. A commit that broke a test, or a
doc link, reached production as soon as it was pushed. There is no staging environment, so
production was the first place a regression could show up.

## Decision

`deploy.yml` gets a `verify` job: `dotnet test Fiolyn.sln -c Release`, then
`bash scripts/docs-lint.sh`. `build-and-deploy` declares `needs: verify` and runs only on a push.
Pull requests into `master` run `verify` alone.

Both jobs stay in one workflow file. A separate test workflow chained by `workflow_run` was
rejected: it would split one release across two runs and make the dependency harder to see.

The SDK is installed from `global.json`. Its version changed from `10.0.0` to `10.0.100`, because
`actions/setup-dotnet` rejects a version without a feature band whenever `rollForward` is set.
`latestMinor` still lets any 10.x SDK at or above it build locally.

## Consequences

- A release waits for the test suite and docs-lint to finish before the image build starts.
- A red `verify` blocks every deploy, including an urgent fix, until the failure is fixed or the
  test is changed. There is no bypass switch.
- The pipeline still doesn't apply migrations
  ([0004](0004-migrations-never-run-on-startup.md)). Tests run on EF InMemory, so they don't catch a
  migration that is missing or that fails on Postgres.
- Branch protection on `master` (requiring `verify` before merge) is a repository setting, not part
  of this file. It isn't set up by this change.
