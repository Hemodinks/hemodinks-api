"""Keep migration credentials out of runtime and read-only checks."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class SqlIdentitySeparationTests(unittest.TestCase):
    def test_only_bundle_steps_receive_migration_identity(self):
        cases = {
            "apply-production-migrations.yml": ("AZURE", {"Apply only known pending migrations to Azure SQL"}),
            "publish-container.yml": ("AZURE", {"Apply migration bundle"}),
        }
        for filename, (prefix, migration_steps) in cases.items():
            source = (ROOT / ".github/workflows" / filename).read_text(encoding="utf-8")
            found = set()
            for step in re.split(r"(?m)^      - name: ", source)[1:]:
                name = step.splitlines()[0]
                match = re.search(r"ConnectionStrings__DefaultConnection: \$\{\{ secrets\.(\w+) \}\}", step)
                if match:
                    expected = f"{prefix}_SQL_MIGRATION_CONNECTION_STRING" if name in migration_steps else f"{prefix}_SQL_RUNTIME_CONNECTION_STRING"
                    self.assertEqual(expected, match[1], f"{filename}: {name}")
                    found.add(name)
            self.assertTrue(migration_steps <= found)
            self.assertNotIn(f"secrets.{prefix}_SQL_CONNECTION_STRING", source)
            self.assertIn("--validate-connections", source)
            self.assertIn(f"HEMODINKS_SQL_MIGRATION_CONNECTION_STRING: ${{{{ secrets.{prefix}_SQL_MIGRATION_CONNECTION_STRING }}}}", source)

    def test_ci_runs_identity_checks(self):
        source = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_sql_identity_separation.py", source)

    def test_production_publish_rechecks_runtime_before_deploy(self):
        source = (ROOT / ".github/workflows/publish-container.yml").read_text(encoding="utf-8")
        self.assertLess(source.index("Apply migration bundle"), source.index("Verify SQL runtime after migration"))
        self.assertLess(source.index("Verify SQL runtime after migration"), source.index("  deploy-api:"))


if __name__ == "__main__":
    unittest.main()
