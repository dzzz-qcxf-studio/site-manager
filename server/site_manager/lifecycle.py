from __future__ import annotations

import os
import shutil
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable
from uuid import UUID

from .locks import SiteLock
from .models import SiteManifest
from .registry import Registry


class SlugConflictError(ValueError):
    pass


class LifecycleService:
    def __init__(self, root: Path, *, clock: Callable[[], datetime] | None = None, retention_days: int = 30) -> None:
        if retention_days < 1:
            raise ValueError("Trash retention must be positive.")
        self._root = root
        self._registry = Registry(root)
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._retention_days = retention_days

    def trash(self, site_id: UUID) -> SiteManifest:
        current = self._require_site(site_id)
        if current.status == "trash":
            return current

        with SiteLock(self._root / "locks", str(site_id)):
            current = self._require_site(site_id)
            if current.status == "trash":
                return current
            live_link = self._root / "live" / current.slug
            if not live_link.is_symlink():
                raise ValueError("Live site link is missing or unsafe.")
            old_target = os.readlink(live_link)
            source = self._root / "versions" / str(site_id)
            destination = self._root / "trash" / str(site_id)
            if not source.is_dir() or destination.exists():
                raise ValueError("Site version directory is not in a recoverable state.")

            live_link.unlink()
            try:
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(source), str(destination))
                now = self._now()
                trashed = SiteManifest(
                    **{**current.__dict__, "status": "trash", "updated_at": now, "trashed_at": now, "purge_at": now + timedelta(days=self._retention_days)}
                )
                self._registry.save(trashed)
                return trashed
            except Exception:
                if destination.exists() and not source.exists():
                    shutil.move(str(destination), str(source))
                self._replace_link(live_link, old_target, site_id)
                raise

    def restore(self, site_id: UUID) -> SiteManifest:
        current = self._require_site(site_id)
        if current.status == "live":
            return current

        with SiteLock(self._root / "locks", str(site_id)):
            current = self._require_site(site_id)
            if current.status == "live":
                return current
            live_link = self._root / "live" / current.slug
            if live_link.exists() or live_link.is_symlink():
                raise SlugConflictError("The original site slug is already in use.")
            source = self._root / "trash" / str(site_id)
            destination = self._root / "versions" / str(site_id)
            if not source.is_dir() or destination.exists():
                raise ValueError("Trashed site directory is not in a recoverable state.")

            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(str(source), str(destination))
            try:
                self._replace_link(live_link, self._link_target(site_id, current.version), site_id)
                now = self._now()
                restored = SiteManifest(
                    **{**current.__dict__, "status": "live", "updated_at": now, "trashed_at": None, "purge_at": None}
                )
                self._registry.save(restored)
                return restored
            except Exception:
                live_link.unlink(missing_ok=True)
                if destination.exists() and not source.exists():
                    shutil.move(str(destination), str(source))
                raise

    def purge(self, site_id: UUID) -> UUID | None:
        current = self._registry.get(site_id)
        if current is None:
            return None
        if current.status != "trash":
            raise ValueError("Only trashed sites can be permanently removed.")

        with SiteLock(self._root / "locks", str(site_id)):
            current = self._registry.get(site_id)
            if current is None:
                return None
            if current.status != "trash":
                raise ValueError("Only trashed sites can be permanently removed.")
            directory = self._root / "trash" / str(site_id)
            if directory.exists():
                shutil.rmtree(directory)
            self._registry.delete(site_id)
            return site_id

    def purge_expired(self) -> list[UUID]:
        now = self._now()
        return [
            site.id
            for site in self._registry.list("trash")
            if site.purge_at is not None and site.purge_at <= now and self.purge(site.id) is not None
        ]

    def _require_site(self, site_id: UUID) -> SiteManifest:
        site = self._registry.get(site_id)
        if site is None:
            raise ValueError("Site does not exist.")
        return site

    def _replace_link(self, live_link: Path, target: str, site_id: UUID) -> None:
        live_link.parent.mkdir(parents=True, exist_ok=True)
        temporary = live_link.parent / f".{live_link.name}.{site_id}.tmp"
        os.symlink(target, temporary, target_is_directory=True)
        os.replace(temporary, live_link)

    @staticmethod
    def _link_target(site_id: UUID, version: int) -> str:
        return str(Path("..") / "versions" / str(site_id) / f"v{version}")

    def _now(self) -> datetime:
        value = self._clock()
        if value.tzinfo is None:
            raise ValueError("Clock must return a timezone-aware datetime.")
        return value.astimezone(timezone.utc)
