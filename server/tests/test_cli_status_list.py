import io
import json
import os
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch
from uuid import UUID

from server.site_manager.cli import main
from server.site_manager.models import SiteManifest
from server.site_manager.registry import Registry


class CliStatusListTests(unittest.TestCase):
    request_id = "0191f7d0-0000-7000-8000-000000000001"

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_status_returns_protocol_version_disk_and_server_time(self) -> None:
        response = self.run_cli("status", "--request-id", self.request_id)

        self.assertTrue(response["ok"])
        self.assertEqual(1, response["protocolVersion"])
        self.assertEqual(self.request_id, response["requestId"])
        self.assertIn("serverTime", response["data"])
        self.assertGreater(response["data"]["disk"]["freeBytes"], 0)

    def test_list_returns_registry_sites(self) -> None:
        site = SiteManifest(
            id=UUID("0191f7d0-0000-7000-8000-000000000100"),
            name="产品模型演示",
            note="",
            slug="a8k3m2",
            url="http://127.0.0.1/s/a8k3m2/",
            status="live",
            version=1,
            size_bytes=10,
            content_sha256="a" * 64,
            created_at=datetime(2026, 7, 31, 12, tzinfo=timezone.utc),
            updated_at=datetime(2026, 7, 31, 12, tzinfo=timezone.utc),
            trashed_at=None,
            purge_at=None,
        )
        Registry(self.root).save(site)

        response = self.run_cli("list", "--request-id", self.request_id, "--status", "live")

        self.assertTrue(response["ok"])
        self.assertEqual([site.to_dict()], response["data"]["sites"])

    def test_prepare_returns_idempotent_upload_session(self) -> None:
        arguments = (
            "prepare",
            "--request-id",
            self.request_id,
            "--mode",
            "create",
            "--size",
            "123",
            "--sha256",
            "a" * 64,
        )

        first = self.run_cli(*arguments)
        second = self.run_cli(*arguments)

        self.assertTrue(first["ok"])
        self.assertEqual(first["data"]["uploadId"], second["data"]["uploadId"])
        self.assertEqual(0, first["data"]["resumeOffset"])
        self.assertTrue(first["data"]["remotePath"].endswith("payload.tar.gz.partial"))

    def test_cli_reads_public_base_url_from_environment(self) -> None:
        output = io.StringIO()
        with patch.dict(os.environ, {"SITE_MANAGER_PUBLIC_BASE_URL": "http://47.86.89.203/s/"}), \
             patch("server.site_manager.cli.Publisher") as publisher_factory, \
             redirect_stdout(output):
            exit_code = main(["status", "--request-id", self.request_id], root=self.root)

        self.assertEqual(0, exit_code)
        publisher_factory.assert_called_once_with(self.root, "http://47.86.89.203/s/")

    def run_cli(self, *arguments: str) -> dict:
        output = io.StringIO()
        with redirect_stdout(output):
            exit_code = main([*arguments], root=self.root)

        self.assertEqual(0, exit_code)
        lines = output.getvalue().splitlines()
        self.assertEqual(1, len(lines))
        return json.loads(lines[0])
