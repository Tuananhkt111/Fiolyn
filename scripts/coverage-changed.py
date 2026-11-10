"""coverage-changed.py — the changed lines under Fiolyn/ that no test runs.

Usage (from anywhere in the repo):
  python scripts/coverage-changed.py [<base>] [--no-run]

<base> defaults to master. The diff runs from the merge-base of <base> and HEAD to the working tree, so
committed, staged and unstaged changes all count, and so do untracked .cs files. `HEAD~1` reports on the last
commit alone.

- Run: `dotnet test Fiolyn.Tests --collect "XPlat Code Coverage"` (coverlet.collector, already referenced),
  Cobertura into <temp>/fiolyn-coverage-changed/. --no-run reads the last run's file instead.
- Report: for each changed production .cs file under Fiolyn/ (generated migrations excluded), its changed lines
  that are code and that no test ran. A changed file with no code line in the report is listed as not measured.

It reports and never fails on coverage (ADR 0016): exit 0 unless the tests or git fail. Use it before a PR, then
test each listed line or say in the PR why not.
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS = ROOT / 'Fiolyn.Tests' / 'Fiolyn.Tests.csproj'
OUT = Path(tempfile.gettempdir()) / 'fiolyn-coverage-changed'


def git(*args):
  r = subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, text=True, encoding='utf-8')
  if r.returncode != 0:
    sys.exit(f'git {" ".join(args)}: {r.stderr.strip()}')
  return r.stdout


def production(path):
  return path.startswith('Fiolyn/') and path.endswith('.cs') and '/Migrations/' not in path


def run_tests():
  shutil.rmtree(OUT, ignore_errors=True)
  print('coverage: dotnet test --collect "XPlat Code Coverage" …', flush=True)
  r = subprocess.run(['dotnet', 'test', str(TESTS), '--collect', 'XPlat Code Coverage', '--results-directory',
                      str(OUT), '--nologo', '-v', 'q'], capture_output=True, text=True, encoding='utf-8',
                     errors='replace')
  if r.returncode != 0:
    print(r.stdout[-3000:], r.stderr[-2000:], sep='\n')
    sys.exit('coverage: the tests failed; fix them first.')


def hits():
  """{normalised absolute path: {line: hits}} over every Cobertura file; a line counts as run if any run hit it."""
  lines = {}
  for f in OUT.rglob('*.cobertura.xml'):
    tree = ET.parse(f)
    sources = [s.text or '' for s in tree.iter('source')]
    for cls in tree.iter('class'):
      name = cls.get('filename', '')
      candidates = [name] if os.path.isabs(name) else [os.path.join(s, name) for s in sources]
      path = next((c for c in candidates if os.path.exists(c)), candidates[0] if candidates else name)
      d = lines.setdefault(os.path.normcase(os.path.abspath(path)), {})
      for ln in cls.iter('line'):
        n = int(ln.get('number'))
        d[n] = max(d.get(n, 0), int(ln.get('hits', '0')))
  return lines


def changed_lines(base):
  """{repo path: {new line numbers}} for changed production .cs files."""
  start = git('merge-base', base, 'HEAD').strip()
  out, current = {}, None
  for line in git('diff', '-U0', '--no-color', start, '--', '*.cs').splitlines():
    if line.startswith('+++ '):
      current = line[6:] if line.startswith('+++ b/') else None
      if current and not production(current):
        current = None
    elif current and line.startswith('@@'):
      m = re.match(r'@@ -\S+ \+(\d+)(?:,(\d+))? @@', line)
      first, count = int(m.group(1)), int(m.group(2) or 1)
      out.setdefault(current, set()).update(range(first, first + count))
  for f in git('ls-files', '--others', '--exclude-standard', '--', '*.cs').splitlines():
    if production(f):
      with open(ROOT / f, encoding='utf-8-sig', errors='replace') as fh:
        out[f] = set(range(1, sum(1 for _ in fh) + 1))
  return out


def ranges(nums):
  out = []
  for n in sorted(nums):
    if out and n == out[-1][1] + 1:
      out[-1][1] = n
    else:
      out.append([n, n])
  return [f'{a}' if a == b else f'{a}-{b}' for a, b in out]


def main(argv):
  if argv[:1] in (['-h'], ['--help']):
    print(__doc__)
    return 0
  args = [a for a in argv if not a.startswith('--')]
  base = args[0] if args else 'master'
  changed = changed_lines(base)
  if not changed:
    print(f'No production .cs lines changed since the merge-base with {base}.')
    return 0
  if '--no-run' not in argv:
    run_tests()
  cov = hits()
  if not cov:
    sys.exit(f'coverage: no Cobertura file in {OUT}; run without --no-run.')
  report, not_measured, code_lines, uncovered = {}, [], 0, 0
  for f, nums in sorted(changed.items()):
    d = cov.get(os.path.normcase(str(ROOT / f)))
    code = [n for n in nums if d and n in d]
    if not code:
      not_measured.append(f)
      continue
    code_lines += len(code)
    missed = [n for n in code if d[n] == 0]
    uncovered += len(missed)
    if missed:
      report[f] = ranges(missed)
  print(f'{code_lines} changed code lines since {base}, {uncovered} run by no test')
  for f, rs in report.items():
    print(f'  {f}: {", ".join(rs)}')
  if not_measured:
    print('Not measured (no code lines, or not in the instrumented assembly): ' + ', '.join(not_measured))
  return 0


if __name__ == '__main__':
  sys.exit(main(sys.argv[1:]))
