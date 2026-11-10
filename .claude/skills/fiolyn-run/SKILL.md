---
name: fiolyn-run
description: Start the Fiolyn API locally in Development (port 5027), list pending EF migrations without applying them, regenerate docs/reference/openapi.json from the running app and summarise its diff. Use when asked to run or start the API, to see a change working against the real app, to check for pending migrations, or after a change that adds, removes or renames an endpoint or alters a request/response record (openapi.json must be regenerated, never hand-edited).
argument-hint: "[--no-build] [--no-openapi] [--stop-after] [--restart] | --stop"
allowed-tools: Read, Bash
---

# /fiolyn-run

Launch, verify, report — in one script, for the checkout you are in (a worktree works too).

## Run

```bash
python .claude/skills/fiolyn-run/fiolyn_run.py [--no-build] [--no-openapi] [--stop-after] [--restart]
python .claude/skills/fiolyn-run/fiolyn_run.py --stop
```

1. **Preflight:** `Fiolyn/appsettings.Development.json` exists and `Jwt:SigningKey` is at least 32
   characters. Values are never printed.
2. **Port 5027:** free, or held by the server this script started for this checkout.
3. **Build** `Fiolyn/` (skip with `--no-build` when it is current).
4. **Migrations:** `dotnet ef migrations list`. Pending ones are listed and **never applied**
   ([ADR 0004](../../../docs/decisions/0004-migrations-never-run-on-startup.md)). Applying is the
   owner's deliberate step: `cd Fiolyn && dotnet ef database update`. Don't run it unless asked.
5. **Start** the built `Fiolyn.dll` in Development and wait for Swagger to answer (90 s at most).
6. **`openapi.json`:** fetched from `/swagger/v1/swagger.json` and written to
   `docs/reference/openapi.json`, then the added (`+`), removed (`-`) and changed (`~`) paths,
   operations and schemas are listed. Skip with `--no-openapi`.

The server keeps running for you to exercise (`curl`, Swagger UI at `http://localhost:5027/swagger`).
Use `--stop-after` when you only need the checks and the regenerated document. **Stop it with
`--stop` when the task is done**, unless the owner wants it left up.

| Exit | Last line | Meaning |
|---|---|---|
| 0 | `RUNNING pid=… url=… log=…` | started; every check above ran |
| 0 | `STOPPED …` | `--stop`, or `--stop-after` once the checks ran |
| 2 | `ERROR …` | missing settings, build failure, startup crash (log tail shown), Swagger timeout |
| 3 | `PORT_BUSY pid=… ours=… cmd=…` | port 5027 is taken |

## Exit 3: port busy

- **`ours=true`:** this script started it for this checkout. Rerun with `--restart` to pick up a new
  build, or use it as it is.
- **`ours=false`:** another worktree's server, a `dotnet run` from an IDE, or something else. Don't
  kill it: tell the owner the pid and command line, and let them decide.

## After it runs

- **Migrations PENDING:** say so in the result. Code that needs the new schema fails at query time
  against this database until the owner applies them.
- **openapi.json REGENERATED:** read `git diff -- docs/reference/openapi.json`. Every change must be
  explained by the change you are making. Commit it in the same commit (implementation loop, phase 6).
  A change you can't explain is a finding: report it rather than reverting it.
- **Telegram webhook:** exercise it with the payloads in
  [`docs/guides/local-development.md`](../../../docs/guides/local-development.md#testing-the-telegram-webhook).
