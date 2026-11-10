"""Exercises fiolyn_hooks.py case by case, on a scratch clone of this repo (the Stop check reads git state).

Usage: python -I -X utf8 .claude/hooks/test_fiolyn_hooks.py
Exit code 0 when every case passes. The clone lives in a temp folder and is removed afterwards.
"""
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

SRC = Path(__file__).resolve().parent.parent.parent
TMP = Path(tempfile.mkdtemp(prefix='fiolyn-hooks-'))
D = TMP / 'repo'
subprocess.run(['git', 'clone', '-q', '--no-hardlinks', str(SRC), str(D)], check=True)
(D / '.claude' / 'hooks').mkdir(parents=True, exist_ok=True)
HOOK = D / '.claude' / 'hooks' / 'fiolyn_hooks.py'
shutil.copy2(SRC / '.claude' / 'hooks' / 'fiolyn_hooks.py', HOOK)  # the working copy, committed or not
subprocess.run(['git', '-C', str(D), 'add', '-A'], check=True)
subprocess.run(['git', '-C', str(D), '-c', 'core.hooksPath=/dev/null', '-c', 'user.name=t', '-c', 'user.email=t@t',
                'commit', '-qm', 'test: hook under test', '--allow-empty'], check=True)
OPENAPI = str(D / 'docs' / 'reference' / 'openapi.json')
results = []


def run(event, payload, env=None, raw=None):
  r = subprocess.run([sys.executable, str(HOOK), event], input=raw if raw is not None else json.dumps(payload),
                     capture_output=True, text=True, encoding='utf-8', env={**os.environ, **(env or {})})
  decision = None
  if r.stdout.strip().startswith('{'):
    decision = json.loads(r.stdout)['hookSpecificOutput']['permissionDecision']
  return r.returncode, decision, (r.stdout + r.stderr).strip()


def case(name, event, payload, expect, env=None, raw=None):
  code, decision, out = run(event, payload, env, raw)
  got = decision or ('exit2' if code == 2 else 'pass' if code == 0 else f'exit{code}')
  ok = got == expect
  results.append(ok)
  print(f'{"OK  " if ok else "FAIL"} {name}: expected {expect}, got {got}' + ('' if ok else f'\n     {out[:400]}'))


def pre(tool, inp):
  return {'hook_event_name': 'PreToolUse', 'tool_name': tool, 'tool_input': inp, 'cwd': str(D)}


def post(tool, inp):
  return {'hook_event_name': 'PostToolUse', 'tool_name': tool, 'tool_input': inp, 'cwd': str(D), 'tool_response': {}}


def stop(active=False):
  return {'hook_event_name': 'Stop', 'cwd': str(D), 'stop_hook_active': active}


owner = {'FIOLYN_GH_LOGIN': 'Tuananhkt111'}
other = {'FIOLYN_GH_LOGIN': 'TuanAnhArent'}
unknown = {'FIOLYN_GH_LOGIN': ''}

try:
  # --- openapi.json is generated
  case('edit openapi.json', 'pre-tool', pre('Edit', {'file_path': OPENAPI, 'old_string': 'a', 'new_string': 'b'}), 'deny')
  case('write openapi.json', 'pre-tool', pre('Write', {'file_path': OPENAPI, 'content': '{}'}), 'deny')
  case('edit openapi.json, relative path', 'pre-tool',
       pre('Edit', {'file_path': 'docs/reference/openapi.json', 'old_string': 'a', 'new_string': 'b'}), 'deny')
  case('edit another reference doc', 'pre-tool',
       pre('Edit', {'file_path': str(D / 'docs' / 'reference' / 'auth.md'), 'old_string': 'a', 'new_string': 'b'}), 'pass')
  case('openapi.json outside this repo', 'pre-tool',
       pre('Write', {'file_path': str(TMP / 'docs' / 'reference' / 'openapi.json'), 'content': '{}'}), 'pass')
  case('regenerating with curl is allowed', 'pre-tool',
       pre('Bash', {'command': 'curl -s http://localhost:5027/swagger/v1/swagger.json > docs/reference/openapi.json'}), 'pass')

  # --- git push from the right account
  case('push as another account', 'pre-tool', pre('Bash', {'command': 'git push -u origin ci/verify-gate'}), 'ask', other)
  case('push as the owner', 'pre-tool', pre('Bash', {'command': 'git push'}), 'pass', owner)
  case('push, login unknown', 'pre-tool', pre('Bash', {'command': 'git push'}), 'ask', unknown)
  case('git -C <dir> push in PowerShell', 'pre-tool', pre('PowerShell', {'command': 'git -C "D:\\x" push origin master'}),
       'ask', other)
  case('push after another command', 'pre-tool', pre('Bash', {'command': 'git status && git push --force-with-lease'}),
       'ask', other)
  case('"push" inside a commit message', 'pre-tool', pre('Bash', {'command': 'git commit -m "docs: git push rules"'}),
       'pass', other)
  case('"git push" inside a heredoc', 'pre-tool',
       pre('Bash', {'command': "cat > notes.txt <<'EOF'\ngit push origin master\nEOF"}), 'pass', other)
  case('gh pr create is not a push', 'pre-tool', pre('Bash', {'command': 'gh pr create --title x'}), 'pass', other)
  case('git status (fast exit)', 'pre-tool', pre('Bash', {'command': 'git status'}), 'pass', other)

  # --- money as long
  cs = str(D / 'Fiolyn' / 'Data' / 'Entities' / 'Tmp.cs')
  case('decimal Amount property (Write)', 'post-tool',
       post('Write', {'file_path': cs, 'content': 'public class T\n{\n  public decimal Amount { get; set; }\n}\n'}), 'exit2')
  case('decimal? UnitPrice parameter (Edit)', 'post-tool',
       post('Edit', {'file_path': cs, 'old_string': 'x', 'new_string': 'public record R( decimal? UnitPrice, long Id );'}),
       'exit2')
  case('decimal local total', 'post-tool',
       post('Edit', {'file_path': cs, 'old_string': 'x', 'new_string': 'decimal total = 0;'}), 'exit2')
  case('decimal _balance field (MultiEdit)', 'post-tool',
       post('MultiEdit', {'file_path': cs, 'edits': [{'old_string': 'a', 'new_string': 'private decimal _balance;'}]}),
       'exit2')
  case('decimal Quantity in a test file', 'post-tool',
       post('Edit', {'file_path': str(D / 'Fiolyn.Tests' / 'X.cs'), 'old_string': 'x', 'new_string': 'decimal Quantity;'}),
       'exit2')
  case('decimal Percentage is fine', 'post-tool',
       post('Edit', {'file_path': cs, 'old_string': 'x', 'new_string': 'public decimal Percentage { get; init; }'}), 'pass')
  case('long Amount is fine', 'post-tool',
       post('Edit', {'file_path': cs, 'old_string': 'x', 'new_string': 'public long Amount { get; set; } // not decimal'}),
       'pass')
  case('"decimal" in a comment', 'post-tool',
       post('Edit', {'file_path': cs, 'old_string': 'x', 'new_string': '// rounded to 2 decimal places'}), 'pass')
  case('decimal Amount in Markdown', 'post-tool',
       post('Write', {'file_path': str(D / 'docs' / 'x.md'), 'content': 'decimal Amount'}), 'pass')
  case('decimal Amount in a migration', 'post-tool',
       post('Write', {'file_path': str(D / 'Fiolyn' / 'Data' / 'Migrations' / 'M.cs'), 'content': 'decimal Amount'}), 'pass')

  # --- Stop: docs-lint when the docs changed
  case('stop, clean tree', 'stop', stop(), 'pass')
  code_file = D / 'Fiolyn' / 'Program.cs'
  code_file.write_text(code_file.read_text(encoding='utf-8') + '\n// touched\n', encoding='utf-8')
  case('stop, code changed only', 'stop', stop(), 'pass')
  guide = D / 'docs' / 'guides' / 'deployment.md'
  guide.write_text(guide.read_text(encoding='utf-8') + '\nSee [nothing](nope.md).\n', encoding='utf-8')
  case('stop, docs changed and lint fails', 'stop', stop(), 'exit2')
  case('stop, lint fails but the hook already fired', 'stop', stop(active=True), 'pass')
  subprocess.run(['git', '-C', str(D), 'checkout', '-q', '--', 'docs/guides/deployment.md'], check=True)
  guide.write_text(guide.read_text(encoding='utf-8') + '\nA clean line.\n', encoding='utf-8')
  case('stop, docs changed and lint passes', 'stop', stop(), 'pass')

  # --- fail open
  case('malformed input fails open', 'pre-tool', None, 'pass', raw='{not json push')
finally:
  def unlock(func, path, _):
    os.chmod(path, stat.S_IWRITE)
    func(path)
  shutil.rmtree(TMP, onexc=unlock)

print(f'\n{sum(results)}/{len(results)} passed')
sys.exit(0 if all(results) else 1)
