import unittest
from pathlib import Path


class ReleaseAssetTests(unittest.TestCase):
    repository_root = Path(__file__).resolve().parents[2]

    def test_publish_script_tests_before_creating_release_files(self) -> None:
        contents = self.read("scripts/publish-win-x64.ps1")

        self.assertIn("dotnet test -c Release", contents)
        self.assertIn("SiteManager.App.exe", contents)
        self.assertIn("settings.example.json", contents)
        self.assertLess(contents.index("dotnet test -c Release"), contents.index("New-Item -ItemType Directory"))

    def test_wpf_project_declares_self_contained_windows_release(self) -> None:
        contents = self.read("src/SiteManager.App/SiteManager.App.csproj")

        self.assertIn("<RuntimeIdentifier>win-x64</RuntimeIdentifier>", contents)
        self.assertIn("<SelfContained>true</SelfContained>", contents)
        self.assertIn("<PublishSingleFile>true</PublishSingleFile>", contents)
        self.assertIn("<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>", contents)
        self.assertIn("<PublishTrimmed>false</PublishTrimmed>", contents)

    def test_package_instructions_and_settings_template_are_present(self) -> None:
        readme = self.read("README.md")
        template = self.read("config/settings.example.json")

        self.assertIn("网页展台", readme)
        self.assertIn("设置", readme)
        self.assertIn('"schemaVersion": 1', template)
        self.assertNotIn("BEGIN OPENSSH PRIVATE KEY", template)

    def read(self, relative_path: str) -> str:
        return (self.repository_root / relative_path).read_text(encoding="utf-8")
