#!/usr/bin/env bash
# docs-lint.sh — conformance checks for the four-layer documentation contract.
#
# Verifies the structural promises docs/README.md makes:
#   1. every rules/reference/guides file is on the map
#   2. every ADR is in the decisions index
#   3. every reference doc points at its canonical rule
#   4. every rule doc points back at its reference / ADR
#   5. every relative link and #anchor resolves
#   6. no count is frozen into prose
#   7. every class an FRS story lists under **Code:** is still declared under Fiolyn/,
#      and every such story has at least one test tagged [Trait( "Story", "US-nn" )]
#
# Content quality is not checkable here — that is what review is for. This only
# catches the structural drift that always happens silently.
#
# Usage: scripts/docs-lint.sh     — exits non-zero if any check fails.

set -uo pipefail
cd "$( dirname "$0" )/.." || exit 1

ERR="$( mktemp )"
trap 'rm -f "$ERR"' EXIT

report() {
  printf '  %-4s %s\n' "$1" "$2"
  [ "$1" = FAIL ] && printf 'x' >> "$ERR"
  return 0
}

section() { printf '\n== %s\n' "$1" ; }

# Markdown with fenced code blocks removed. Templates inside fences carry
# placeholder links (`rules/<topic>.md`) that must not be resolved.
uncoded() { awk '/^```/ { b = !b ; next } !b' "$1" ; }

# Every heading in a file, as GitHub anchor slugs, one per line.
heading_slugs() {
  grep '^#\{1,\} ' "$1" \
    | sed 's/^#\{1,\} *//' \
    | tr '[:upper:]' '[:lower:]' \
    | sed 's/[^a-z0-9 _-]//g ; s/ /-/g'
}

# ---------------------------------------------------------------- 1. the map

section "Map completeness — docs/README.md"
n=0
for f in docs/rules/*.md docs/reference/* docs/guides/*.md ; do
  [ -f "$f" ] || continue
  n=$(( n + 1 ))
  grep -qF "(${f#docs/})" docs/README.md || report FAIL "not on the map: $f"
done
report OK "$n files checked against the map"

# -------------------------------------------------------------- 2. adr index

section "ADR index — docs/decisions/README.md"
n=0
for f in docs/decisions/[0-9]*.md ; do
  [ -f "$f" ] || continue
  n=$(( n + 1 ))
  grep -qF "($( basename "$f" ))" docs/decisions/README.md \
    || report FAIL "ADR missing from the index: $f"
done
report OK "$n ADRs checked against the index"

# --------------------------------------------------- 3 & 4. layer cross-links

section "Cross-layer pointers"
for f in docs/reference/*.md ; do
  head -12 "$f" | grep -q '\*\*Canonical rules:\*\*' \
    || report FAIL "no 'Canonical rules:' pointer in the first 12 lines: $f"
done
for f in docs/rules/*.md ; do
  head -12 "$f" | grep -q '^> ' \
    || report FAIL "no pointer block in the first 12 lines: $f"
done
report OK "pointer blocks checked"

# ------------------------------------------------------- 5. links and anchors

section "Links and anchors"
n=0
while IFS= read -r src ; do
  [ -n "$src" ] || continue
  dir="$( dirname "$src" )"
  while IFS= read -r raw ; do
    [ -n "$raw" ] || continue
    link="${raw#](}" ; link="${link%)}"
    case "$link" in http*|mailto:*|'') continue ;; esac

    target="${link%%#*}"
    anchor=""
    case "$link" in *#*) anchor="${link#*#}" ;; esac

    if [ -z "$target" ] ; then path="$src" ; else path="$dir/$target" ; fi
    n=$(( n + 1 ))

    if [ ! -e "$path" ] ; then
      report FAIL "$src -> $link (no such file)"
      continue
    fi

    [ -n "$anchor" ] || continue
    case "$path" in *.md) ;; *) continue ;; esac

    # Captured first: piping a live `sed` into `grep -q` closes the pipe early
    # and floods stderr with SIGPIPE noise.
    slugs="$( heading_slugs "$path" )"
    printf '%s\n' "$slugs" | grep -qxF -- "$anchor" \
      || report FAIL "$src -> $link (no heading with that anchor)"
  done < <( uncoded "$src" | grep -oE '\]\([^)]*\)' )
done < <( git ls-files '*.md' )
report OK "$n relative links checked"

# --------------------------------------------------------- 6. frozen counts

section "Frozen counts"
# docs/README.md states the no-frozen-counts rule by quoting violations of it,
# so the line that defines the rule is exempt.
hits="$( grep -rnE '\b[0-9]+ (tests?|endpoints?|controllers?|migrations?|entit(y|ies)|ADRs?|files?|records?|rules?|leaves)\b' \
           docs/ README.md .claude/CLAUDE.md 2>/dev/null | grep -v 'Never write' )"
if [ -n "$hits" ] ; then
  printf '%s\n' "$hits" | while IFS= read -r h ; do report FAIL "$h" ; done
else
  report OK "no counts frozen into prose"
fi

# ------------------------------------------------------ 7. story code links

section "Story code links — FRS/"
# One "<file> <story> <name>" line per name in a **Code:** line, the story taken from the
# nearest '## US-nn' heading above it. The trailing "tests tagged ..." clause is not a name.
links="$( awk '
  /^## US-[0-9]+/ { match( $0, /US-[0-9]+/ ) ; story = substr( $0, RSTART, RLENGTH ) }
  /^\*\*Code:\*\*/ {
    line = $0 ; sub( / · tests tagged.*/, "", line )
    while ( match( line, /`[A-Za-z_][A-Za-z0-9_]*`/ ) ) {
      print FILENAME, story, substr( line, RSTART + 1, RLENGTH - 2 )
      line = substr( line, RSTART + RLENGTH )
    }
  }' FRS/*.md 2> /dev/null )"
n=0
while read -r file story name ; do
  [ -n "$name" ] || continue
  n=$(( n + 1 ))
  git grep -qE "(class|record|interface|enum|struct)[[:space:]]+${name}([^A-Za-z0-9_]|\$)" -- 'Fiolyn/' \
    || report FAIL "$file ($story): \`$name\` is no longer declared under Fiolyn/"
done <<< "$links"
for story in $( printf '%s\n' "$links" | awk 'NF { print $2 }' | sort -u ) ; do
  git grep -qF "[Trait( \"Story\", \"$story\" )]" -- 'Fiolyn.Tests/' \
    || report FAIL "$story has a Code: list but no test tagged [Trait( \"Story\", \"$story\" )]"
done
report OK "$n story code links checked"

# ------------------------------------------------------------------- verdict

if [ -s "$ERR" ] ; then
  printf '\nFAILED — %s problem(s).\n' "$( wc -c < "$ERR" | tr -d ' ' )"
  exit 1
fi
printf '\nAll checks passed.\n'
