# CLI contract

The executable is `SiteManager.App.exe`. Use `--json` for machine-readable responses.

| Command | Required options | Optional options |
|---|---|---|
| `status` | — | `--settings`, `--json` |
| `sync` | — | `--settings`, `--json` |
| `list` | — | `--status live\|trash\|all`, `--settings`, `--json` |
| `publish` | `--source`, `--name` | `--note`, `--archive`, `--settings`, `--json` |
| `update` | `--site`, `--source` | `--name`, `--note`, `--archive`, `--settings`, `--json` |
| `open` | `--site` | `--launch`, `--settings`, `--json` |
| `trash` | `--site` | `--settings`, `--json` |
| `restore` | `--site` | `--settings`, `--json` |
| `purge` | `--site`, `--yes` | `--settings`, `--json` |

Success envelope:

```json
{
  "ok": true,
  "command": "list",
  "data": {
    "status": "live",
    "sites": [],
    "count": 0
  }
}
```

Failure envelope:

```json
{
  "ok": false,
  "command": "purge",
  "error": {
    "code": "CONFIRMATION_REQUIRED",
    "message": "永久删除必须显式提供 --yes。"
  }
}
```

Site objects include `id`, `name`, `note`, `slug`, `status`, `version`, timestamps, and the derived public `url`. Never expose `contentSha256` or local private-key paths to a user unless the user specifically asks for non-sensitive diagnostics.
