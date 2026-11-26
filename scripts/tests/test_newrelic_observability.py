"""Regras da configuracao versionada do New Relic e idempotencia do apply (#289)."""
import copy
import importlib.util
import os
from pathlib import Path
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("nr", ROOT / "scripts/newrelic_observability.py")
nr = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(nr)

ACCOUNT = 1234567
VARIABLES = {"APM_APP": "Hemodinks API", "BROWSER_APP": "Hemodinks Front"}


class FakeNerdGraph:
    """Estado em memoria; registra mutations para verificar plan/apply."""

    def __init__(self, existing=False):
        self.mutations = []
        self.existing = existing
        self.ids = 0

    def _id(self):
        self.ids += 1
        return str(self.ids)

    def query(self, document, variables=None):
        variables = variables or {}
        if document.lstrip().startswith("mutation"):
            self.mutations.append((document, variables))
        if document is nr.Q_POLICIES:
            policies = [{"id": "p1", "name": variables["name"]}] if self.existing else []
            return {"actor": {"account": {"alerts": {"policiesSearch": {"policies": policies}}}}}
        if document is nr.M_POLICY_CREATE:
            return {"alertsPolicyCreate": {"id": "p1", "name": variables["policy"]["name"]}}
        if document is nr.Q_CONDITIONS:
            names = [c["name"] for c in nr.load_config()["alerts"]["conditions"]] if self.existing else []
            return {"actor": {"account": {"alerts": {"nrqlConditionsSearch": {
                "nrqlConditions": [{"id": f"c{i}", "name": n} for i, n in enumerate(names)]}}}}}
        if document in (nr.M_CONDITION_CREATE, nr.M_CONDITION_UPDATE):
            return {}
        if document in (nr.Q_DESTINATIONS, nr.Q_CHANNELS):
            entities = [{"id": "d1", "name": variables["name"]}] if self.existing else []
            key = "destinations" if document is nr.Q_DESTINATIONS else "channels"
            return {"actor": {"account": {"aiNotifications": {key: {"entities": entities}}}}}
        if document is nr.M_DESTINATION_CREATE:
            return {"aiNotificationsCreateDestination": {"destination": {"id": "d1", "name": "x"}}}
        if document is nr.M_CHANNEL_CREATE:
            return {"aiNotificationsCreateChannel": {"channel": {"id": "ch1", "name": "x"}}}
        if document is nr.Q_WORKFLOWS:
            entities = [{"id": "w1", "name": variables["name"]}] if self.existing else []
            return {"actor": {"account": {"aiWorkflows": {"workflows": {"entities": entities}}}}}
        if document is nr.M_WORKFLOW_CREATE:
            return {"aiWorkflowsCreateWorkflow": {"workflow": {"id": "w1", "name": "x"}, "errors": []}}
        if document is nr.Q_ENTITY:
            query = variables["query"]
            if "APPLICATION" in query:
                return {"actor": {"entitySearch": {"results": {"entities": [{"guid": "APM", "name": "Hemodinks API"}]}}}}
            dashboards = [{"guid": "DASH", "name": nr.load_config()["dashboard"]["name"]}] if self.existing else []
            return {"actor": {"entitySearch": {"results": {"entities": dashboards}}}}
        if document is nr.Q_SERVICE_LEVELS:
            names = [s["name"] for s in nr.load_config()["service-levels"]["serviceLevels"]] if self.existing else []
            return {"actor": {"entity": {"serviceLevel": {"indicators": [{"id": f"s{i}", "name": n} for i, n in enumerate(names)]}}}}
        if document in (nr.M_SLI_CREATE, nr.M_SLI_UPDATE):
            return {}
        if document is nr.M_DASHBOARD_CREATE:
            return {"dashboardCreate": {"entityResult": {"guid": "DASH"}, "errors": []}}
        if document is nr.M_DASHBOARD_UPDATE:
            return {"dashboardUpdate": {"entityResult": {"guid": "DASH"}, "errors": []}}
        raise AssertionError(f"documento inesperado: {document[:60]}")

    def mutation_names(self):
        names = {id(nr.M_POLICY_CREATE): "policy+", id(nr.M_CONDITION_CREATE): "condition+",
                 id(nr.M_CONDITION_UPDATE): "condition~", id(nr.M_DESTINATION_CREATE): "destination+",
                 id(nr.M_CHANNEL_CREATE): "channel+", id(nr.M_WORKFLOW_CREATE): "workflow+",
                 id(nr.M_SLI_CREATE): "slo+", id(nr.M_SLI_UPDATE): "slo~",
                 id(nr.M_DASHBOARD_CREATE): "dashboard+", id(nr.M_DASHBOARD_UPDATE): "dashboard~"}
        return [names[id(document)] for document, _ in self.mutations]


def run(client, apply, variables=VARIABLES, email="alertas@example.invalid"):
    planner = nr.Planner(client, ACCOUNT, dict(variables), apply=apply, alert_email=email)
    return planner.run(nr.prune(nr.load_config(), variables))


class ValidationTests(unittest.TestCase):
    def test_committed_config_is_valid(self):
        self.assertEqual([], nr.validate(nr.load_config()))

    def test_every_condition_has_runbook_section(self):
        anchors = nr.runbook_anchors()
        for condition in nr.load_config()["alerts"]["conditions"]:
            self.assertIn(condition["runbook"], anchors)

    def test_sensitive_attributes_cannot_be_selected_or_faceted(self):
        for query in ("SELECT message FROM Log",
                      "SELECT count(*) FROM Transaction FACET request.uri",
                      "SELECT latest(cpf) FROM Log",
                      "SELECT count(*) FROM JavaScriptError FACET errorMessage SINCE 1 day ago",
                      "SELECT uniques(request.headers.authorization) FROM Transaction",
                      "SELECT count(*) FROM Log FACET userEmail LIMIT 5",
                      "SELECT\nmessage FROM Log",
                      "SELECT\tlatest(cpf)\nFROM Log",
                      "SELECT count(*) FROM Log FACET\n  userEmail\nSINCE 1 day ago"):
            with self.subTest(query=query):
                self.assertTrue(nr.sensitive_projection(query))

    def test_sensitive_attributes_may_filter(self):
        for query in ("SELECT count(*) FROM Log WHERE message LIKE 'ActiveSessionsSnapshot %'",
                      "SELECT count(*) FROM Transaction WHERE request.uri IN ('/api/users/authenticate') FACET name",
                      "SELECT max(numeric(UsuariosOnline)) AS 'Usuarios online' FROM Log TIMESERIES"):
            with self.subTest(query=query):
                self.assertEqual([], nr.sensitive_projection(query))

    def test_session_snapshot_rejects_aggregations_that_add_replicas(self):
        for query in ("SELECT sum(numeric(UsuariosOnline)) FROM Log TIMESERIES",
                      "SELECT count(*) FROM Log WHERE message LIKE 'ActiveSessionsSnapshot %'",
                      "SELECT average(numeric(SnapshotSessoes)) FROM Log FACET SnapshotClinicaId",
                      "SELECT latest(numeric(TotalSessoes)), SUM(numeric(UsuariosOnline)) FROM Log",
                      "SELECT\n  sum(numeric(UsuariosOnline))\nFROM Log",
                      "SELECT\tpercentile(numeric(TotalSessoes), 95) FROM Log"):
            with self.subTest(query=query):
                self.assertTrue(nr.replica_unsafe_aggregations(query))

    def test_session_snapshot_accepts_idempotent_aggregations(self):
        for query in ("SELECT max(numeric(UsuariosOnline)) AS 'sum(x)' FROM Log TIMESERIES",
                      "SELECT latest(numeric(SnapshotUsuarios)) FROM Log WHERE message LIKE 'ActiveSessionsSnapshotClinica%' FACET SnapshotClinicaId",
                      "SELECT min(numeric(TotalSessoes)) FROM Log",
                      "SELECT count(*) FROM Transaction WHERE appName = 'Hemodinks API'"):
            with self.subTest(query=query):
                self.assertEqual([], nr.replica_unsafe_aggregations(query))

    def test_validate_rejects_summed_session_snapshot(self):
        config = copy.deepcopy(nr.load_config())
        config["dashboard"]["pages"][0]["widgets"][0]["query"] = (
            "SELECT sum(numeric(UsuariosOnline)) FROM Log WHERE entity.name = '{{APM_APP}}'")
        self.assertIn("snapshot de sessoes repete por replica", "\n".join(nr.validate(config)))

    def test_validate_rejects_broken_definitions(self):
        config = nr.load_config()
        broken = copy.deepcopy(config)
        broken["alerts"]["conditions"][0]["runbook"] = "nao-existe"
        broken["alerts"]["conditions"][1]["name"] = broken["alerts"]["conditions"][0]["name"]
        broken["alerts"]["conditions"][2]["warningThreshold"] = broken["alerts"]["conditions"][2]["threshold"]
        broken["service-levels"]["serviceLevels"][0]["windowDays"] = 30
        broken["service-levels"]["serviceLevels"][1]["badEvents"] = {"from": "Transaction", "where": "x"}
        broken["dashboard"]["pages"][0]["widgets"][0]["query"] = "SELECT message FROM Log WHERE appName = '{{OUTRO}}'"
        errors = "\n".join(nr.validate(broken))
        for expected in ("sem runbook", "duplicados", "warning >= critical", "janela", "exatamente um",
                         "atributo sensivel", "placeholder desconhecido"):
            with self.subTest(expected=expected):
                self.assertIn(expected, errors)

    def test_render_requires_every_placeholder_and_escapes_quotes(self):
        with self.assertRaises(nr.ConfigError):
            nr.render("appName = '{{APM_APP}}'", {})
        self.assertEqual("appName = 'O\\'Brien'", nr.render("appName = '{{APM_APP}}'", {"APM_APP": "O'Brien"}))

    def test_condition_does_not_alert_on_missing_traffic_and_links_runbook(self):
        condition = nr.render(nr.load_config()["alerts"]["conditions"][0], VARIABLES)
        payload = nr.condition_input(condition)
        self.assertFalse(payload["expiration"]["openViolationOnExpiration"])
        # NerdGraph: close_violations_on_expiration exige a duracao (BAD_USER_INPUT no primeiro apply).
        if payload["expiration"]["closeViolationsOnExpiration"]:
            self.assertGreater(payload["expiration"].get("expirationDuration", 0), 0)
        self.assertTrue(payload["runbookUrl"].endswith("#alerta-api-erros-5xx"))
        self.assertEqual({"CRITICAL", "WARNING"}, {term["priority"] for term in payload["terms"]})

    def test_no_synthetic_monitor_touches_the_database(self):
        text = "".join((nr.CONFIG_DIR / f"{n}.json").read_text(encoding="utf-8") for n in ("alerts", "service-levels", "dashboard"))
        self.assertNotIn("Synthetic", text)
        self.assertNotIn("SyntheticCheck", text)


class PlannerTests(unittest.TestCase):
    def test_plan_reads_only(self):
        client = FakeNerdGraph()
        actions = run(client, apply=False)
        self.assertEqual([], client.mutations)
        self.assertTrue(any(a.startswith("criar") and "politica" in a for a in actions))

    def test_first_apply_creates_everything(self):
        client = FakeNerdGraph()
        run(client, apply=True)
        names = client.mutation_names()
        self.assertEqual(1, names.count("policy+"))
        self.assertEqual(len(nr.load_config()["alerts"]["conditions"]), names.count("condition+"))
        self.assertEqual(["destination+", "channel+", "workflow+"],
                         [n for n in names if n in ("destination+", "channel+", "workflow+")])
        self.assertEqual(len(nr.load_config()["service-levels"]["serviceLevels"]), names.count("slo+"))
        self.assertEqual(1, names.count("dashboard+"))

    def test_second_apply_is_idempotent(self):
        client = FakeNerdGraph(existing=True)
        run(client, apply=True)
        names = client.mutation_names()
        self.assertFalse([n for n in names if n.endswith("+")], names)
        self.assertIn("condition~", names)
        self.assertIn("slo~", names)
        self.assertIn("dashboard~", names)

    def test_front_items_are_skipped_without_browser_app(self):
        client = FakeNerdGraph()
        variables = {"APM_APP": "Hemodinks API", "BROWSER_APP": ""}
        actions = run(client, apply=True, variables=variables)
        self.assertTrue(any(a.startswith("ignorar") and "Front - Erros JavaScript" in a for a in actions))
        dashboard = next(v for d, v in client.mutations if d is nr.M_DASHBOARD_CREATE)["dashboard"]
        self.assertEqual(["API e banco"], [page["name"] for page in dashboard["pages"]])
        for _, variables_sent in client.mutations:
            self.assertNotIn("{{", repr(variables_sent))

    def test_homologation_creates_only_its_own_dashboard(self):
        client = FakeNerdGraph()
        variables = {"APM_APP": nr.HOMOLOGATION["APM_APP"], "BROWSER_APP": nr.HOMOLOGATION["BROWSER_APP"]}
        planner = nr.Planner(client, ACCOUNT, dict(variables), apply=True, alert_email="alertas@example.invalid")
        actions = planner.run(nr.prune(nr.for_homologation(nr.load_config()), variables), dashboard_only=True)
        self.assertEqual(["dashboard+"], client.mutation_names())
        dashboard = next(v for d, v in client.mutations if d is nr.M_DASHBOARD_CREATE)["dashboard"]
        self.assertEqual("HemoDinks - Saude da homologacao", dashboard["name"])
        sent = repr(dashboard)
        self.assertIn("Hemodinks API Confirmation", sent)
        self.assertIn("Hemodinks Front HOMOL", sent)
        self.assertNotIn("'Hemodinks API'", sent)
        self.assertTrue(all("politica" not in a and "SLO" not in a for a in actions), actions)
        # A copia nunca altera o nome do dashboard de producao versionado.
        self.assertEqual("HemoDinks - Saude da producao", nr.load_config()["dashboard"]["name"])

    def test_notifications_are_skipped_without_email(self):
        client = FakeNerdGraph()
        actions = run(client, apply=True, email=None)
        self.assertTrue(any("notificacao" in a and a.startswith("ignorar") for a in actions))
        self.assertNotIn("destination+", client.mutation_names())

    def test_workflow_filters_by_policy_and_email_only_goes_to_destination(self):
        client = FakeNerdGraph()
        run(client, apply=True)
        workflow = next(v for d, v in client.mutations if d is nr.M_WORKFLOW_CREATE)["workflow"]
        self.assertEqual(["p1"], workflow["issuesFilter"]["predicates"][0]["values"])
        for document, variables in client.mutations:
            if document is not nr.M_DESTINATION_CREATE:
                self.assertNotIn("alertas@example.invalid", repr(variables))

    def test_errors_name_the_failing_operation_and_details(self):
        message = nr.describe_errors(nr.M_CONDITION_CREATE, [{
            "message": "Validation Error", "path": ["alertsNrqlConditionStaticCreate"],
            "extensions": {"validationErrors": [{"field": "condition.terms.0.operator"}]}}])
        self.assertIn("mutation alertsNrqlConditionStaticCreate", message)
        self.assertIn("condition.terms.0.operator", message)
        self.assertEqual("query actor", nr.operation_name(nr.Q_POLICIES))

    def test_notification_create_error_is_reported_instead_of_none(self):
        result = {"destination": None, "error": {"__typename": "AiNotificationsResponseError",
                                                 "description": "Destination already exists", "type": "ENTITY_IN_USE"}}
        with self.assertRaisesRegex(RuntimeError, "destino nao criado: .*Destination already exists"):
            nr.created_or_raise(result, "destination", "destino")
        self.assertEqual({"id": "d1"}, nr.created_or_raise({"destination": {"id": "d1"}}, "destination", "destino"))
        for document in (nr.M_DESTINATION_CREATE, nr.M_CHANNEL_CREATE):
            self.assertIn("error { __typename", document)

    def test_plan_and_apply_require_credentials(self):
        with mock.patch.dict(os.environ, {"NEW_RELIC_API_KEY": "", "NEW_RELIC_ACCOUNT_ID": ""}):
            self.assertEqual(2, nr.main(["plan"]))


class WorkflowTests(unittest.TestCase):
    def test_workflow_is_manual_minimal_and_keeps_secrets_in_env(self):
        text = (ROOT / ".github/workflows/newrelic-observability.yml").read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", text)
        self.assertNotIn("push:", text)
        self.assertIn("permissions:\n  contents: read", text)
        self.assertIn("environment: production", text)
        self.assertIn("timeout-minutes:", text)
        self.assertIn("concurrency:", text)
        for line in text.splitlines():
            if "secrets." in line:
                self.assertRegex(line.strip(), r"^[A-Z_]+: \$\{\{ secrets\.[A-Z_]+ \}\}$")


if __name__ == "__main__":
    unittest.main()
