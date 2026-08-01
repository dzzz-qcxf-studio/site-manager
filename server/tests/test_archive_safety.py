import io
import tarfile
import tempfile
import unittest
from pathlib import Path

from server.site_manager.archive import ArchiveSafetyError, extract_safe_archive


class ArchiveSafetyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.archive = self.root / "payload.tar.gz"

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_rejects_parent_path_and_symlink_tar_entries(self) -> None:
        with tarfile.open(self.archive, "w:gz") as payload:
            parent = tarfile.TarInfo("../escape.txt")
            parent.size = 1
            payload.addfile(parent, io.BytesIO(b"x"))

        with self.assertRaises(ArchiveSafetyError):
            extract_safe_archive(self.archive, self.root / "destination")

        with tarfile.open(self.archive, "w:gz") as payload:
            link = tarfile.TarInfo("assets/link")
            link.type = tarfile.SYMTYPE
            link.linkname = "/etc/passwd"
            payload.addfile(link)

        with self.assertRaises(ArchiveSafetyError):
            extract_safe_archive(self.archive, self.root / "destination")

    def test_requires_root_index_html(self) -> None:
        self.write_archive({"assets/a.js": b"1"})

        with self.assertRaises(ArchiveSafetyError):
            extract_safe_archive(self.archive, self.root / "destination")

    def write_archive(self, entries: dict[str, bytes]) -> None:
        with tarfile.open(self.archive, "w:gz") as payload:
            for name, content in entries.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                payload.addfile(entry, io.BytesIO(content))
