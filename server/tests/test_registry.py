import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from uuid import UUID

from server.site_manager.models import SiteManifest
from server.site_manager.registry import Registry


class RegistryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.registry = Registry(self.root)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_registry_writes_one_atomic_json_file_per_site(self) -> None:
        site = self.create_site("2026-07-31T12:00:00Z")

        self.registry.save(site)

        manifest_path = self.root / "registry" / "sites" / f"{site.id}.json"
        self.assertTrue(manifest_path.is_file())
        self.assertEqual([], list(manifest_path.parent.glob("*.tmp")))
        loaded = self.registry.get(site.id)
        self.assertEqual(site, loaded)

    def test_list_returns_live_and_trash_sorted_by_updated_at(self) -> None:
        older = self.create_site("2026-07-31T12:00:00Z", site_id="0191f7d0-0000-7000-8000-000000000100")
        newer = self.create_site(
            "2026-07-31T13:00:00Z",
            site_id="0191f7d0-0000-7000-8000-000000000101",
            status="trash",
        )
        self.registry.save(older)
        self.registry.save(newer)

        self.assertEqual([older], self.registry.list("live"))
        self.assertEqual([newer], self.registry.list("trash"))
        self.assertEqual([newer, older], self.registry.list("all"))

    def test_invalid_manifest_does_not_hide_other_sites(self) -> None:
        site = self.create_site("2026-07-31T12:00:00Z")
        self.registry.save(site)
        broken = self.root / "registry" / "sites" / "broken.json"
        broken.write_text("not json", encoding="utf-8")

        self.assertEqual([site], self.registry.list("all"))

    @staticmethod
    def create_site(updated_at: str, site_id: str = "0191f7d0-0000-7000-8000-000000000100", status: str = "live") -> SiteManifest:
        timestamp = datetime.fromisoformat(updated_at.replace("Z", "+00:00"))
        return SiteManifest(
            id=UUID(site_id),
            name="产品模型演示",
            note="客户 A",
            slug="a8k3m2",
            url="http://127.0.0.1/s/a8k3m2/",
            status=status,
            version=1,
            size_bytes=10,
            content_sha256="a" * 64,
            created_at=timestamp,
            updated_at=timestamp,
            trashed_at=None,
            purge_at=None,
        )
