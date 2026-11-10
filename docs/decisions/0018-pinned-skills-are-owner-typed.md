# 0018 — Skills whose model pin matters are typed by the owner only

**Date:** 2026-11-10 · **Status:** Accepted

## Context

Six project skills pin a `model` and `effort` in their frontmatter, for example `test-gen` at
sonnet/high and `impact-analysis` at opus/high. A pin applies when the owner types the skill. When
Claude invokes the skill itself, the skill runs inside Claude's own turn, so its pin doesn't take
effect.

## Decision

`test-gen` and `impact-analysis` set `disable-model-invocation: true`. Claude can't invoke them;
the owner types `/test-gen` or `/impact-analysis`, and the pin applies. When Claude needs
`test-gen`'s work during a change, it spawns a fresh agent with `model: sonnet` that reads the
skill file as instructions ([0017](0017-tests-written-and-reviewed-apart-from-the-code.md)). This is
the pattern the owner's skills use in another project.

`commit`, `pr-create` and `utf8-encode` are left invocable. They are cheap at any model, so a pin
that doesn't take effect costs nothing. `model-create` has no pin.

## Consequences

- Asking Claude to "generate tests" or "analyse this PR's impact" no longer loads those skills
  automatically. For `impact-analysis`, which posts a PR comment, that is also a safeguard.
- A fresh agent can't set its own effort, so tests that Claude writes during a change run at
  sonnet with the default effort.
