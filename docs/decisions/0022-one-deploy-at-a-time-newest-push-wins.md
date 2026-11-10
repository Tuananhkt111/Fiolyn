# 0022 — One deploy at a time: the newest push to `master` wins

**Date:** 2026-11-10 · **Status:** Accepted

## Context

Merging a stack of PRs on 2026-11-10 pushed to `master` twelve times in about four minutes. Each
push started its own run of `deploy.yml`, and the runs went in parallel. They finished out of
order, so the last image pushed to `ghcr.io/tuananhkt111/fiolyn:latest` came from an older commit,
not from the newest one. Northflank pulls `:latest` on restart, so the next restart would have
deployed the wrong version.

The same runs showed a second gap: the Northflank restart failed with `curl -s -f`, which prints
nothing, so the log didn't say whether the cause was the token, the service or the network.

## Decision

- The workflow declares `concurrency` at workflow level, with `cancel-in-progress: true`. All
  pushes to `master` share one group, so a newer push cancels the older run outright, `verify`
  included. Each pull request has its own group, keyed by its ref, so PR runs never cancel a deploy.
- The restart step records the HTTP status, prints it, and on failure prints Northflank's response
  and fails with an error naming the likely cause.

Job-level concurrency on `build-and-deploy` alone was rejected. Runs whose `verify` finished out of
order could still enter the deploy group oldest-last, and the older image would win.

## Consequences

- A burst of merges deploys once, from the newest commit. The superseded commits on `master` don't
  get their own `verify` run in CI; the newest run verifies the cumulative state.
- A cancelled run can stop after pushing its image but before the restart. The newer run then
  pushes `:latest` again and restarts, so the result is still the newest image.
- Re-running the newest `master` run after a failed restart is now a safe fix: it rebuilds
  `:latest` from that commit and restarts.
