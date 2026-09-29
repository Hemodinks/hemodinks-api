import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("preflight", Path(__file__).parents[1] / "check_homologation.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class PreflightTests(unittest.TestCase):
    def setUp(self):
        self.service = {"serviceDetails": {"url": "https://expected"}, "branch": "developer", "autoDeploy": "no"}
        self.values = {key: "false" for key in ["Database__RunMigrationsOnStartup", "Database__RunMaintenanceOnStartup", "Seed__CbhpmOnStartup", "Seed__UsersOnStartup"]}
        self.values["ConnectionStrings__DefaultConnection"] = "secret-value"

    def check(self):
        m.validate(self.service, [{"envVar": {"key": k, "value": v}} for k, v in self.values.items()], "expected", "secret-value")

    def test_valid(self):
        self.check()

    def test_each_missing_or_enabled_flag_is_named(self):
        for key in list(self.values)[:-1]:
            with self.subTest(key=key):
                self.values[key] = "true"
                with self.assertRaisesRegex(m.PreflightError, key):
                    self.check()
                del self.values[key]
                with self.assertRaisesRegex(m.PreflightError, key):
                    self.check()
                self.values[key] = "false"

    def test_wrong_connection_not_disclosed(self):
        self.values["ConnectionStrings__DefaultConnection"] = "another-secret"
        with self.assertRaises(m.PreflightError) as error:
            self.check()
        self.assertNotIn("secret-value", str(error.exception))
        self.assertNotIn("another-secret", str(error.exception))

    def test_wrong_target_branch_and_autodeploy_blocked(self):
        for key,value in [("branch","main"),("autoDeploy","yes"),("serviceDetails",{"url":"https://production"})]:
            with self.subTest(key=key):
                old=self.service[key]; self.service[key]=value
                with self.assertRaises(m.PreflightError): self.check()
                self.service[key]=old

if __name__ == "__main__": unittest.main()
