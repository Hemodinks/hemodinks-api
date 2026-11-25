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
        self.values["AzureStorage__ConnectionString"] = (
            "DefaultEndpointsProtocol=https;AccountName=stghemodinksprod01;AccountKey=storage-secret;EndpointSuffix=core.windows.net")
        self.values["AzureStorage__ContainerName"] = "profile-photos-confirmation"
        self.values["AzureStorage__PatientFilesContainerName"] = "patient-files-confirmation"

    def check(self):
        m.validate(self.service, [{"envVar": {"key": k, "value": v}} for k, v in self.values.items()], "expected",
                   "secret-value", "stghemodinksprod01")

    def test_valid(self):
        self.check()

    def test_each_missing_or_enabled_flag_is_named(self):
        for key in ["Database__RunMigrationsOnStartup", "Database__RunMaintenanceOnStartup", "Seed__CbhpmOnStartup", "Seed__UsersOnStartup"]:
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

    def test_storage_missing_or_empty_blocks_without_disk_fallback(self):
        for value in (None, "", "   "):
            with self.subTest(value=value):
                if value is None:
                    del self.values["AzureStorage__ConnectionString"]
                else:
                    self.values["AzureStorage__ConnectionString"] = value
                with self.assertRaisesRegex(m.PreflightError, "disco efemero"):
                    self.check()

    def test_storage_other_account_blocked_without_disclosing_key(self):
        self.values["AzureStorage__ConnectionString"] = "AccountName=outraconta;AccountKey=storage-secret"
        with self.assertRaises(m.PreflightError) as error:
            self.check()
        self.assertIn("stghemodinksprod01", str(error.exception))
        self.assertNotIn("storage-secret", str(error.exception))
        self.assertNotIn("outraconta", str(error.exception))

    def test_storage_account_from_blob_endpoint_and_case(self):
        self.values["AzureStorage__ConnectionString"] = (
            "BlobEndpoint=https://STGHEMODINKSPROD01.blob.core.windows.net/;SharedAccessSignature=sv=2024&sig=secret")
        self.check()

    def test_production_containers_blocked(self):
        for key, production in [("AzureStorage__ContainerName", "profile-photos"),
                                ("AzureStorage__PatientFilesContainerName", "patient-files")]:
            with self.subTest(key=key):
                old = self.values[key]; self.values[key] = production
                with self.assertRaisesRegex(m.PreflightError, key):
                    self.check()
                self.values[key] = old

if __name__ == "__main__": unittest.main()


class NewRelicReportTests(unittest.TestCase):
    def test_report_never_prints_license_key_and_flags_drift(self):
        ch = m
        values = dict(ch.NEW_RELIC_EXPECTED)
        values["CORECLR_ENABLE_PROFILING"] = "0"
        values["NEW_RELIC_LICENSE_KEY"] = "segredo-de-licenca-123"
        values["NEW_RELIC_ENABLED"] = "false"
        values["ConnectionStrings__DefaultConnection"] = "Server=x;Password=y"
        service = {"serviceDetails": {"plan": "free", "region": "virginia", "numInstances": 1}}
        text = "\n".join(ch.newrelic_report(service, values))
        self.assertNotIn("segredo-de-licenca-123", text)
        self.assertNotIn("Password", text)
        self.assertIn("NEW_RELIC_LICENSE_KEY: definida", text)
        self.assertIn("CORECLR_ENABLE_PROFILING: DIFERENTE (0)", text)
        self.assertIn("NEW_RELIC_APP_NAME: ok", text)
        self.assertIn("Outras variaveis do agente (so nomes): NEW_RELIC_ENABLED", text)
        self.assertIn("plano=free", text)

    def test_report_marks_missing_values(self):
        ch = m
        text = "\n".join(ch.newrelic_report({}, {}))
        self.assertIn("NEW_RELIC_LICENSE_KEY: AUSENTE", text)
        self.assertIn("CORECLR_ENABLE_PROFILING: AUSENTE", text)
        self.assertIn("nenhuma", text)
