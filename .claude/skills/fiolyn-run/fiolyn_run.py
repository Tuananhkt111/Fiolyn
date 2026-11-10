"""fiolyn_run.py — start the Fiolyn API locally, check migrations, regenerate openapi.json. Used by /fiolyn-run.

Usage (from anywhere in a Fiolyn checkout or worktree):
  python .claude/skills/fiolyn-run/fiolyn_run.py [--no-build] [--no-openapi] [--stop-after] [--restart]
  python .claude/skills/fiolyn-run/fiolyn_run.py --stop

Steps: preflight (Fiolyn/appsettings.Development.json, Jwt:SigningKey) → port 5027 → build → pending migrations
(listed, never applied: ADR 0004) → start the built Fiolyn.dll in Development → wait for Swagger → regenerate
docs/reference/openapi.json and summarise its diff. The server keeps running unless --stop-after.

Last line and exit code:
  0  RUNNING pid=… url=… log=…     started (or --stop-after: STOPPED after the checks)
  0  STOPPED pid=…                 --stop
  2  ERROR …                       preflight, build or start failed
  3  PORT_BUSY pid=… cmd=…         port 5027 is taken by a process this script didn't start for this checkout
                                   (one it did start is restarted with --restart, else reported the same way)
No secret is ever printed: the settings file is checked for presence and length only.
"""
import json
import os
import re
import signal
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

PORT = 5027
URL = f'http://localhost:{PORT}'
SWAGGER = f'{URL}/swagger/v1/swagger.json'
STATE_DIR = Path(tempfile.gettempdir()) / 'fiolyn-run'
STATE = STATE_DIR / 'state.json'


def out(line):
  print(line, flush=True)


def finish(code, line):
  out(line)
  sys.exit(code)


def checkout():
  r = subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True)
  root = Path(r.stdout.strip()) if r.returncode == 0 else Path(__file__).resolve().parents[3]
  if not (root / 'Fiolyn' / 'Fiolyn.csproj').is_file():
    finish(2, f'ERROR not a Fiolyn checkout: {root}')
  return root


# ---------------------------------------------------------------- state and processes

def load_state():
  try:
    return json.loads(STATE.read_text(encoding='utf-8'))
  except (OSError, ValueError):
    return {}


def save_state(state):
  STATE_DIR.mkdir(parents=True, exist_ok=True)
  STATE.write_text(json.dumps(state, indent=2), encoding='utf-8')


def alive(pid):
  if not pid:
    return False
  if os.name == 'nt':
    r = subprocess.run(['tasklist', '/FI', f'PID eq {pid}', '/NH'], capture_output=True, text=True)
    return str(pid) in r.stdout
  try:
    os.kill(pid, 0)
    return True
  except OSError:
    return False


def kill(pid):
  if os.name == 'nt':
    subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], capture_output=True)
  else:
    try:
      os.kill(pid, signal.SIGTERM)
    except OSError:
      pass
  for _ in range(20):
    if not alive(pid):
      return
    time.sleep(0.25)


def port_owner():
  """(pid, command line) of the process listening on PORT, or (None, None)."""
  if os.name == 'nt':
    r = subprocess.run(['netstat', '-ano', '-p', 'TCP'], capture_output=True, text=True)
    pid = next((int(l.split()[-1]) for l in r.stdout.splitlines()
                if 'LISTENING' in l and re.search(rf'[:.]{PORT}\s', l)), None)
    if pid is None:
      return None, None
    c = subprocess.run(['powershell', '-NoProfile', '-Command',
                        f'(Get-CimInstance Win32_Process -Filter "ProcessId={pid}").CommandLine'],
                       capture_output=True, text=True)
    return pid, c.stdout.strip() or '?'
  r = subprocess.run(['lsof', '-t', f'-iTCP:{PORT}', '-sTCP:LISTEN'], capture_output=True, text=True)
  pid = int(r.stdout.split()[0]) if r.stdout.split() else None
  return pid, (Path(f'/proc/{pid}/cmdline').read_text().replace('\0', ' ') if pid else None)


# ---------------------------------------------------------------- steps

def preflight(root):
  path = root / 'Fiolyn' / 'appsettings.Development.json'
  if not path.is_file():
    finish(2, f'ERROR {path.relative_to(root)} is missing: see docs/guides/configuration.md')
  try:
    cfg = json.loads(path.read_text(encoding='utf-8-sig'))
  except ValueError as e:
    finish(2, f'ERROR {path.relative_to(root)} is not valid JSON: {e}')
  key = (cfg.get('Jwt') or {}).get('SigningKey') or ''
  if len(key) < 32:
    finish(2, 'ERROR Jwt:SigningKey is missing or under 32 characters: the app refuses to start without it')
  if not (cfg.get('ConnectionStrings') or {}):
    out('WARN no ConnectionStrings section: queries will fail')
  out('preflight: development settings present, signing key long enough')


def build(root):
  out('build: dotnet build Fiolyn …')
  r = subprocess.run(['dotnet', 'build', str(root / 'Fiolyn' / 'Fiolyn.csproj'), '-nologo', '-v', 'q'],
                     capture_output=True, text=True, encoding='utf-8', errors='replace')
  if r.returncode != 0:
    errors = sorted({l.strip() for l in r.stdout.splitlines() if ' error ' in l})
    finish(2, 'ERROR build failed:\n  ' + '\n  '.join(errors[:15]))


def migrations(root):
  """Lists pending migrations. Never applies them: that is a deliberate, separate step (ADR 0004)."""
  r = subprocess.run(['dotnet', 'ef', 'migrations', 'list', '--no-build', '--project', str(root / 'Fiolyn')],
                     capture_output=True, text=True, encoding='utf-8', errors='replace', cwd=root / 'Fiolyn',
                     timeout=120)
  if r.returncode != 0:
    reason = next((l.strip() for l in (r.stderr + r.stdout).splitlines() if l.strip()), 'no output')
    out(f'migrations: UNKNOWN (dotnet ef failed: {reason[:200]})')
    return
  names = [l.strip() for l in r.stdout.splitlines() if re.match(r'^\d{14}_', l.strip())]
  pending = [n.replace('(Pending)', '').strip() for n in names if n.endswith('(Pending)')]
  if pending:
    out(f'migrations: {len(pending)} PENDING — ' + ', '.join(pending))
    out('  not applied (ADR 0004). Apply on purpose: cd Fiolyn && dotnet ef database update')
  else:
    out(f'migrations: up to date ({len(names)} applied)')


def start(root):
  dll = root / 'Fiolyn' / 'bin' / 'Debug' / 'net10.0' / 'Fiolyn.dll'
  if not dll.is_file():
    finish(2, f'ERROR {dll.relative_to(root)} is missing: run without --no-build')
  STATE_DIR.mkdir(parents=True, exist_ok=True)
  log = STATE_DIR / f'fiolyn-{time.strftime("%Y%m%d-%H%M%S")}.log'
  env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Development', ASPNETCORE_URLS=URL)
  flags = 0x00000008 | 0x00000200 if os.name == 'nt' else 0  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
  with open(log, 'wb') as fh:
    proc = subprocess.Popen(['dotnet', str(dll)], cwd=root / 'Fiolyn', env=env, stdout=fh, stderr=subprocess.STDOUT,
                            stdin=subprocess.DEVNULL, creationflags=flags, start_new_session=os.name != 'nt')
  out(f'start: pid {proc.pid}, log {log}')
  deadline = time.time() + 90
  while time.time() < deadline:
    if proc.poll() is not None:
      tail = log.read_text(encoding='utf-8', errors='replace').splitlines()[-25:]
      finish(2, 'ERROR the app exited during startup:\n  ' + '\n  '.join(tail))
    try:
      with urllib.request.urlopen(SWAGGER, timeout=2) as resp:
        if resp.status == 200:
          save_state({'pid': proc.pid, 'checkout': str(root), 'started': time.time(), 'log': str(log)})
          return proc.pid, log
    except OSError:
      pass
    time.sleep(1)
  kill(proc.pid)
  finish(2, f'ERROR Swagger did not answer at {SWAGGER} within 90 s (log: {log})')


def summarise(old, new):
  """The paths, operations and schemas that differ between two OpenAPI documents."""
  lines = []
  op, np_ = old.get('paths', {}), new.get('paths', {})
  for p in sorted(set(np_) - set(op)):
    lines.append(f'  + {p} ({", ".join(m.upper() for m in np_[p])})')
  for p in sorted(set(op) - set(np_)):
    lines.append(f'  - {p}')
  for p in sorted(set(op) & set(np_)):
    for m in sorted(set(op[p]) | set(np_[p])):
      if op[p].get(m) != np_[p].get(m):
        verb = '+' if m not in op[p] else '-' if m not in np_[p] else '~'
        lines.append(f'  {verb} {m.upper()} {p}')
  os_, ns = old.get('components', {}).get('schemas', {}), new.get('components', {}).get('schemas', {})
  for s in sorted(set(os_) | set(ns)):
    if os_.get(s) != ns.get(s):
      verb = '+' if s not in os_ else '-' if s not in ns else '~'
      lines.append(f'  {verb} schema {s}')
  return lines


def openapi(root):
  path = root / 'docs' / 'reference' / 'openapi.json'
  with urllib.request.urlopen(SWAGGER, timeout=10) as resp:
    body = resp.read().replace(b'\r\n', b'\n')
  before = path.read_bytes() if path.is_file() else b'{}'
  if body == before:
    out('openapi.json: unchanged')
    return
  path.write_bytes(body)  # the generated way (docs/README.md), not a hand edit
  try:
    lines = summarise(json.loads(before), json.loads(body))
  except ValueError:
    lines = []
  stat = subprocess.run(['git', '-C', str(root), 'diff', '--shortstat', '--', 'docs/reference/openapi.json'],
                        capture_output=True, text=True).stdout.strip()
  out(f'openapi.json: REGENERATED, {stat or "changed"}')
  for l in lines or ['  (formatting only: no path, operation or schema differs)']:
    out(l)
  out('  review: git diff -- docs/reference/openapi.json; commit it with the change that caused it')


def stop():
  state = load_state()
  pid = state.get('pid')
  if not alive(pid):
    STATE.unlink(missing_ok=True)
    finish(0, 'STOPPED nothing was running')
  kill(pid)
  STATE.unlink(missing_ok=True)
  finish(0, f'STOPPED pid={pid}')


def main(argv):
  if '-h' in argv or '--help' in argv:
    print(__doc__)
    return 0
  sys.stdout.reconfigure(encoding='utf-8')
  if '--stop' in argv:
    stop()
  root = checkout()
  preflight(root)
  pid, cmd = port_owner()
  if pid:
    state = load_state()
    ours = state.get('pid') == pid and state.get('checkout') == str(root)
    if ours and '--restart' in argv:
      out(f'port {PORT}: restarting pid {pid} (started by this script for this checkout)')
      kill(pid)
    else:
      finish(3, f'PORT_BUSY pid={pid} ours={str(ours).lower()} cmd={cmd}')
  if '--no-build' not in argv:
    build(root)
  migrations(root)
  pid, log = start(root)
  if '--no-openapi' not in argv:
    openapi(root)
  if '--stop-after' in argv:
    kill(pid)
    STATE.unlink(missing_ok=True)
    finish(0, f'STOPPED pid={pid} after the checks')
  finish(0, f'RUNNING pid={pid} url={URL}/swagger log={log}  (stop: --stop)')


if __name__ == '__main__':
  sys.exit(main(sys.argv[1:]))
