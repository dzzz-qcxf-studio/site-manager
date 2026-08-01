# SSH 发布协议

> 最后更新：2026-08-01

## 协议原则

- 传输使用 SFTP；控制命令通过 SSH 执行 `site-managerctl`。
- 客户端不得拼接 shell 片段，只能传递经过验证的参数。
- 命令标准输出只包含一行 UTF-8 JSON；日志写到标准错误。
- 每个修改操作带 `requestId`，服务器按请求 ID 保证幂等。
- 协议版本首版为 `1`。

## 公共响应

成功：

```json
{
  "protocolVersion": 1,
  "ok": true,
  "requestId": "0191f7d0-0000-7000-8000-000000000001",
  "data": {}
}
```

失败：

```json
{
  "protocolVersion": 1,
  "ok": false,
  "requestId": "0191f7d0-0000-7000-8000-000000000001",
  "error": {
    "code": "HASH_MISMATCH",
    "message": "上传内容校验失败",
    "retryable": true
  }
}
```

## 站点清单字段

```json
{
  "schemaVersion": 1,
  "id": "0191f7d0-0000-7000-8000-000000000100",
  "name": "产品模型演示",
  "note": "客户 A 的 7 月版本",
  "slug": "a8k3m2",
  "url": "http://47.86.89.203/s/a8k3m2/",
  "status": "live",
  "version": 3,
  "sizeBytes": 10485760,
  "contentSha256": "lowercase-hex",
  "createdAt": "2026-07-31T12:00:00Z",
  "updatedAt": "2026-07-31T13:00:00Z",
  "trashedAt": null,
  "purgeAt": null
}
```

字段规则：

- 时间统一为 UTC RFC 3339，界面显示时转换为本地时区。
- `id` 为 UUID；`slug` 默认生成 8 位，允许长度为 6–12 位。
- `slug` 仅使用无歧义字符集 `abcdefghjkmnpqrstuvwxyz23456789`，不包含易混淆的 `i`、`l`、`o`、`0`、`1`。
- `status` 仅允许 `live`、`trash`。
- `version` 从 1 单调递增。
- `contentSha256` 为 64 位小写十六进制字符串。

客户端短链接生成规则已实现；生产环境使用加密安全随机数源，测试可注入确定性随机序列。

## 命令

```text
site-managerctl status --request-id <uuid>
site-managerctl list --request-id <uuid> [--status live|trash|all]
site-managerctl prepare --request-id <uuid> --mode create|update --site-id <uuid?> --size <bytes> --sha256 <hex>
site-managerctl publish --request-id <uuid> --upload-id <uuid> --name-b64 <base64url> --note-b64 <base64url>
site-managerctl update-meta --request-id <uuid> --site-id <uuid> --name-b64 <base64url> --note-b64 <base64url>
site-managerctl trash --request-id <uuid> --site-id <uuid>
site-managerctl restore --request-id <uuid> --site-id <uuid>
site-managerctl purge --request-id <uuid> --site-id <uuid>
site-managerctl purge-expired --request-id <uuid>
```

名称和备注使用无填充 Base64URL 传递，避免 shell 引号和换行歧义；空备注仍必须保留 `--note-b64` 参数并传递空字符串，服务端解码后仍需验证长度和 UTF-8。

`status` 成功的 `data` 包含 `serverTime`（UTC RFC 3339）以及 `disk.totalBytes`、`disk.freeBytes`。`list` 成功的 `data` 为 `{ "sites": [ ... ] }`，列表按 `updatedAt` 倒序排列；即使服务器上一份独立清单损坏，其他站点仍会正常返回。

`trash`、`restore` 返回更新后的站点清单；`purge` 返回被清理的 `siteId`（幂等的重复清理可返回空值）；`purge-expired` 返回 `purgedSiteIds`。恢复时若同一 slug 已被其他在线站点占用，返回 `SLUG_CONFLICT`。

## 上传会话

`prepare` 返回：

```json
{
  "uploadId": "0191f7d0-0000-7000-8000-000000000200",
  "remotePath": "/srv/site-manager/staging/0191f7d0-0000-7000-8000-000000000200/payload.tar.gz.partial",
  "resumeOffset": 0,
  "expiresAt": "2026-08-01T12:00:00Z"
}
```

客户端重连后重新调用相同 `requestId` 的 `prepare`，服务器返回相同会话和当前安全续传偏移。

服务端会先核对上传包的实际字节数和 SHA-256，再进行安全解压。大小或哈希不符时返回 `SIZE_MISMATCH` 或 `HASH_MISMATCH`，且不得改变既有在线版本；压缩包内容不安全或缺少根 `index.html` 时返回 `ARCHIVE_UNSAFE` 或 `INDEX_MISSING`。

## 错误码

| 错误码 | 含义 | 可重试 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数、字段或编码无效 | 否 |
| `SITE_NOT_FOUND` | 站点不存在 | 否 |
| `SLUG_CONFLICT` | 短链接已被占用 | 可重新生成 |
| `UPLOAD_NOT_FOUND` | 上传会话不存在或过期 | 重新准备 |
| `INSUFFICIENT_SPACE` | 服务器空间不足 | 否 |
| `SIZE_MISMATCH` | 上传大小不符 | 是 |
| `HASH_MISMATCH` | SHA-256 不符 | 是 |
| `ARCHIVE_UNSAFE` | 压缩包存在逃逸或危险条目 | 否 |
| `INDEX_MISSING` | 根目录缺少 `index.html` | 否 |
| `SITE_BUSY` | 同站点正在执行另一操作 | 是 |
| `NGINX_INVALID` | Nginx 配置验证失败 | 否 |
| `INTERNAL_ERROR` | 未分类服务器错误 | 视响应而定 |

新增错误码允许向后兼容；不得改变现有错误码语义。

## 客户端解析与文本编码

- 客户端只接受 `protocolVersion: 1`、非空 UUID `requestId` 和成功响应中的 `data`。未知版本、无效 JSON 或不完整响应均视为协议错误，不继续执行后续操作。
- `ok: false` 会转换为本地 `RemoteCommandException`；调用方可从中读取远端错误码、`retryable` 与 `requestId`，用于界面提示和安全重试。
- 名称和备注的 Base64URL 编码使用 UTF-8，将 `+` 替换为 `-`、`/` 替换为 `_` 并移除尾随 `=`；解码时严格验证 Base64 与 UTF-8。

## 客户端 SSH 适配

- `SshNetRemotePublisher` 只映射本文件列出的 `status`、`list`、`prepare`、`publish`、`trash`、`restore` 和 `purge` 固定命令；不会接受或执行来自界面的任意 shell 文本。
- UUID、状态筛选、字节数和小写 SHA-256 在构造命令前均进行本地校验；名称与备注始终使用 Base64URL，因此中文、空格和换行不会改变 shell 参数边界。
- 每次控制命令均使用配置中的 SSH 主机、端口、用户名、私钥路径和无填充 SHA-256 主机指纹创建短连接。若指纹不匹配，或 SSH 库未报告主机指纹，连接立即失败。
- 上传通道使用相同的严格主机指纹策略建立 SFTP 流。`status` 是唯一由设置页“测试连接”调用的命令，属于只读操作。
