# 网页展台 CLI 模式设计

> 状态：已确认
> 最后更新：2026-08-02

## 目标

在现有 Windows 桌面程序中加入命令行模式。`SiteManager.App.exe` 不带参数时启动 WPF 界面，带命令参数时执行一次 CLI 任务并退出；两种入口共享配置、SSH/SFTP、Core 用例和本地缓存。

## 命令

| 命令 | 用途 | 关键参数 |
|---|---|---|
| `status` | 查询服务器状态 | `--json` |
| `sync` | 拉取完整站点清单并更新本地缓存 | `--json` |
| `list` | 查询站点 | `--status live\|trash\|all` |
| `publish` | 发布新网站 | `--source`、`--name`、`--note`、`--archive` |
| `update` | 更新已有网站 | `--site`、`--source`、`--name`、`--note` |
| `open` | 输出网站公网地址 | `--site`、`--launch` |
| `trash` | 移入回收站 | `--site` |
| `restore` | 从回收站恢复 | `--site` |
| `purge` | 永久删除 | `--site`、`--yes` |

站点选择器支持站点 ID、slug 或唯一名称。名称匹配多个项目时拒绝执行并返回候选列表，避免 AI 误操作。

## 输出协议

- `--json` 时 stdout 只输出一个 JSON 文档，字段使用 camelCase。
- 成功格式为 `{ "ok": true, "command": "...", "data": ... }`。
- 失败格式为 `{ "ok": false, "command": "...", "error": { "code": "...", "message": "..." } }`。
- 非 JSON 模式输出简短人类可读文本；进度信息写入 stderr。
- 退出码：`0` 成功，`2` 参数或配置错误，`3` 远程/文件系统操作失败，`4` 用户确认缺失或选择器不唯一。

## 安全边界

- 继续读取 `%APPDATA%/SiteManager/settings.json`，不增加私钥内容、密码或 API key 参数。
- `purge` 必须显式提供 `--yes`。
- 所有远程命令继续由 `IRemotePublisher` 发送，并保留主机指纹校验。
- 默认 `open` 只输出 URL；只有显式 `--launch` 才调用系统浏览器。

## Skill

新增 `site-manager-cli` Skill，指导 AI 先读取配置和站点清单，再使用 ID/slug 执行操作；发布、更新和永久删除必须在执行前确认关键参数，并优先使用 `--json`。
