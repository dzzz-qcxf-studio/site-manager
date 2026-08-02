---
name: site-manager-cli
description: Operate the Site Manager Windows application through its built-in command-line mode for listing, publishing, updating, opening, trashing, restoring, and permanently deleting static websites. Use when an AI must manage websites on the configured server through the same SiteManager.App.exe GUI/CLI program, especially when structured JSON output and safe confirmations are required.
---

# Site Manager CLI

Use the same `SiteManager.App.exe` that starts the WPF desktop interface. With command arguments it performs one operation and exits; it uses the existing `%APPDATA%/SiteManager/settings.json`, SSH host-key verification, SFTP publisher, and local cache.

## Locate and invoke

1. Prefer `artifacts/SiteManager-win-x64/SiteManager.App.exe` in the repository or the installed `SiteManager.App.exe` supplied by the user.
2. Run it from PowerShell with `& $exe ...` so paths remain argument-safe.
3. Add `--json` to every automated call. stdout then contains exactly one JSON response; progress is written to stderr.
4. Never pass a private-key value, password, token, or secret on the command line. The CLI reads only the configured private-key path.

If the executable or settings file is missing, report the missing prerequisite instead of guessing a credential or server.

## Safe workflow

### Inspect

Run `list --status live --json` before changing a site. Use the returned `id` or `slug` as the selector. A name is allowed only when it is unique; if the CLI reports ambiguity, ask the user to choose an ID or slug.

Use `status --json` to verify the configured server and `sync --json` when the local cache may be stale.

### Publish a new site

Confirm the source folder and display name, then run:

```powershell
& $exe publish --source "C:\path\to\site" --name "展示名称" --note "可选备注" --json
```

The source must contain a lowercase `index.html` at its root and must pass the existing size and sensitive-file checks. Report the returned `site.id`, `site.slug`, and `url`.

### Update an existing site

Select the live site first, then preserve its identity with:

```powershell
& $exe update --site <id-or-slug> --source "C:\path\to\site" --json
```

Add `--name` or `--note` only when the user requests a metadata change. Do not replace the selector with a display name after the operation.

### Open or share

Use `open --site <id-or-slug> --json` to return the public URL. Add `--launch` only when the user explicitly asks to open the browser on the current machine; otherwise report the URL without launching anything.

### Trash and restore

Use `trash --site <id-or-slug> --json` for a live site and `restore --site <id-or-slug> --json` for a trash site. Re-list after mutation if the user needs the refreshed catalog.

### Permanent deletion

Treat `purge` as destructive. Explain that it removes the server copy and ask for explicit confirmation. Only after confirmation run:

```powershell
& $exe purge --site <id-or-slug> --yes --json
```

Never add `--yes` based only on a vague request such as “clean it up”.

## Results and errors

For `--json`, check both the process exit code and `ok`. Exit code `0` means success; `2` is an argument/configuration error; `3` is a remote or filesystem failure; `4` is a missing confirmation or ambiguous/missing site selector. Read the `error.code` and `error.message` fields, then report the smallest actionable next step. See [references/cli-contract.md](references/cli-contract.md) for the command matrix and response examples.
