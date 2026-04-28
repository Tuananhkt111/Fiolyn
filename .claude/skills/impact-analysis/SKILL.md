---
name: impact-analysis
description: Analyze the impact scope of a PR and post a checklist as a PR comment. PR number is required.
argument-hint: "<PR number> [--no-post] [--depth 1|2|3|auto] [--output <output path>] [--format md|json]"
allowed-tools: Read, Glob, Grep, Bash, Write, Agent, Edit
---

# Impact Analysis Skill

## Execution Steps

### Step 0: Parse Arguments

| Argument | Default | Description |
|----------|---------|-------------|
| 1st arg (number) | **required** | PR number |
| `--no-post` | false | Suppress PR comment; output to console only |
| `--depth` | `auto` | Analysis depth (1=direct refs, 2=2-hop, 3=full transitive, auto=adaptive) |
| `--output` | none | Output file path for the detailed report |
| `--format` | `md` | Output format (md/json) |

If no PR number given, auto-search on the current branch. If not found, prompt user.

### Step 1: Retrieve Diff and Classify Changes

Run both commands **in parallel** (they are independent):

```bash
gh pr diff <PR number>
gh pr view <PR number> --json title,body,headRefName,baseRefName,files,commits
```

**Classify changed files:**

| File pattern | Category |
|---|---|
| `Data/Migrations/*.cs` | 🔴 schema change — always critical |
| Source code (`.cs`, excluding migrations) | impact analysis |
| `appsettings*.json` | configuration impact |
| `Dockerfile` / `*.csproj` | deployment/build impact |
| `.github/workflows/*.yml` | CI/CD impact |
| Test code (`*Test*.cs`) | test impact category |
| Documentation (`.md`) | excluded |

### Step 2: Identify Changed Symbols

For each changed source file:

1. **Identify changed methods/properties/classes**
   - Derive enclosing method/class from diff hunk headers (`@@`)
   - Extract method, property, and class declarations from added/removed lines

2. **Classify change type**
   - **Signature change**: method name, parameters, or return type changed → high impact
   - **Logic change**: internal logic modified → medium impact
   - **New addition**: new method/class → limited impact on existing code
   - **Deletion**: method/class removed → impacts all callers
   - **Comment/formatting only** → no impact (exclude)

3. **Noise exclusion**: skip general keywords like get/set/if/else/return/new/var

### Step 3: Dependency Tracing

**Automatic depth selection (`--depth auto`):**

| Condition | Depth | Reason |
|-----------|-------|--------|
| Signature change present | 3 | callers must update; transitive breakage risk |
| Public API / shared utility referenced by 5+ files | 3 | wide impact surface |
| Private/internal method logic change | 2 | propagation possible but limited |
| Leaf method (no downstream calls) | 1 | only direct callers affected |
| Config/resource files only | 1 | impact is localized |

#### 3-1. Direct Reference Trace (depth >= 1)

- **Caller search**: `Grep "SymbolName\(" --type cs`
- **Inheritance/implementation trace**: `Grep "class.*: .*ISymbolName" --type cs`, `Grep "override.*SymbolName" --type cs`
- **Shared resource impact**: constant or enum value changes → all reference sites

**Project-specific traces (run when the matching file is changed):**

| Changed file | Additional search |
|---|---|
| `IRepository<T>` or `Repository<T>` | Grep all entities using `IRepository<` — all data access is affected |
| `IAuditable` or `ISoftDelete` | Grep `IAuditable\|ISoftDelete` to find all implementing entities |
| `AppDbContext` | Grep `AppDbContext` — affects all DB operations and SaveChangesAsync behavior |
| `IServiceRegister` | Grep `IServiceRegister` — DI registration order may be affected |
| `NotionSettings` / settings classes | Grep `IOptions<` to find all injection sites |

#### 3-2. Indirect & Transitive Reference Trace (depth >= 2)

- Repeat one more hop per depth level from callers found at the previous depth
- Stop at: test code / entry points (Execute, Main, Run) / common base methods (ToString, Equals) / 50 entries

### Step 4: Git History Analysis

```bash
# co-change patterns
git log --oneline --name-only --follow -- <file> | head -100
# past bug fixes
git log --oneline --grep="fix\|bug" -- <file>
```

Files with many past bug fixes get their severity raised by one level.

### Step 5: Generate Checklist (for PR comment)

Conciseness is the top priority. Keep each item to 2-3 lines.

**Severity Levels:**

| Level | Criteria | Indicator |
|-------|----------|-----------|
| Critical | Migration changed; `AppDbContext`/`IRepository<T>` signature changed; `TelegramController` return path risks non-200 | 🔴 |
| High | Direct ref + logic change; `IAuditable`/`ISoftDelete` changed; external service interface changed (`INotionService`, `ITelegramMessageService`) | 🟠 |
| Medium | Indirect ref (2-hop); `IServiceRegister` impl changed; co-change pattern | 🟡 |
| Info | Test impact; `appsettings.json` key added/removed; docs update recommended | ⚪ |

**Checklist format (for PR comment):**

```markdown
## Impact Checklist

> **Branch**: `feature/xxx` → `master` | **Changed**: 3 files, 45 lines | **Depth**: 2 (auto)

### 🔴 Must Verify
- [ ] **CHK-001** Telegram → Notion flow (`TelegramMessageService`)
  Send a Telegram message from an allowed user → verify a Notion page is created correctly
  Also verify: webhook always returns 200 even if Notion call fails
- [ ] **CHK-002** Whitelist check (`TelegramMessageService`)
  Send from a non-whitelisted user → verify message is silently ignored (200 returned, no Notion page)

### 🟠 Recommended
- [ ] **CHK-003** Soft-delete filtering (`Repository<T>`)
  Deleted users must not pass the whitelist check — verify the global query filter excludes them

### 🟡 If Time Permits
- [ ] **CHK-004** DI registration (`ScopedServicesRegister`)
  Restart app and verify all services resolve without error

### ⚪ Info
- Co-change: TelegramUserInfo.cs is frequently co-modified with Repository.cs
```

<details>
<summary>📋 Detail Report</summary>

### Summary
- Branch: `feature/xxx` → `master`
- Changed: 3 files, 2 symbols
- Depth: 2 (auto — logic changes detected)

### Changed Symbols
| Symbol | Change Type |
|--------|-------------|
| `TelegramMessageService.ProcessAsync(Update)` | Signature change |
| `TelegramUserInfo.IsAllowed` | Property added |

### Impact Details

**🔴 IMP-001** `TelegramController.Post()` — Controllers/TelegramController.cs:24
Direct caller of ProcessAsync(). Must always return 200 — verify error handling is intact. → CHK-001

**🟠 IMP-002** `Repository<T>.Query()` — Data/Repositories/Repository.cs:13
Soft-delete filter used in whitelist lookup. Logic change may affect which users are allowed. → CHK-003

### Affected Flow
| Trigger | Class | Severity | Checklist |
|---------|-------|----------|-----------|
| Telegram webhook | TelegramController | 🔴 | CHK-001 |
| Whitelist check | TelegramMessageService | 🔴 | CHK-002 |
| Soft-delete filter | Repository\<T\> | 🟠 | CHK-003 |

### Overall Assessment
Signature change in ProcessAsync affects the controller entry point.
Webhook contract (always-200) must be preserved regardless of downstream failures.

</details>
```

**Checklist quality rules:**
- Each item: **Service/class name** + **trigger steps** + **expected result** in 2-3 lines
- Include concrete trigger (e.g., `POST /api/telegram`, "Send from allowed user")
- 🔴 items: always verify the always-200 webhook contract is not broken
- Pure refactoring (no behavioral change): state so explicitly, focus on regression checks

### Step 6: Generate Detail Report

The detailed report provides the **rationale** behind the checklist, placed below in a `<details>` block.

**Conciseness principles:**
- Each impact item: max 2 lines (symbol + reason + check point)
- Minimal code examples and file paths
- Focus on "what to verify", not "what changed"
- Lower severity = shorter description

### Step 7: Post and Output

**Important: Prevent temp file collisions**
Always include the PR number in temp file names (parallel worktree execution risk).
- Temp file: `checklist_<PR number>.md` in the current worktree
- Delete temp file after posting; never use `/tmp/`

**Post checklist + detail report as a PR comment** (unless `--no-post`). Upsert if an existing impact analysis comment exists:
   ```bash
   CHECKLIST_FILE="checklist_<PR number>.md"

   existing=$(gh api "repos/{owner}/{repo}/issues/<PR number>/comments" \
     --jq '[.[] | select(.body | startswith("## Impact Checklist"))] | last | .id // empty')

   if [ -n "$existing" ]; then
     gh api "repos/{owner}/{repo}/issues/comments/$existing" \
       -X PATCH -F "body=@${CHECKLIST_FILE}"
   else
     gh pr comment <PR number> --body-file "${CHECKLIST_FILE}"
   fi

   rm -f "${CHECKLIST_FILE}"
   ```

## Agent Parallelization

For large diffs (5+ files), parallelize with the Agent tool:

- **Agent 1**: Source code dependency tracing (Steps 2-3)
- **Agent 2**: Git history analysis (Step 4)

---

## Explosion Suppression

- Do not trace `System.*` or `Microsoft.*` namespaces
- Exclude ToString(), Equals(), GetHashCode(), SaveChangesAsync() base overrides
- References within test projects → "test impact" category
- depth >= 2 with 50+ results → stop and report count only
- Merge multiple impacts within the same file

## Severity Escalation

| Pattern | Minimum Severity |
|---------|-----------------|
| `Data/Migrations/` file changed | 🔴 |
| `AppDbContext` or `IRepository<T>` signature changed | 🔴 |
| `TelegramController` return path changed (risks non-200) | 🔴 |
| `IAuditable` or `ISoftDelete` interface changed | 🟠 |
| External service interface changed (`INotionService`, `ITelegramMessageService`) | 🟠 |
| `IServiceRegister` implementation changed | 🟡 |
| Shared utility referenced by 5+ files | 🟠 |
| NuGet package update (`*.csproj`) | 🟡 |