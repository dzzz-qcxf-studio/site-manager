import hashlib
import io
import os
import tarfile
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from uuid import UUID

from server.site_manager.lifecycle import LifecycleService
from server.site_manager.publisher import Publisher


@unittest.skipIf(os.name == "nt", "Lifecycle symlink behavior is verified on the Ubuntu deployment target.")
class LifecycleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.now = datetime(2026, 7, 31, 12, tzinfo=timezone.utc)
        self.publisher = Publisher(self.root, "http://127.0.0.1/s/", clock=lambda: self.now)
        self.lifecycle = LifecycleService(self.root, clock=lambda: self.now)
        self.site = self.publish_site()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_trash_removes_live_and_sets_purge_at_30_days(self) -> None:
        trashed = self.lifecycle.trash(self.site.id)

        self.assertEqual("trash", trashed.status)
        self.assertEqual(datetime(2026, 8, 30, 12, tzinfo=timezone.utc), trashed.purge_at)
        self.assertFalse((self.root / "live" / self.site.slug).exists())
        self.assertTrue((self.root / "trash" / str(self.site.id)).is_dir())

    def test_restore_recreates_same_slug_and_clears_trash_dates(self) -> None:
        self.lifecycle.trash(self.site.id)

        restored = self.lifecycle.restore(self.site.id)

        self.assertEqual("live", restored.status)
        self.assertEqual(self.site.slug, restored.slug)
        self.assertIsNone(restored.trashed_at)
        self.assertIsNone(restored.purge_at)
        self.assertEqual(b"ok", (self.root / "live" / self.site.slug / "index.html").read_bytes())

    def test_purge_expired_only_deletes_due_sites(self) -> None:
        self.lifecycle.trash(self.site.id)
        self.now = datetime(2026, 8, 31, 12, tzinfo=timezone.utc)

        purged = self.lifecycle.purge_expired()

        self.assertEqual([self.site.id], purged)
        self.assertFalse((self.root / "trash" / str(self.site.id)).exists())

    def test_trash_restore_and_purge_are_idempotent(self) -> None:
        first_trash = self.lifecycle.trash(self.site.id)
        self.assertEqual(first_trash, self.lifecycle.trash(self.site.id))
        first_restore = self.lifecycle.restore(self.site.id)
        self.assertEqual(first_restore, self.lifecycle.restore(self.site.id))
        self.lifecycle.trash(self.site.id)
        self.lifecycle.purge(self.site.id)
        self.assertIsNone(self.lifecycle.purge(self.site.id))

    def publish_site(self):
        request_id = UUID("0191f7d0-0000-7000-8000-000000000001")
        archive = self.root / "payload.tar.gz"
        with tarfile.open(archive, "w:gz") as payload:
            entry = tarfile.TarInfo("index.html")
            entry.size = 2
            payload.addfile(entry, io.BytesIO(b"ok"))
        contents = archive.read_bytes()
        session = self.publisher.prepare(request_id, "create", None, len(contents), hashlib.sha256(contents).hexdigest())
        session.payload_path.write_bytes(contents)
        return self.publisher.publish(session.upload_id, "name", "note")
