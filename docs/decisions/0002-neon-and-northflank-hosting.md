# 0002 — Neon over Supabase, Northflank over Render

**Date:** 2026-04-01 · **Status:** Accepted

## Context

Fiolyn is a personal-finance service with one household's traffic. It needs a Postgres database and
somewhere to run a container, both on free tiers. The binding constraint is not throughput or
storage — it is **latency on the first request after idle**, because the service spends almost all
its time idle and is then used interactively.

Two free-tier behaviours break that:

- **Database auto-pause.** Supabase's free tier pauses a project after a period of inactivity; the
  next query pays the wake-up cost, and a paused project needs manual intervention to restore.
- **Container cold starts.** Render's free tier spins containers down when idle; the next request
  waits for a full container start.

Either one turns "send a Telegram message, see it recorded" into a multi-second wait, on exactly
the request a user notices most.

## Decision

- **Database: Neon.** Its free tier never auto-pauses.
- **Hosting: Northflank.** Its free tier is always-on, with no cold starts.

Both chosen for idle behaviour specifically, not for feature breadth or quota size.

## Consequences

- The app is deployed as a container image rather than via a platform-native buildpack — see
  [`0003`](0003-no-https-redirection.md) for the proxy consequence and
  [`../guides/deployment.md`](../guides/deployment.md) for the pipeline.
- Northflank terminates TLS externally, which constrains the ASP.NET middleware pipeline.
- Migrations are not applied by the platform, which is what forces the explicit step in
  [`0004`](0004-migrations-never-run-on-startup.md).
- If either provider changes its idle policy, the reason for that choice disappears with it. This
  decision is about a free-tier behaviour, not a technical property of the products.
