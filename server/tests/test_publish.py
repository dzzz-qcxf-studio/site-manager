import hashlib
import io
import os
import tarfile
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch
from uuid import UUID

from server.site_manager.publisher import InsufficientSpaceError, Publisher


class PublisherTests(unittest.TestCase):
    request_id = UUID("0191f7d0-0000-7000-8000-000000000001")

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.publisher = Publisher(
            self.root,
            "http://127.0.0.1/s/",
            clock=lambda: datetime(2026, 7, 31, 12, tzinfo=timezone.utc),
        )

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_prepare_is_idempotent_for_request_id(self) -> None:
        first = self.publisher.prepare(self.request_id, "create", None, 100, "a" * 64)
        second = self.publisher.prepare(self.request_id, "create", None, 100, "a" * 64)

        self.assertEqual(first, second)
        self.assertTrue(first.payload_path.is_file() is False)

    def test_prepare_rejects_insufficient_space(self) -> None:
        with patch("server.site_manager.publisher.shutil.disk_usage", return_value=(1, 1, 1)):
            with self.assertRaises(InsufficientSpaceError):
                self.publisher.prepare(self.request_id, "create", None, 100, "a" * 64)

    def test_publish_rejects_hash_mismatch_without_touching_live(self) -> None:
        session = self.publisher.prepare(self.request_id, "create", None, 1, "a" * 64)
        session.payload_path.write_bytes(b"x")

        with self.assertRaises(ValueError):
            self.publisher.publish(session.upload_id, "name", "note")

        self.assertEqual([], list((self.root / "live").glob("*")) if (self.root / "live").exists() else [])

    @unittest.skipIf(os.name == "nt", "Atomic directory symlinks are verified on the Ubuntu deployment target.")
    def test_update_switches_live_symlink_and_increments_version(self) -> None:
        first = self.publish_archive(self.request_id, {"index.html": b"one"})
        second_request = UUID("0191f7d0-0000-7000-8000-000000000002")
        second = self.publish_archive(second_request, {"index.html": b"two"}, site_id=first.id)

        live = self.root / "live" / first.slug
        self.assertEqual(2, second.version)
        self.assertEqual(first.slug, second.slug)
        self.assertEqual(b"two", (live / "index.html").read_bytes())
        self.assertTrue((self.root / "versions" / str(first.id) / "v1").is_dir())

    @unittest.skipIf(os.name == "nt", "Atomic directory symlinks are verified on the Ubuntu deployment target.")
    def test_failed_update_keeps_previous_live_target(self) -> None:
        first = self.publish_archive(self.request_id, {"index.html": b"one"})
        session = self.publisher.prepare(
            UUID("0191f7d0-0000-7000-8000-000000000003"),
            "update",
            first.id,
            1,
            "a" * 64,
        )
        session.payload_path.write_bytes(b"not an archive")

        with self.assertRaises(ValueError):
            self.publisher.publish(session.upload_id, "name", "note")

        self.assertEqual(b"one", (self.root / "live" / first.slug / "index.html").read_bytes())

    def publish_archive(self, request_id: UUID, entries: dict[str, bytes], site_id=None):
        archive = self.root / f"{request_id}.tar.gz"
        with tarfile.open(archive, "w:gz") as payload:
            for name, content in entries.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                payload.addfile(entry, io.BytesIO(content))
        contents = archive.read_bytes()
        session = self.publisher.prepare(request_id, "update" if site_id else "create", site_id, len(contents), hashlib.sha256(contents).hexdigest())
        session.payload_path.write_bytes(contents)
        return self.publisher.publish(session.upload_id, "name", "note")
