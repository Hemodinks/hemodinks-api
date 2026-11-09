import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "functions_network_restrictions", Path(__file__).parents[1] / "functions_network_restrictions.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

ALLOW_ALL = {"properties": {"ipSecurityRestrictions": [
    {"action": "Allow", "ipAddress": "Any", "name": "Allow all", "priority": 2147483647}]}}


def container_app(ips):
    return {"properties": {"outboundIpAddresses": ips}}


def ips(count):
    return [f"20.226.{index // 250}.{index % 250 + 1}" for index in range(count)]


class FunctionsNetworkRestrictionTests(unittest.TestCase):
    def test_rules_group_eight_ips_and_deny_everything_else(self):
        payload = m.apply_payload(m.normalize_ips(ips(17)))["properties"]

        self.assertEqual("Deny", payload["ipSecurityRestrictionsDefaultAction"])
        self.assertFalse(payload["scmIpSecurityRestrictionsUseMain"])
        rules = payload["ipSecurityRestrictions"]
        self.assertEqual([8, 8, 1], [len(rule["ipAddress"].split(",")) for rule in rules])
        self.assertEqual(["api-egress-001", "api-egress-002", "api-egress-003"], [rule["name"] for rule in rules])
        self.assertTrue(all(rule["action"] == "Allow" for rule in rules))
        self.assertEqual(len(rules), len({rule["priority"] for rule in rules}))

    def test_source_ips_are_normalized_deduplicated_and_sorted(self):
        self.assertEqual(
            ["20.226.185.233/32", "4.228.165.239/32"],
            m.source_ips(container_app([" 4.228.165.239", "20.226.185.233", "20.226.185.233/32"])))

    def test_rejects_empty_invalid_or_ipv6_sources(self):
        for value in ([], ["not-an-ip"], ["10.0.0.1/8"], ["2001:db8::1"]):
            with self.subTest(value=value), self.assertRaises(m.RestrictionError):
                m.source_ips(container_app(value))
        with self.assertRaises(m.RestrictionError):
            m.source_ips({"properties": {}})

    def test_rejects_more_rules_than_app_service_allows(self):
        with self.assertRaises(m.RestrictionError):
            m.build_rules(m.normalize_ips(ips(m.IPS_PER_RULE * m.MAX_RULES + 1)))

    def test_allow_all_is_reported_as_not_applied(self):
        status, _ = m.check(ALLOW_ALL, m.normalize_ips(ips(3)))
        self.assertEqual("not-applied", status)

    def test_deny_with_allow_any_rule_is_not_considered_restricted(self):
        config = {"properties": {**ALLOW_ALL["properties"], "ipSecurityRestrictionsDefaultAction": "Deny"}}
        self.assertFalse(m.is_restricted(config))

    def test_applied_payload_is_in_sync(self):
        expected = m.normalize_ips(ips(20))
        status, _ = m.check(m.apply_payload(expected), expected)
        self.assertEqual("in-sync", status)

    def test_new_api_ip_is_reported_as_drift(self):
        applied = m.apply_payload(m.normalize_ips(ips(5)))
        status, messages = m.check(applied, m.normalize_ips(ips(5) + ["191.235.40.92"]))
        self.assertEqual("drift", status)
        self.assertIn("191.235.40.92/32", messages[0])

    def test_removed_api_ip_is_reported_as_drift(self):
        applied = m.apply_payload(m.normalize_ips(ips(5)))
        status, messages = m.check(applied, m.normalize_ips(ips(4)))
        self.assertEqual("drift", status)
        self.assertIn("nao usa mais", messages[0])

    def test_rollback_restores_open_access(self):
        payload = m.rollback_payload()["properties"]
        self.assertEqual([], payload["ipSecurityRestrictions"])
        self.assertEqual("Allow", payload["ipSecurityRestrictionsDefaultAction"])

    def test_cli_exit_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory, "source.json")
            current = Path(directory, "current.json")
            source.write_text(json.dumps(container_app(ips(3))), encoding="utf-8")
            current.write_text(json.dumps(ALLOW_ALL), encoding="utf-8")
            args = ["check", "--current", str(current), "--source", str(source)]
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, m.main(args))
                self.assertEqual(1, m.main(args + ["--require-applied"]))

                payload = io.StringIO()
                with redirect_stdout(payload):
                    self.assertEqual(0, m.main(["payload", "--source", str(source)]))
                current.write_text(payload.getvalue(), encoding="utf-8")
                self.assertEqual(0, m.main(args + ["--require-applied"]))

                source.write_text(json.dumps(container_app(ips(4))), encoding="utf-8")
                self.assertEqual(1, m.main(args))

                source.write_text(json.dumps(container_app([])), encoding="utf-8")
                with redirect_stderr(io.StringIO()):
                    self.assertEqual(2, m.main(args))


if __name__ == "__main__":
    unittest.main()
