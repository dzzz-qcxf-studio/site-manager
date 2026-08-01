from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path
from uuid import UUID

from .models import SiteManifest


class Registry:
    def __init__(self, root: Path) -> None:
        self._root = root
        self._sites_directory = root / "registry" / "sites"

    def save(self, manifest: SiteManifest) -> None:
        manifest.validate()
        self._sites_directory.mkdir(parents=True, exist_ok=True)
        destination = self._manifest_path(manifest.id)
        temporary_path: Path | None = None

        try:
            with tempfile.NamedTemporaryFile(
                mode="w",
                encoding="utf-8",
                dir=self._sites_directory,
                prefix=f".{manifest.id}.",
                suffix=".tmp",
                delete=False,
            ) as temporary_file:
                temporary_path = Path(temporary_file.name)
                json.dump(manifest.to_dict(), temporary_file, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
                temporary_file.flush()
                os.fsync(temporary_file.fileno())

            os.replace(temporary_path, destination)
        finally:
            if temporary_path is not None and temporary_path.exists():
                temporary_path.unlink()

    def get(self, site_id: UUID) -> SiteManifest | None:
        path = self._manifest_path(site_id)
        if not path.is_file():
            return None
        return self._load(path)

    def list(self, status: str) -> list[SiteManifest]:
        if status not in {"live", "trash", "all"}:
            raise ValueError("Status filter must be live, trash or all.")
        if not self._sites_directory.exists():
            return []

        sites: list[SiteManifest] = []
        for path in sorted(self._sites_directory.glob("*.json")):
            try:
                manifest = self._load(path)
            except (OSError, ValueError, json.JSONDecodeError) as error:
                print(f"Ignoring invalid manifest {path.name}: {error}", file=sys.stderr)
                continue
            if status == "all" or manifest.status == status:
                sites.append(manifest)

        return sorted(sites, key=lambda site: site.updated_at, reverse=True)

    def delete(self, site_id: UUID) -> None:
        self._manifest_path(site_id).unlink(missing_ok=True)

    def _manifest_path(self, site_id: UUID) -> Path:
        return self._sites_directory / f"{site_id}.json"

    @staticmethod
    def _load(path: Path) -> SiteManifest:
        with path.open("r", encoding="utf-8") as manifest_file:
            return SiteManifest.from_dict(json.load(manifest_file))
