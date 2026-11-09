"""Claude Code hooks for Fiolyn: rules that were instructions, enforced instead (ADR 0015).

Registered in .claude/settings.json as
  python .claude/hooks/fiolyn_hooks.py <pre-tool|post-tool|stop>
with the hook's JSON on stdin.

  pre-tool   Edit / Write: deny a hand edit of docs/reference/openapi.json (it is generated).
             Bash / PowerShell: ask before `git push` when gh isn't logged in as the repo owner.
  post-tool  Edit / Write on *.cs: report `decimal` on a money or quantity name (ADR 0001).
  stop       When docs/, .claude/CLAUDE.md or README.md has uncommitted changes, docs-lint must pass.

Blocking follows the hook protocol: PreToolUse prints a permissionDecision (deny / ask); PostToolUse and Stop
report through exit code 2 and stderr. A crash in this script never blocks work: it exits 0 (fail open).

FIOLYN_GH_LOGIN overrides the `gh api user` lookup (for the tests).
Tests: python -I -X utf8 .claude/hooks/test_fiolyn_hooks.py
"""
import sys

RAW = None
if __name__ == '__main__':
  RAW = sys.stdin.buffer.read().decode('utf-8', 'replace')
  # Every tool call runs this. One that can't match a rule exits before the imports.
  event_arg = sys.argv[1] if len(sys.argv) > 1 else ''
  if event_arg == 'pre-tool' and 'openapi.json' not in RAW and 'push' not in RAW:
    sys.exit(0)
  if event_arg == 'post-tool' and ('decimal' not in RAW or '.cs' not in RAW):
    sys.exit(0)

import json
import os
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
OPENAPI = 'docs/reference/openapi.json'
OWNER = 'tuananhkt111'
DOCS_PATHS = ['docs', '.claude/CLAUDE.md', 'README.md']  # what docs-lint reads

# `decimal` (or `decimal?`) followed by a name that says money or quantity: field, property, parameter or local.
MONEY_DECIMAL = re.compile(
  r'\bdecimal\s*\??\s+(?P<name>\w*(?:amount|balance|price|total|quantity|qty)\w*)\b', re.I)
GIT_PUSH = re.compile(r'\bgit(?:\s+-[Cc]\s+\S+|\s+--?[\w-]+(?:=\S+)?)*\s+push\b')


# ---------------------------------------------------------------- helpers

def rel(path, cwd=None):
  """The path relative to the repo root (forward slashes), or None when it is outside it."""
  if not path:
    return None
  p = Path(path)
  if not p.is_absolute():
    p = Path(cwd or os.getcwd()) / p
  try:
    return p.resolve().relative_to(ROOT).as_posix()
  except (ValueError, OSError):
    return None


def decide(decision, reason):
  """PreToolUse: deny or ask, with the reason shown to Claude (deny) or the owner (ask)."""
  print(json.dumps({'hookSpecificOutput': {'hookEventName': 'PreToolUse', 'permissionDecision': decision,
                                           'permissionDecisionReason': f'[fiolyn hook] {reason}'}}))
  return 0


def feedback(message):
  """PostToolUse / Stop: exit 2 sends stderr to Claude."""
  print(f'[fiolyn hook] {message}', file=sys.stderr)
  return 2


def run(cmd, timeout=60):
  return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace',
                        timeout=timeout)


def find_bash():
  """Git's bash on Windows (System32\\bash.exe is WSL and can't see this checkout the same way)."""
  if os.name == 'nt':
    git = shutil.which('git')
    if git:
      for up in (Path(git).parent.parent, Path(git).parent.parent.parent):
        candidate = up / 'bin' / 'bash.exe'
        if candidate.is_file():
          return str(candidate)
  return shutil.which('bash')


def gh_login():
  if 'FIOLYN_GH_LOGIN' in os.environ:
    return os.environ['FIOLYN_GH_LOGIN'] or None
  try:
    r = run(['gh', 'api', 'user', '--jq', '.login'], timeout=15)
  except (OSError, subprocess.TimeoutExpired):
    return None
  return r.stdout.strip() if r.returncode == 0 and r.stdout.strip() else None


def unquoted(cmd):
  """The command with heredoc bodies, here-strings and quoted text blanked: a commit message saying
  "git push" is data, not a command."""
  text = re.sub(r'<<-?\s*[\'"]?(\w+)[\'"]?[^\n]*\n.*?\n\s*\1\b', '<<DATA', cmd, flags=re.S)
  text = re.sub(r'@([\'"])\r?\n.*?\r?\n\1@', '@DATA', text, flags=re.S)
  return re.sub(r'"[^"]*"|\'[^\']*\'', '""', text)


# ---------------------------------------------------------------- PreToolUse

def pre_edit(inp, cwd):
  if rel(inp.get('file_path') or inp.get('notebook_path'), cwd) == OPENAPI:
    return decide('deny', f'{OPENAPI} is generated: never hand-edit it. Regenerate it from the running app '
                          '(command in docs/README.md, "Regenerating openapi.json").')
  return 0


def pre_shell(inp):
  if not GIT_PUSH.search(unquoted(inp.get('command', ''))):
    return 0
  login = gh_login()
  if login is None:
    return decide('ask', 'Could not read the gh login (`gh api user --jq .login`). Fiolyn pushes as '
                         'Tuananhkt111: check `gh auth status` before pushing.')
  if login.lower() != OWNER:
    return decide('ask', f'gh is logged in as {login}, but Fiolyn pushes as Tuananhkt111. Run '
                         '`gh auth switch --user Tuananhkt111`, push, then switch back to TuanAnhArent.')
  return 0


# ---------------------------------------------------------------- PostToolUse

def post_edit(inp, cwd):
  path = inp.get('file_path') or ''
  if not path.endswith('.cs'):
    return 0
  r = rel(path, cwd)
  if r is not None and '/Migrations/' in f'/{r}':
    return 0  # generated
  texts = [inp.get('new_string'), inp.get('content')]
  texts += [e.get('new_string') for e in inp.get('edits') or [] if isinstance(e, dict)]
  hits = sorted({m.group(0) for t in texts if isinstance(t, str) for m in MONEY_DECIMAL.finditer(t)})
  if not hits:
    return 0
  return feedback(f'{r or path}: {", ".join(f"`{h}`" for h in hits)}. Money and quantities are `long`, not '
                  '`decimal` (docs/decisions/0001-money-as-long-not-decimal.md). Change it to `long`, unless '
                  'the owner has confirmed a genuine sub-unit requirement.')


# ---------------------------------------------------------------- Stop

def stop(event):
  if event.get('stop_hook_active'):
    return 0  # one push per turn; never loop
  status = run(['git', 'status', '--porcelain', '--', *DOCS_PATHS])
  if status.returncode != 0 or not status.stdout.strip():
    return 0
  bash = find_bash()
  if not bash:
    return 0  # fail open
  lint = run([bash, 'scripts/docs-lint.sh'], timeout=110)
  if lint.returncode == 0:
    return 0
  fails = '\n'.join(l for l in lint.stdout.splitlines() if 'FAIL' in l)
  return feedback('The docs have uncommitted changes and `bash scripts/docs-lint.sh` fails:\n' + fails +
                  '\nFix what this turn broke. If a failure is in a file you did not touch, tell the owner '
                  'instead of changing it.')


def main():
  # Claude Code reads hook output as UTF-8; Windows' default code page would garble "—".
  sys.stdout.reconfigure(encoding='utf-8')
  sys.stderr.reconfigure(encoding='utf-8')
  event_name = sys.argv[1] if len(sys.argv) > 1 else ''
  raw = RAW if RAW is not None else sys.stdin.read()
  event = json.loads(raw or '{}')
  tool = event.get('tool_name', '')
  inp = event.get('tool_input') or {}
  cwd = event.get('cwd')
  if event_name == 'pre-tool':
    if tool in ('Edit', 'Write', 'MultiEdit', 'NotebookEdit'):
      return pre_edit(inp, cwd)
    if tool in ('Bash', 'PowerShell'):
      return pre_shell(inp)
  elif event_name == 'post-tool':
    if tool in ('Edit', 'Write', 'MultiEdit'):
      return post_edit(inp, cwd)
  elif event_name == 'stop':
    return stop(event)
  return 0


if __name__ == '__main__':
  try:
    sys.exit(main())
  except SystemExit:
    raise
  except Exception as e:  # fail open: a broken hook must never block work
    print(f'[fiolyn hook] internal error, allowed: {e!r}', file=sys.stderr)
    sys.exit(0)
