"""Validate explicitly verified ingress addresses; generate/merge Azure environment JSON."""
import argparse
import ipaddress
import json
import os
from pathlib import Path
import sys

FIXED = {
    "ForwardedHeaders__Enabled": "true",
    "ForwardedHeaders__ForwardLimit": "1",
    "ForwardedHeaders__TrustAnyImmediateProxy": "false",
    "ASPNETCORE_FORWARDEDHEADERS_ENABLED": "false",
    "ForwardedHeaders__AzureContainerAppsIngress": "false",
}


def parse_addresses(raw, networks=False):
    try:
        if len(raw) > 8192:
            raise ValueError()
        values = json.loads(raw or "[]")
        if not isinstance(values, list) or len(values) > 64:
            raise ValueError()
        result = []
        for value in values:
            if not isinstance(value, str) or '%' in value or not value.strip():
                raise ValueError()
            value = value.strip()
            if networks:
                if '/' not in value:
                    raise ValueError()
                address = ipaddress.ip_network(value, strict=True)
                if address.prefixlen == 0:
                    raise ValueError()
                first = address.network_address
            else:
                address = ipaddress.ip_address(value)
                first = address
            effective = getattr(first, 'ipv4_mapped', None) or first
            if effective.is_unspecified or effective.is_multicast or str(effective) == '255.255.255.255':
                raise ValueError()
            normalized = str(address)
            if normalized not in result:
                result.append(normalized)
        return result
    except (ValueError, TypeError):
        kind = 'networks' if networks else 'proxies'
        raise ValueError(f'Invalid trusted {kind}: use a JSON array of verified IPs/CIDRs; universal, scoped or unspecified addresses are forbidden.') from None


def validate_azure_http_ingress(app):
    try:
        ingress = app['properties']['configuration']['ingress']
        name = app['name']
        fqdn = ingress['fqdn']
        if (name != 'hemodinks-api-prod' or app['resourceGroup'].casefold() != 'rg-hemodinks-prod'
                or ingress['transport'].casefold() not in ('auto', 'http')
                or ingress['targetPort'] != 8080 or ingress['allowInsecure'] is not False
                or ingress.get('additionalPortMappings') not in (None, [])
                or not fqdn.startswith(name + '.') or not fqdn.endswith('.azurecontainerapps.io')):
            raise ValueError()
        return {'name': name, 'dnsSuffix': fqdn[len(name) + 1:]}
    except (KeyError, TypeError, AttributeError, ValueError):
        raise ValueError('Azure ingress must be the exact Production HTTP target with no additional TCP ports.') from None


def build_environment(proxies, networks, azure_identity=None):
    proxies = parse_addresses(proxies)
    networks = parse_addresses(networks, networks=True)
    if not proxies and not networks and azure_identity is None:
        raise ValueError('Configure API_FORWARDED_HEADERS_KNOWN_PROXIES or API_FORWARDED_HEADERS_KNOWN_NETWORKS in the production Environment with a verified JSON allowlist.')
    entries = [{"name": name, "value": value} for name, value in FIXED.items()]
    if not proxies and not networks:
        entries[-1]['value'] = 'true'
        entries.extend([
            {'name': 'ForwardedHeaders__AzureContainerAppName', 'value': azure_identity['name']},
            {'name': 'ForwardedHeaders__AzureContainerAppDnsSuffix', 'value': azure_identity['dnsSuffix']},
        ])
    for group, values in [('KnownProxies', proxies), ('KnownNetworks', networks)]:
        entries.extend({"name": f"ForwardedHeaders__{group}__{i}", "value": value} for i, value in enumerate(values))
    return entries


def merge_environment(existing, approved):
    if not isinstance(existing, list) or not isinstance(approved, list):
        raise ValueError('Missing or invalid approved proxy environment.')
    values = {}
    for item in approved:
        if not isinstance(item, dict) or set(item) != {'name', 'value'} or not isinstance(item['name'], str) or not isinstance(item['value'], str) or item['name'] in values:
            raise ValueError('Invalid approved proxy environment.')
        values[item['name']] = item['value']
    azure_ingress = values.get('ForwardedHeaders__AzureContainerAppsIngress') == 'true'
    expected_fixed = dict(FIXED)
    expected_fixed['ForwardedHeaders__AzureContainerAppsIngress'] = str(azure_ingress).lower()
    if any(values.get(name) != value for name, value in expected_fixed.items()):
        raise ValueError('Missing or unsafe approved proxy environment.')
    groups = []
    for group in ['KnownProxies', 'KnownNetworks']:
        prefix = f'ForwardedHeaders__{group}__'
        items = [values.get(prefix + str(i)) for i in range(sum(name.startswith(prefix) for name in values))]
        groups.append(json.dumps(items))
    identity = None
    if azure_ingress:
        name = values.get('ForwardedHeaders__AzureContainerAppName')
        suffix = values.get('ForwardedHeaders__AzureContainerAppDnsSuffix', '')
        if name != 'hemodinks-api-prod' or not suffix.endswith('.azurecontainerapps.io') or any(json.loads(group) for group in groups):
            raise ValueError('Invalid approved Azure ingress policy.')
        identity = {'name': name, 'dnsSuffix': suffix}
    canonical = build_environment(*groups, azure_identity=identity)
    if {item['name']: item['value'] for item in canonical} != values:
        raise ValueError('Invalid approved proxy environment.')
    retained = []
    for item in existing:
        if not isinstance(item, dict) or not isinstance(item.get('name'), str):
            raise ValueError('Invalid existing environment.')
        name = item['name'].replace('__', ':').casefold()
        if name == 'forwardedheaders' or name.startswith('forwardedheaders:') or item['name'].casefold() == 'aspnetcore_forwardedheaders_enabled':
            continue
        retained.append(item)
    return retained + canonical


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--known-proxies', default=os.getenv('API_FORWARDED_HEADERS_KNOWN_PROXIES', '[]'))
    parser.add_argument('--known-networks', default=os.getenv('API_FORWARDED_HEADERS_KNOWN_NETWORKS', '[]'))
    parser.add_argument('--github-output')
    parser.add_argument('--azure-app-config', action='store_true', help='Read Azure app JSON from stdin and validate the HTTP ingress boundary; empty allowlists select managed ingress.')
    parser.add_argument('--validate-azure-app-config', action='store_true', help='Validate the Azure HTTP ingress boundary without generating environment values.')
    parser.add_argument('--merge', help='Previously validated proxy JSON; existing environment is read from stdin.')
    parser.add_argument('--verify-existing', action='store_true', help='Require the existing proxy policy to match the approved policy without printing environment values.')
    args = parser.parse_args()
    try:
        identity = None
        if args.azure_app_config or args.validate_azure_app_config:
            if args.merge is not None or args.verify_existing:
                raise ValueError('Conflicting operations.')
            identity = validate_azure_http_ingress(json.load(sys.stdin))
            if args.validate_azure_app_config:
                return 0
        if args.verify_existing and args.merge is None:
            raise ValueError('Verification requires an approved proxy environment.')
        existing = json.load(sys.stdin) if args.merge is not None else None
        entries = merge_environment(existing, json.loads(args.merge)) if args.merge is not None else build_environment(args.known_proxies, args.known_networks, identity)
        if args.verify_existing:
            if sorted(existing, key=lambda item: item['name']) != sorted(entries, key=lambda item: item['name']):
                raise ValueError('Existing proxy policy differs from the approved policy.')
            return 0
        payload = json.dumps(entries, separators=(',', ':'))
        if args.github_output:
            with Path(args.github_output).open('a', encoding='utf-8') as output:
                output.write('environment=' + payload + '\n')
        else:
            print(payload)
    except (ValueError, OSError):
        # Do not echo existing env values, tokens or arbitrary input in errors.
        print('::error::Invalid ingress policy. Verify the Production Azure HTTP ingress boundary or configure verified proxy JSON arrays. See docs/forwarded-headers-production-preflight.md.', file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
