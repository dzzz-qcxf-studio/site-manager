from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any
from uuid import UUID


def _parse_timestamp(value: str | None) -> datetime | None:
    if value is None:
        return None
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("Timestamp must include a timezone.")
    return parsed.astimezone(timezone.utc)


def _format_timestamp(value: datetime | None) -> str | None:
    if value is None:
        return None
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


@dataclass(frozen=True)
class SiteManifest:
    id: UUID
    name: str
    note: str
    slug: str
    url: str
    status: str
    version: int
    size_bytes: int
    content_sha256: str
    created_at: datetime
    updated_at: datetime
    trashed_at: datetime | None
    purge_at: datetime | None

    def to_dict(self) -> dict[str, Any]:
        return {
            "schemaVersion": 1,
            "id": str(self.id),
            "name": self.name,
            "note": self.note,
            "slug": self.slug,
            "url": self.url,
            "status": self.status,
            "version": self.version,
            "sizeBytes": self.size_bytes,
            "contentSha256": self.content_sha256,
            "createdAt": _format_timestamp(self.created_at),
            "updatedAt": _format_timestamp(self.updated_at),
            "trashedAt": _format_timestamp(self.trashed_at),
            "purgeAt": _format_timestamp(self.purge_at),
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "SiteManifest":
        if data.get("schemaVersion") != 1:
            raise ValueError("Unsupported manifest schema version.")

        manifest = cls(
            id=UUID(data["id"]),
            name=data["name"],
            note=data["note"],
            slug=data["slug"],
            url=data["url"],
            status=data["status"],
            version=data["version"],
            size_bytes=data["sizeBytes"],
            content_sha256=data["contentSha256"],
            created_at=_parse_timestamp(data["createdAt"]),
            updated_at=_parse_timestamp(data["updatedAt"]),
            trashed_at=_parse_timestamp(data.get("trashedAt")),
            purge_at=_parse_timestamp(data.get("purgeAt")),
        )
        manifest.validate()
        return manifest

    def validate(self) -> None:
        if self.status not in {"live", "trash"}:
            raise ValueError("Manifest status must be live or trash.")
        if not self.name or not self.slug or not self.url:
            raise ValueError("Manifest name, slug and URL must not be empty.")
        if self.version < 1 or self.size_bytes < 0:
            raise ValueError("Manifest version and size must be non-negative.")
        if len(self.content_sha256) != 64 or any(character not in "0123456789abcdef" for character in self.content_sha256):
            raise ValueError("Manifest content hash must be lowercase SHA-256.")
        if self.created_at.tzinfo is None or self.updated_at.tzinfo is None:
            raise ValueError("Manifest timestamps must include timezones.")
