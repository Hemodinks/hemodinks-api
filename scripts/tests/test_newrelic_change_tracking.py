"""Marcador de deploy no Change Tracking do New Relic: nunca bloqueia o deploy, nunca vaza a chave."""
import contextlib
import importlib.util
import io
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location("ct", ROOT / "scripts/newrelic_change_tracking.py")
ct = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ct)
nr = ct.nr

SHA = "0123456789abcdef0123456789abcdef01234567"
ENV = {"NEW_RELIC_API_KEY": "NRAK-SECRET", "NEW_RELIC_ACCOUNT_ID": "1234567", "GITHUB_SHA": SHA,
       "GITHUB_ACTOR": "deployer", "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "Hemodinks/hemodinks-api",
       "GITHUB_RUN_ID": "42", "CANDIDATE_REVISION": "hemodinks-api-prod--abc1234"}


class FakeNerdGraph:
    def __init__(self, entities=None, fail=None):
        self.calls, self.entities, self.fail = [], entities, fail

    def query(self, document, variables=None):
        self.calls.append((document, variables))
        if self.fail:
            raise RuntimeError(self.fail)
        if document is nr.Q_ENTITY:
            entities = self.entities if self.entities is not None else [{"guid": "GUID-API", "name": "Hemodinks API"}]
            return {"actor": {"entitySearch": {"results": {"entities": entities}}}}
        if document is ct.M_DEPLOYMENT_CREATE:
            return {"changeTrackingCreateDeployment": {"deploymentId": "d1", "entityGuid": variables["deployment"]["entityGuid"]}}
        raise AssertionError(document)


def run(env, client):
    out = io.StringIO()
    original = nr.NerdGraph
    nr.NerdGraph = lambda key, region: client
    try:
        with contextlib.redirect_stdout(out):
            code = ct.main(env)
    finally:
        nr.NerdGraph = original
    return code, out.getvalue()


class ChangeTrackingTests(unittest.TestCase):
    def test_records_blue_green_deployment_on_the_apm_entity(self):
        client = FakeNerdGraph()
        code, out = run(ENV, client)
        self.assertEqual(0, code)
        self.assertIn("deploymentId d1", out)
        query = client.calls[0][1]["query"]
        self.assertIn("name = 'Hemodinks API'", query)
        self.assertIn("domain = 'APM'", query)
        self.assertIn("accountId = 1234567", query)
        deployment = client.calls[1][1]["deployment"]
        self.assertEqual({"entityGuid": "GUID-API", "version": SHA[:7], "commit": SHA, "deploymentType": "BLUE_GREEN",
                          "description": "Producao: revisao hemodinks-api-prod--abc1234 promovida", "user": "deployer",
                          "deepLink": "https://github.com/Hemodinks/hemodinks-api/actions/runs/42"}, deployment)

    def test_missing_configuration_skips_without_calling_new_relic(self):
        for missing in ("NEW_RELIC_API_KEY", "NEW_RELIC_ACCOUNT_ID"):
            with self.subTest(missing=missing):
                client = FakeNerdGraph()
                code, out = run({**ENV, missing: ""}, client)
                self.assertEqual(0, code)
                self.assertIn("::notice::", out)
                self.assertEqual([], client.calls)

    def test_new_relic_failure_warns_but_never_fails_the_deploy(self):
        code, out = run(ENV, FakeNerdGraph(fail="NerdGraph HTTP 500"))
        self.assertEqual(0, code)
        self.assertIn("::warning::", out)

    def test_unknown_entity_does_not_record_on_another_app(self):
        client = FakeNerdGraph(entities=[{"guid": "OTHER", "name": "Hemodinks API Local"}])
        code, out = run(ENV, client)
        self.assertEqual(0, code)
        self.assertIn("nao encontrada", out)
        self.assertEqual(1, len(client.calls))

    def test_invalid_sha_is_not_recorded(self):
        client = FakeNerdGraph()
        code, out = run({**ENV, "GITHUB_SHA": "main"}, client)
        self.assertEqual(0, code)
        self.assertIn("GITHUB_SHA", out)
        self.assertEqual(1, len(client.calls))

    def test_api_key_never_reaches_output(self):
        for client in (FakeNerdGraph(), FakeNerdGraph(fail="boom"), FakeNerdGraph(entities=[])):
            _, out = run(ENV, client)
            self.assertNotIn("NRAK-SECRET", out)


class WorkflowWiringTests(unittest.TestCase):
    def test_deploy_api_records_marker_only_after_a_real_promotion_and_never_blocks(self):
        text = (ROOT / ".github/workflows/publish-container.yml").read_text(encoding="utf-8")
        body = re.search(r"^  deploy-api:\n(.*?)(?=^  [\w-]+:|\Z)", text, re.M | re.S).group(1)
        step = body.split("      - name: Record deployment in New Relic\n", 1)[1].split("      - name:", 1)[0]
        self.assertIn("steps.promotion.outputs.result == 'promoted'", step)
        self.assertIn("continue-on-error: true", step)
        self.assertIn("NEW_RELIC_API_KEY: ${{ secrets.NEW_RELIC_API_KEY }}", step)
        self.assertIn("run: python3 -B scripts/newrelic_change_tracking.py", step)
        self.assertLess(body.index("Promote candidate and verify Production traffic"),
                        body.index("Record deployment in New Relic"))


if __name__ == "__main__":
    unittest.main()
