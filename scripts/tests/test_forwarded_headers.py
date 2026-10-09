import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).parents[1] / "forwarded_headers.py"
spec = importlib.util.spec_from_file_location("forwarded_headers", SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ForwardedHeadersTests(unittest.TestCase):
    def test_preflight_transports_approved_json_and_invalid_policy_writes_nothing(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'github-output'
            environment = dict(os.environ, API_FORWARDED_HEADERS_KNOWN_PROXIES='[]',
                               API_FORWARDED_HEADERS_KNOWN_NETWORKS='[]')
            command = [sys.executable, '-B', str(SCRIPT), '--github-output', str(output)]
            invalid = subprocess.run(command, env=environment, text=True, capture_output=True)
            self.assertNotEqual(0, invalid.returncode)
            self.assertFalse(output.exists())
            environment['API_FORWARDED_HEADERS_KNOWN_NETWORKS'] = '["10.21.0.0/24"]'
            valid = subprocess.run(command, env=environment, text=True, capture_output=True)
            self.assertEqual(0, valid.returncode, valid.stderr)
            self.assertEqual('', valid.stdout)
            self.assertEqual(m.build_environment('[]', '["10.21.0.0/24"]'),
                             json.loads(output.read_text().removeprefix('environment=')))

    def test_verified_allowlist_generates_explicit_single_hop_policy(self):
        values = {x["name"]: x["value"] for x in m.build_environment(
            '["10.20.0.4", "::ffff:10.20.0.4"]', '["10.21.0.0/24", "fd00:abcd::/64"]')}
        self.assertEqual("true", values["ForwardedHeaders__Enabled"])
        self.assertEqual("false", values["ForwardedHeaders__TrustAnyImmediateProxy"])
        self.assertEqual("false", values["ASPNETCORE_FORWARDEDHEADERS_ENABLED"])
        self.assertEqual("1", values["ForwardedHeaders__ForwardLimit"])
        self.assertEqual("10.20.0.4", values["ForwardedHeaders__KnownProxies__0"])
        self.assertEqual("fd00:abcd::/64", values["ForwardedHeaders__KnownNetworks__1"])

    def test_empty_or_malformed_input_is_rejected_without_echoing_input(self):
        for proxies, networks in [("", ""), ("[]", "[]"), ('"secret-input"', "[]"),
                                  ('["secret-input"]', "[]"), ('[null]', "[]"), ('{}', '[]'),
                                  ('[]', '["10.20.0.1/24"]'), ('[]', '["10.20.0.1"]')]:
            with self.subTest(proxies=proxies, networks=networks):
                with self.assertRaises(ValueError) as error:
                    m.build_environment(proxies, networks)
                self.assertNotIn("secret-input", str(error.exception))

    def test_universal_unspecified_multicast_and_scoped_addresses_rejected(self):
        for network in ["0.0.0.0/0", "::/0", "::ffff:0:0/96", "224.0.0.0/4"]:
            with self.subTest(network=network), self.assertRaises(ValueError):
                m.build_environment('[]', json.dumps([network]))
        for address in ["0.0.0.0", "::", "255.255.255.255", "224.0.0.1", "fe80::1%eth0"]:
            with self.subTest(address=address), self.assertRaises(ValueError):
                m.build_environment(json.dumps([address]), '[]')

    def test_merge_replaces_all_stale_proxy_keys_and_preserves_unrelated_secretrefs(self):
        previous = [
            {"name": "ForwardedHeaders__TrustAnyImmediateProxy", "value": "true"},
            {"name": "ForwardedHeaders__KnownNetworks__9", "secretRef": "old-network"},
            {"name": "forwardedheaders:knownproxies:18", "value": "10.99.0.1"},
            {"name": "ASPNETCORE_FORWARDEDHEADERS_ENABLED", "value": "true"},
            {"name": "ConnectionStrings__DefaultConnection", "secretRef": "database"},
            {"name": "BusinessSetting", "value": "preserved"},
        ]
        approved = m.build_environment('["10.20.0.4"]', '[]')
        merged = m.merge_environment(previous, approved)
        self.assertEqual(previous[-2:] + approved, merged)
        self.assertEqual("true", previous[0]["value"])

    def test_missing_approved_output_cannot_clear_policy(self):
        for approved in [None, [], {}, [{"name": "ForwardedHeaders__Enabled", "value": "true"}]]:
            with self.subTest(approved=approved), self.assertRaises(ValueError):
                m.merge_environment([], approved)

    def test_cli_merge_is_real_payload_path_and_invalid_preflight_has_no_output(self):
        approved = m.build_environment('[]', '["10.20.0.0/24"]')
        result = subprocess.run([sys.executable, '-B', str(SCRIPT), '--merge', json.dumps(approved)],
            input=json.dumps([{"name": "forwardedheaders:knownnetworks:99", "value": "0.0.0.0/0"}]),
            text=True, capture_output=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(approved, json.loads(result.stdout))
        invalid = subprocess.run([sys.executable, '-B', str(SCRIPT), '--known-proxies', '[]', '--known-networks', '[]'],
            text=True, capture_output=True)
        self.assertNotEqual(0, invalid.returncode)
        self.assertEqual('', invalid.stdout)
        self.assertIn('::error::', invalid.stderr)


if __name__ == '__main__':
    unittest.main()
