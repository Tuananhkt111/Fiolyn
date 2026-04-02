---
name: pr-create
description: Create a pull request with a structured description following the project PR template. Use when asked to create a PR, open a pull request, or push and create a PR.
allowed-tools: Bash, Read, Glob, Grep
---

# Pull Request Create

## Instructions

Create a pull request using `gh pr create` with a structured description following the project's PR template.

### Steps

1. **Get git info** — run in one command:
   ```bash
   git branch --show-current && git log master..HEAD --oneline && git diff master...HEAD
   ```

2. **Generate PR title and body**, show to user and ask: *"Create this PR? (yes / no)"* — only proceed on confirmation.

3. **Push branch if needed**:
   ```bash
   git push -u origin HEAD
   ```

4. **Create the PR**:
   ```bash
   gh pr create --base master --title "<title>" --assignee Tuananhkt111 --body "$(cat <<'EOF'
   <body>
   EOF
   )"
   ```

Print the PR URL after creation.

---

## PR Title

Short, imperative, ≤ 70 characters. Format: `Tuan Anh/{short-description}`

Examples:
- `Tuan Anh/Fix bug pick from to through shaft`
- `Tuan Anh/Fix bug delete row in detail table`

---

## PR Body Template

Use this **exact structure**. Do not add or remove sections.

```markdown
## Feature Overview
<1–2 sentence summary of what the task/feature/bug is about>

## Technical Changes Summary
<what changed technically — specific files/methods, include before/after code snippets where useful>

## Cause
<root cause of the bug — only for bug fix tasks, skip entirely otherwise>

## Solution
<how it was fixed — only for bug fix tasks, skip entirely otherwise>

## Impact of This Pull Request
<what areas of the app are affected>

## Specific Areas for Review
<highlight tricky or risky parts for the reviewer>

## Review Priority
<When you have time / ASAP / Before EOD / etc.>

## Additional Information
<Any extra context, known limitations, follow-up tasks, or leave blank.>
```

---

## Constraints

- Base branch is always `master`
- Never force-push
- Never skip pre-push hooks
- If `gh` is not authenticated, report the error and stop
