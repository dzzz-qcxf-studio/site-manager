#!/usr/bin/env bash
set -euo pipefail

# One-click bootstrap for a fresh Ubuntu server. The existing installer remains
# the single source of truth for filesystem, Nginx and systemd installation.

readonly DEFAULT_REPOSITORY_URL="https://github.com/dzzz-qcxf-studio/site-manager.git"
readonly DEFAULT_SOURCE_DIR="/root/site-manager-source"
readonly DEFAULT_PUBLIC_KEY_FILE="/root/sitepublisher.pub"

REPOSITORY_URL="${SITE_MANAGER_REPOSITORY_URL:-$DEFAULT_REPOSITORY_URL}"
SOURCE_DIR="${SITE_MANAGER_SOURCE_DIR:-$DEFAULT_SOURCE_DIR}"
AUTHORIZED_KEY_FILE="${SITE_MANAGER_PUBLIC_KEY_FILE:-}"
SERVER_HOST="${SITE_MANAGER_SERVER_HOST:-}"
PUBLIC_BASE_URL="${SITE_MANAGER_PUBLIC_BASE_URL:-}"
NON_INTERACTIVE=false

die() {
    echo "错误：$*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
用法：sudo ./deploy/one-click-install.sh [选项]

自动安装依赖、下载项目、创建 sitepublisher、安装 Nginx/systemd 并启动服务。
只会安装公钥；私钥必须留在 Windows 电脑上。

选项：
  --public-key-file PATH   sitepublisher 的 SSH 公钥文件（必需）
  --server-host HOST       公网 IP 或域名，例如 47.86.89.203
  --public-base-url URL    默认 http://HOST/s/
  --source-dir PATH        项目缓存目录，默认 /root/site-manager-source
  --repository-url URL     Git 仓库地址
  --non-interactive        不询问输入，缺少参数时直接失败
  --help                   显示帮助

示例：
  sudo ./deploy/one-click-install.sh \
    --public-key-file /root/sitepublisher.pub \
    --server-host 47.86.89.203
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --public-key-file|--authorized-key-file)
            [[ $# -ge 2 ]] || die "$1 需要一个路径"
            AUTHORIZED_KEY_FILE="$2"
            shift
            ;;
        --server-host)
            [[ $# -ge 2 ]] || die "--server-host 需要一个值"
            SERVER_HOST="$2"
            shift
            ;;
        --public-base-url)
            [[ $# -ge 2 ]] || die "--public-base-url 需要一个 URL"
            PUBLIC_BASE_URL="$2"
            shift
            ;;
        --source-dir)
            [[ $# -ge 2 ]] || die "--source-dir 需要一个路径"
            SOURCE_DIR="$2"
            shift
            ;;
        --repository-url)
            [[ $# -ge 2 ]] || die "--repository-url 需要一个 URL"
            REPOSITORY_URL="$2"
            shift
            ;;
        --non-interactive)
            NON_INTERACTIVE=true
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            die "未知选项：$1"
            ;;
    esac
    shift
done

[[ "${EUID}" -eq 0 ]] || die "请使用 root 运行，或在命令前加 sudo。"

if [[ -z "$AUTHORIZED_KEY_FILE" ]]; then
    if [[ "$NON_INTERACTIVE" == true ]]; then
        die "非交互模式必须提供 --public-key-file。"
    fi
    read -r -p "请输入已上传到服务器的公钥文件路径 [$DEFAULT_PUBLIC_KEY_FILE]：" entered_key_file
    AUTHORIZED_KEY_FILE="${entered_key_file:-$DEFAULT_PUBLIC_KEY_FILE}"
fi

if [[ -z "$SERVER_HOST" ]]; then
    detected_host="$(hostname -I 2>/dev/null | awk '{print $1}')"
    if [[ "$NON_INTERACTIVE" == true ]]; then
        SERVER_HOST="${detected_host:-47.86.89.203}"
    else
        read -r -p "请输入浏览器访问的公网 IP/域名 [${detected_host:-47.86.89.203}]：" entered_host
        SERVER_HOST="${entered_host:-${detected_host:-47.86.89.203}}"
    fi
fi

[[ "$SERVER_HOST" =~ ^[A-Za-z0-9.-]+$ ]] || die "公网 IP/域名格式不正确：$SERVER_HOST"

if [[ -z "$PUBLIC_BASE_URL" ]]; then
    PUBLIC_BASE_URL="http://${SERVER_HOST}/s/"
fi
[[ "$PUBLIC_BASE_URL" =~ ^https?://[^/]+/s/$ ]] || die "公开基础 URL 必须以 /s/ 结尾：$PUBLIC_BASE_URL"

[[ -f /etc/os-release ]] || die "找不到 /etc/os-release，当前脚本只支持 Ubuntu/Debian 类系统。"
# shellcheck disable=SC1091
source /etc/os-release
[[ "${ID:-}" == "ubuntu" || "${ID_LIKE:-}" == *debian* ]] || die "当前系统不是 Ubuntu/Debian：${PRETTY_NAME:-unknown}"

command -v apt-get >/dev/null 2>&1 || die "找不到 apt-get。"
command -v systemctl >/dev/null 2>&1 || die "找不到 systemctl。"

echo "[1/7] 安装服务器依赖"
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y git nginx python3 openssh-client

command -v ss >/dev/null 2>&1 || die "缺少 ss 命令，请安装 iproute2 后重试。"
command -v git >/dev/null 2>&1 || die "git 安装失败。"

echo "[2/7] 检查 80 端口"
if ss -lntH | awk '$4 ~ /(^|:)80$/ {found=1} END {exit found ? 0 : 1}'; then
    cat >&2 <<'EOF'
检测到 80 端口已经被占用。为避免覆盖现有网站，本次脚本停止执行。
请先在现有 Nginx 的目标 server {} 中加入：

  include /etc/nginx/snippets/site-manager-location.conf;

然后执行 nginx -t && systemctl reload nginx。
EOF
    exit 2
fi

echo "[3/7] 获取项目源码"
if [[ -d "$SOURCE_DIR/.git" ]]; then
    git -C "$SOURCE_DIR" pull --ff-only
elif [[ -e "$SOURCE_DIR" ]]; then
    die "源码目录已存在但不是 Git 仓库：$SOURCE_DIR"
else
    git clone --depth=1 "$REPOSITORY_URL" "$SOURCE_DIR"
fi

[[ -f "$SOURCE_DIR/deploy/install-server.sh" ]] || die "源码中缺少 deploy/install-server.sh。"
[[ -f "$AUTHORIZED_KEY_FILE" ]] || die "找不到公钥文件：$AUTHORIZED_KEY_FILE"
grep -Eiq 'BEGIN[[:space:]].*PRIVATE KEY' "$AUTHORIZED_KEY_FILE" && die "公钥文件疑似包含私钥正文，已停止。"
awk 'NF >= 2 && ($1 ~ /^ssh-/ || $1 ~ /^ecdsa-/) { found=1 } END { exit found ? 0 : 1 }' "$AUTHORIZED_KEY_FILE" \
    || die "公钥文件不是有效的 OpenSSH 公钥：$AUTHORIZED_KEY_FILE"

echo "[4/7] 安装 Site Manager 服务"
chmod +x "$SOURCE_DIR/deploy/install-server.sh"
SITE_MANAGER_PUBLIC_BASE_URL="$PUBLIC_BASE_URL" \
    "$SOURCE_DIR/deploy/install-server.sh" \
    --apply \
    --source-dir "$SOURCE_DIR" \
    --authorized-key-file "$AUTHORIZED_KEY_FILE"

echo "[5/7] 启动网页和清理服务"
systemctl enable --now site-manager-web.service
systemctl enable --now site-manager-purge.timer

systemctl is-active --quiet site-manager-web.service || die "site-manager-web.service 启动失败，请查看 journalctl -u site-manager-web.service。"
systemctl is-active --quiet site-manager-purge.timer || die "site-manager-purge.timer 启动失败。"

echo "[6/7] 获取 SSH 主机指纹"
host_fingerprint=""
if command -v ssh-keyscan >/dev/null 2>&1 && command -v ssh-keygen >/dev/null 2>&1; then
    host_fingerprint="$(ssh-keyscan -T 5 -t ed25519 "$SERVER_HOST" 2>/dev/null | ssh-keygen -lf - -E sha256 2>/dev/null | awk 'NR == 1 { print $2 }')"
fi

echo "[7/7] 部署完成"
cat <<EOF

公网地址：$PUBLIC_BASE_URL
服务器目录：/srv/site-manager
SSH 用户：sitepublisher
网页服务：$(systemctl is-active site-manager-web.service)
清理定时器：$(systemctl is-active site-manager-purge.timer)
EOF

if [[ -n "$host_fingerprint" ]]; then
    echo "SSH 主机指纹：$host_fingerprint"
else
    echo "SSH 主机指纹：未能自动读取，请在 Windows 上使用 ssh-keyscan 获取。"
fi

cat <<'EOF'

Windows 软件设置：
  服务器地址：上面的公网 IP/域名
  SSH 端口：22
  用户名：sitepublisher
  私钥：Windows 本机的 site_manager_ed25519（不要上传服务器）
  公开基础 URL：上面的公网地址

如果仍无法访问，请检查阿里云安全组是否放行 TCP 80，以及：
  systemctl status site-manager-web.service
  journalctl -u site-manager-web.service --no-pager -n 80
EOF
