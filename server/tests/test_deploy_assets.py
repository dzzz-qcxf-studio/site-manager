import unittest
from pathlib import Path


class DeploymentAssetTests(unittest.TestCase):
    repository_root = Path(__file__).resolve().parents[2]

    def test_nginx_location_is_static_and_blocks_dotfiles(self) -> None:
        contents = self.read("deploy/nginx/site-manager-location.conf")

        self.assertIn("location ^~ /s/", contents)
        self.assertIn("alias /srv/site-manager/live/;", contents)
        self.assertIn("index index.html;", contents)
        self.assertIn("autoindex off;", contents)
        self.assertIn("location ~ (^|/)\\.", contents)
        self.assertIn("location ~* \\.(?:php|py|sh)$", contents)
        self.assertIn("return 404;", contents)

    def test_nginx_declares_glb_gltf_wasm_mime_types(self) -> None:
        contents = self.read("deploy/nginx/site-manager-location.conf")

        self.assertIn("model/gltf-binary glb;", contents)
        self.assertIn("model/gltf+json gltf;", contents)
        self.assertIn("application/wasm wasm;", contents)
        self.assertIn("add_header Accept-Ranges bytes always;", contents)

    def test_isolated_http_server_only_exposes_site_manager_route(self) -> None:
        contents = self.read("deploy/nginx/site-manager-http-server.conf")

        self.assertIn("listen 80;", contents)
        self.assertIn("include /etc/nginx/snippets/site-manager-location.conf;", contents)
        self.assertIn("location / {", contents)
        self.assertIn("return 404;", contents)

    def test_isolated_web_service_uses_its_own_nginx_configuration(self) -> None:
        configuration = self.read("deploy/nginx/site-manager-nginx.conf")
        service = self.read("deploy/systemd/site-manager-web.service")
        installer = self.read("deploy/install-server.sh")

        self.assertIn("pid /run/site-manager-nginx.pid;", configuration)
        self.assertIn("include /etc/nginx/site-manager-http.d/*.conf;", configuration)
        self.assertIn("ExecStartPre=/usr/sbin/nginx -t -c /etc/nginx/site-manager-nginx.conf", service)
        self.assertIn("PIDFile=/run/site-manager-nginx.pid", service)
        self.assertIn("site-manager-nginx.conf", installer)
        self.assertIn("site-manager-web.service", installer)
        self.assertIn("nginx -t -c /etc/nginx/site-manager-nginx.conf", installer)
        self.assertNotIn("run systemctl enable --now site-manager-web", installer)

    def test_timer_runs_daily_and_is_persistent(self) -> None:
        service = self.read("deploy/systemd/site-manager-purge.service")
        timer = self.read("deploy/systemd/site-manager-purge.timer")

        self.assertIn("User=sitepublisher", service)
        self.assertIn("site-managerctl purge-expired --request-id", service)
        self.assertIn("OnCalendar=daily", timer)
        self.assertIn("Persistent=true", timer)

    def test_installer_has_dry_run_and_never_deletes_existing_web_root(self) -> None:
        contents = self.read("deploy/install-server.sh")

        self.assertIn("--dry-run", contents)
        self.assertIn("--apply", contents)
        self.assertIn("nginx -t", contents)
        self.assertNotIn("rm -rf", contents)
        self.assertNotIn("/var/www", contents)
        self.assertIn("SITE_MANAGER_PUBLIC_BASE_URL", contents)

    def test_installer_copies_package_contents_for_repeatable_installation(self) -> None:
        contents = self.read("deploy/install-server.sh")

        self.assertIn('"$SOURCE_DIR/server/site_manager/."', contents)
        self.assertIn('"$APPLICATION_ROOT/site_manager"', contents)

    def test_dry_run_does_not_query_an_account_it_only_plans_to_create(self) -> None:
        contents = self.read("deploy/install-server.sh")
        authorized_key_block = contents[contents.rindex('if [[ -n "$AUTHORIZED_KEY_FILE"'):]

        self.assertLess(authorized_key_block.index('if "$DRY_RUN"; then'), authorized_key_block.index('user_home="$(getent passwd'))

    def read(self, relative_path: str) -> str:
        return (self.repository_root / relative_path).read_text(encoding="utf-8")
