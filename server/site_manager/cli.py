from __future__ import annotations

import argparse
import base64
import json
import os
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Sequence
from uuid import UUID

from .archive import ArchiveSafetyError, IndexMissingError
from .lifecycle import LifecycleService, SlugConflictError
from .publisher import InsufficientSpaceError, PayloadHashMismatchError, PayloadSizeMismatchError, Publisher
from .registry import Registry


PROTOCOL_VERSION = 1


def main(arguments: Sequence[str] | None = None, *, root: Path | None = None) -> int:
    parser = argparse.ArgumentParser(prog="site-managerctl")
    commands = parser.add_subparsers(dest="command", required=True)

    status = commands.add_parser("status")
    status.add_argument("--request-id", required=True)

    list_command = commands.add_parser("list")
    list_command.add_argument("--request-id", required=True)
    list_command.add_argument("--status", choices=("live", "trash", "all"), default="live")

    prepare = commands.add_parser("prepare")
    prepare.add_argument("--request-id", required=True)
    prepare.add_argument("--mode", required=True, choices=("create", "update"))
    prepare.add_argument("--site-id")
    prepare.add_argument("--size", required=True, type=int)
    prepare.add_argument("--sha256", required=True)

    publish = commands.add_parser("publish")
    publish.add_argument("--request-id", required=True)
    publish.add_argument("--upload-id", required=True)
    publish.add_argument("--name-b64", required=True)
    publish.add_argument("--note-b64", required=True)

    for command_name in ("trash", "restore", "purge"):
        command = commands.add_parser(command_name)
        command.add_argument("--request-id", required=True)
        command.add_argument("--site-id", required=True)

    purge_expired = commands.add_parser("purge-expired")
    purge_expired.add_argument("--request-id", required=True)

    try:
        parsed = parser.parse_args(arguments)
        request_id = UUID(parsed.request_id)
        working_root = root or Path("/srv/site-manager")
        registry = Registry(working_root)
        publisher = Publisher(working_root, os.environ.get("SITE_MANAGER_PUBLIC_BASE_URL", "http://127.0.0.1/s/"))
        lifecycle = LifecycleService(working_root)
        if parsed.command == "status":
            disk = shutil.disk_usage(working_root)
            data: dict[str, Any] = {
                "serverTime": _utc_now(),
                "disk": {
                    "totalBytes": disk.total,
                    "freeBytes": disk.free,
                },
            }
        elif parsed.command == "list":
            data = {"sites": [site.to_dict() for site in registry.list(parsed.status)]}
        elif parsed.command == "prepare":
            session = publisher.prepare(
                request_id,
                parsed.mode,
                UUID(parsed.site_id) if parsed.site_id else None,
                parsed.size,
                parsed.sha256,
            )
            data = {
                "uploadId": str(session.upload_id),
                "remotePath": str(session.payload_path),
                "resumeOffset": session.payload_path.stat().st_size if session.payload_path.exists() else 0,
                "expiresAt": session.expires_at.isoformat().replace("+00:00", "Z"),
            }
        else:
            if parsed.command == "publish":
                data = publisher.publish(
                    UUID(parsed.upload_id),
                    _decode_text(parsed.name_b64),
                    _decode_text(parsed.note_b64),
                ).to_dict()
            elif parsed.command == "trash":
                data = lifecycle.trash(UUID(parsed.site_id)).to_dict()
            elif parsed.command == "restore":
                data = lifecycle.restore(UUID(parsed.site_id)).to_dict()
            elif parsed.command == "purge":
                data = {"siteId": str(lifecycle.purge(UUID(parsed.site_id)))}
            else:
                data = {"purgedSiteIds": [str(site_id) for site_id in lifecycle.purge_expired()]}

        _write_envelope(request_id, ok=True, data=data)
        return 0
    except InsufficientSpaceError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "INSUFFICIENT_SPACE", "message": str(error), "retryable": False})
        return 2
    except PayloadSizeMismatchError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "SIZE_MISMATCH", "message": str(error), "retryable": True})
        return 2
    except PayloadHashMismatchError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "HASH_MISMATCH", "message": str(error), "retryable": True})
        return 2
    except IndexMissingError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "INDEX_MISSING", "message": str(error), "retryable": False})
        return 2
    except SlugConflictError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "SLUG_CONFLICT", "message": str(error), "retryable": False})
        return 2
    except ArchiveSafetyError as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "ARCHIVE_UNSAFE", "message": str(error), "retryable": False})
        return 2
    except (ValueError, OSError) as error:
        request_id_text = getattr(locals().get("parsed", None), "request_id", "00000000-0000-0000-0000-000000000000")
        _write_envelope(request_id_text, ok=False, error={"code": "INVALID_ARGUMENT", "message": str(error), "retryable": False})
        return 2


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def _decode_text(encoded_text: str) -> str:
    padding = "=" * ((4 - len(encoded_text) % 4) % 4)
    encoded = encoded_text.replace("-", "+").replace("_", "/") + padding
    return base64.b64decode(encoded, validate=True).decode("utf-8", errors="strict")


def _write_envelope(request_id: UUID | str, *, ok: bool, data: dict[str, Any] | None = None, error: dict[str, Any] | None = None) -> None:
    envelope: dict[str, Any] = {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": ok,
        "requestId": str(request_id),
    }
    if ok:
        envelope["data"] = data or {}
    else:
        envelope["error"] = error
    print(json.dumps(envelope, ensure_ascii=False, separators=(",", ":")))


if __name__ == "__main__":
    raise SystemExit(main())
