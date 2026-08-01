from __future__ import annotations

import hashlib
import json
import os
import secrets
import shutil
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable
from uuid import UUID, uuid4

from .archive import extract_safe_archive
from .locks import SiteLock
from .models import SiteManifest
from .registry import Registry


class InsufficientSpaceError(ValueError):
    pass


class PayloadSizeMismatchError(ValueError):
    pass


class PayloadHashMismatchError(ValueError):
    pass


@dataclass(frozen=True)
class PrepareSession:
    upload_id: UUID
    request_id: UUID
    mode: str
    site_id: UUID | None
    expected_size: int
    expected_sha256: str
    expires_at: datetime
    directory: Path

    @property
    def payload_path(self) -> Path:
        return self.directory / "payload.tar.gz.partial"

    def to_dict(self) -> dict[str, str | int | None]:
        return {
            "uploadId": str(self.upload_id),
            "requestId": str(self.request_id),
            "mode": self.mode,
            "siteId": str(self.site_id) if self.site_id else None,
            "expectedSize": self.expected_size,
            "expectedSha256": self.expected_sha256,
            "expiresAt": self.expires_at.isoformat().replace("+00:00", "Z"),
        }

    @classmethod
    def from_dict(cls, value: dict[str, object], directory: Path) -> "PrepareSession":
        return cls(
            upload_id=UUID(str(value["uploadId"])),
            request_id=UUID(str(value["requestId"])),
            mode=str(value["mode"]),
            site_id=UUID(str(value["siteId"])) if value.get("siteId") else None,
            expected_size=int(value["expectedSize"]),
            expected_sha256=str(value["expectedSha256"]),
            expires_at=datetime.fromisoformat(str(value["expiresAt"]).replace("Z", "+00:00")),
            directory=directory,
        )


class Publisher:
    _slug_alphabet = "abcdefghjkmnpqrstuvwxyz23456789"
    _safety_margin_bytes = 512 * 1024 * 1024

    def __init__(self, root: Path, public_base_url: str, *, clock: Callable[[], datetime] | None = None) -> None:
        self._root = root
        self._public_base_url = public_base_url.rstrip("/") + "/"
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._registry = Registry(root)

    def prepare(self, request_id: UUID, mode: str, site_id: UUID | None, size: int, sha256: str) -> PrepareSession:
        self._validate_prepare_arguments(mode, site_id, size, sha256)
        self._root.mkdir(parents=True, exist_ok=True)
        existing = self._find_session_by_request_id(request_id)
        if existing is not None:
            if (existing.mode, existing.site_id, existing.expected_size, existing.expected_sha256) != (mode, site_id, size, sha256):
                raise ValueError("Request ID is already associated with different upload parameters.")
            return existing

        required_bytes = size * 2 + self._safety_margin_bytes
        if shutil.disk_usage(self._root)[2] < required_bytes:
            raise InsufficientSpaceError("Insufficient disk space for upload and extraction.")

        upload_id = uuid4()
        directory = self._root / "staging" / str(upload_id)
        directory.mkdir(parents=True)
        session = PrepareSession(
            upload_id=upload_id,
            request_id=request_id,
            mode=mode,
            site_id=site_id,
            expected_size=size,
            expected_sha256=sha256,
            expires_at=self._now() + timedelta(hours=24),
            directory=directory,
        )
        self._write_session(session)
        return session

    def publish(self, upload_id: UUID, name: str, note: str) -> SiteManifest:
        session = self._load_session(upload_id)
        if session.expires_at < self._now():
            raise ValueError("Upload session has expired.")
        if not session.payload_path.is_file():
            raise ValueError("Upload payload is missing.")
        if session.payload_path.stat().st_size != session.expected_size:
            raise PayloadSizeMismatchError("Upload payload size does not match the prepared session.")
        if _sha256_file(session.payload_path) != session.expected_sha256:
            raise PayloadHashMismatchError("Upload payload SHA-256 does not match the prepared session.")

        current = self._registry.get(session.site_id) if session.site_id else None
        if session.mode == "update" and current is None:
            raise ValueError("Site to update does not exist.")
        site_id = current.id if current else uuid4()

        with SiteLock(self._root / "locks", str(site_id)):
            return self._publish_locked(session, site_id, current, name, note)

    def _publish_locked(self, session: PrepareSession, site_id: UUID, current: SiteManifest | None, name: str, note: str) -> SiteManifest:
        version = current.version + 1 if current else 1
        slug = current.slug if current else self._new_slug()
        versions_directory = self._root / "versions" / str(site_id)
        versions_directory.mkdir(parents=True, exist_ok=True)
        temporary_version = versions_directory / f".v{version}.{session.upload_id}.tmp"
        final_version = versions_directory / f"v{version}"
        if final_version.exists():
            raise ValueError("Target site version already exists.")

        extracted_size = extract_safe_archive(session.payload_path, temporary_version)
        version_created = False
        link_switched = False
        temporary_link: Path | None = None
        live_link = self._root / "live" / slug
        old_target: str | None = None
        try:
            os.replace(temporary_version, final_version)
            version_created = True
            live_link.parent.mkdir(parents=True, exist_ok=True)
            if live_link.exists() or live_link.is_symlink():
                if not live_link.is_symlink():
                    raise ValueError("Live site path is not a controlled symbolic link.")
                old_target = os.readlink(live_link)

            temporary_link = live_link.parent / f".{slug}.{session.upload_id}.tmp"
            os.symlink(Path("..") / "versions" / str(site_id) / f"v{version}", temporary_link, target_is_directory=True)
            os.replace(temporary_link, live_link)
            link_switched = True

            now = self._now()
            manifest = SiteManifest(
                id=site_id,
                name=name,
                note=note,
                slug=slug,
                url=f"{self._public_base_url}{slug}/",
                status="live",
                version=version,
                size_bytes=extracted_size,
                content_sha256=session.expected_sha256,
                created_at=current.created_at if current else now,
                updated_at=now,
                trashed_at=None,
                purge_at=None,
            )
            self._registry.save(manifest)
            shutil.rmtree(session.directory, ignore_errors=True)
            return manifest
        except Exception:
            if temporary_link is not None:
                temporary_link.unlink(missing_ok=True)
            if link_switched:
                self._restore_live_link(live_link, old_target, site_id, session.upload_id)
            if version_created:
                shutil.rmtree(final_version, ignore_errors=True)
            raise

    def _restore_live_link(self, live_link: Path, old_target: str | None, site_id: UUID, upload_id: UUID) -> None:
        if old_target is None:
            live_link.unlink(missing_ok=True)
            return
        replacement = live_link.parent / f".{live_link.name}.restore.{upload_id}.tmp"
        os.symlink(old_target, replacement, target_is_directory=True)
        os.replace(replacement, live_link)

    def _find_session_by_request_id(self, request_id: UUID) -> PrepareSession | None:
        staging = self._root / "staging"
        if not staging.exists():
            return None
        for session_path in staging.glob("*/session.json"):
            session = self._read_session(session_path)
            if session.request_id == request_id:
                return session
        return None

    def _load_session(self, upload_id: UUID) -> PrepareSession:
        path = self._root / "staging" / str(upload_id) / "session.json"
        if not path.is_file():
            raise ValueError("Upload session does not exist.")
        return self._read_session(path)

    @staticmethod
    def _read_session(path: Path) -> PrepareSession:
        with path.open("r", encoding="utf-8") as file:
            return PrepareSession.from_dict(json.load(file), path.parent)

    @staticmethod
    def _write_session(session: PrepareSession) -> None:
        path = session.directory / "session.json"
        with path.open("x", encoding="utf-8") as file:
            json.dump(session.to_dict(), file, ensure_ascii=False, separators=(",", ":"))
            file.flush()
            os.fsync(file.fileno())

    @staticmethod
    def _validate_prepare_arguments(mode: str, site_id: UUID | None, size: int, sha256: str) -> None:
        if mode not in {"create", "update"} or (mode == "create" and site_id is not None) or (mode == "update" and site_id is None):
            raise ValueError("Upload mode and site ID are invalid.")
        if size < 0:
            raise ValueError("Upload size must not be negative.")
        if len(sha256) != 64 or any(character not in "0123456789abcdef" for character in sha256):
            raise ValueError("Upload hash must be lowercase SHA-256.")

    def _new_slug(self) -> str:
        live_directory = self._root / "live"
        while True:
            slug = "".join(secrets.choice(self._slug_alphabet) for _ in range(8))
            path = live_directory / slug
            if not path.exists() and not path.is_symlink():
                return slug

    def _now(self) -> datetime:
        value = self._clock()
        if value.tzinfo is None:
            raise ValueError("Clock must return a timezone-aware datetime.")
        return value.astimezone(timezone.utc)


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
