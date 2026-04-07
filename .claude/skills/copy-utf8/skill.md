---
name: copy-utf8
description: Copy the last assistant response to clipboard with UTF-8 encoding (fixes garbled Japanese/Unicode on Windows).
allowed-tools: Bash
---

# Copy UTF-8

Copy the last assistant response to clipboard using UTF-8 encoding.

## Procedure

Run this single command — no confirmation needed:

```bash
powershell.exe -Command "Get-Content 'C:\Users\MSI10~1\AppData\Local\Temp\claude\response.md' -Encoding UTF8 | Set-Clipboard"
```

Then confirm to the user: "Copied to clipboard (UTF-8)."
