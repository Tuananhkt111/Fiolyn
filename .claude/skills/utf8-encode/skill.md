---
name: utf8-encode
description: Re-encode clipboard content to UTF-8 (fixes garbled Japanese/Unicode). Run after /copy.
allowed-tools: Bash
model: haiku
effort: low
---

# UTF-8 Encode

Re-encode the clipboard content with UTF-8 encoding to fix garbled Japanese/Unicode characters.

## Procedure

1. User runs `/copy` first (built-in command)
2. This skill reads clipboard and re-encodes it with UTF-8:

```bash
powershell.exe -Command "$text = Get-Clipboard; [System.Text.Encoding]::UTF8.GetString([System.Text.Encoding]::UTF8.GetBytes($text)) | Set-Clipboard"
```

Then confirm to the user: "Re-encoded to UTF-8 and copied to clipboard."

## Notes

- Run after `/copy` to fix Unicode/Japanese text encoding issues
- UTF-8 encoding ensures text is copied correctly on Windows
