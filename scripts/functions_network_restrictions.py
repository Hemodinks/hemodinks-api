"""Restricao de origem das Azure Functions de producao (issue #224).

A API roda num ambiente Consumption do Azure Container Apps sem VNet, entao nao tem IP de saida
fixo: a Azure publica a lista completa de IPs de saida possiveis (`outboundIpAddresses`). Este
script transforma essa lista em regras de acesso da Function App (default Deny) e compara com o
que esta aplicado. Nao chama a Azure: o workflow le e grava a configuracao com `az rest`.

Uso:
  check    --current web-config.json --source container-app.json
  payload  --source container-app.json      (corpo do PATCH em config/web)
  rollback                                   (corpo do PATCH que volta a aceitar qualquer origem)
"""

import argparse
import ipaddress
import json
import sys

RULE_PREFIX = "api-egress-"
FIRST_PRIORITY = 100
IPS_PER_RULE = 8  # limite de origens por regra no App Service
MAX_RULES = 512  # limite de regras de acesso no App Service


class RestrictionError(Exception):
    pass


def normalize_ips(values):
    networks = set()
    for value in values:
        try:
            network = ipaddress.ip_network(str(value).strip(), strict=True)
        except ValueError as error:
            raise RestrictionError(f"IP de saida invalido: {value!r}") from error
        if network.version != 4:
            raise RestrictionError(f"So IPv4 e suportado: {value!r}")
        networks.add(network)
    if not networks:
        raise RestrictionError("A lista de IPs de saida da API esta vazia; nada sera restringido.")
    return sorted(str(network) for network in networks)


def source_ips(container_app):
    values = (container_app.get("properties") or {}).get("outboundIpAddresses")
    if not isinstance(values, list):
        raise RestrictionError("Container App sem properties.outboundIpAddresses.")
    return normalize_ips(values)


def build_rules(ips):
    chunks = [ips[index:index + IPS_PER_RULE] for index in range(0, len(ips), IPS_PER_RULE)]
    if len(chunks) > MAX_RULES:
        raise RestrictionError(f"{len(chunks)} regras excedem o limite de {MAX_RULES} do App Service.")
    return [
        {
            "name": f"{RULE_PREFIX}{number:03d}",
            "description": "Saida da API (Container Apps) - issue #224",
            "action": "Allow",
            "priority": FIRST_PRIORITY + number,
            "ipAddress": ",".join(chunk),
        }
        for number, chunk in enumerate(chunks, start=1)
    ]


def apply_payload(ips):
    # SCM fica com regras proprias (scmIpSecurityRestrictionsUseMain=false) para nao bloquear o deploy.
    return {
        "properties": {
            "ipSecurityRestrictions": build_rules(ips),
            "ipSecurityRestrictionsDefaultAction": "Deny",
            "scmIpSecurityRestrictionsUseMain": False,
        }
    }


def rollback_payload():
    return {
        "properties": {
            "ipSecurityRestrictions": [],
            "ipSecurityRestrictionsDefaultAction": "Allow",
            "scmIpSecurityRestrictionsUseMain": False,
        }
    }


def _properties(web_config):
    return web_config.get("properties", web_config) or {}


def is_restricted(web_config):
    properties = _properties(web_config)
    rules = properties.get("ipSecurityRestrictions") or []
    allow_any = any(
        rule.get("action", "Allow") == "Allow" and str(rule.get("ipAddress", "")).lower() == "any"
        for rule in rules
    )
    return properties.get("ipSecurityRestrictionsDefaultAction") == "Deny" and not allow_any


def allowed_ips(web_config):
    allowed = set()
    for rule in _properties(web_config).get("ipSecurityRestrictions") or []:
        if rule.get("action", "Allow") != "Allow" or not rule.get("ipAddress"):
            continue
        for value in str(rule["ipAddress"]).split(","):
            if value.strip().lower() != "any":
                allowed.add(str(ipaddress.ip_network(value.strip(), strict=False)))
    return allowed


def check(web_config, ips):
    """Retorna (status, mensagens). status: not-applied | in-sync | drift."""
    if not is_restricted(web_config):
        return "not-applied", ["Functions ainda aceitam qualquer origem (restricao nao aplicada)."]
    expected = set(ips)
    current = allowed_ips(web_config)
    missing = sorted(expected - current)
    extra = sorted(current - expected)
    if not missing and not extra:
        return "in-sync", [f"Restricao aplicada e alinhada com {len(expected)} IPs de saida da API."]
    messages = []
    if missing:
        messages.append(f"{len(missing)} IP(s) de saida da API sem permissao (chamadas podem falhar): {', '.join(missing)}")
    if extra:
        messages.append(f"{len(extra)} IP(s) permitidos que a API nao usa mais: {', '.join(extra)}")
    return "drift", messages


def _load(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    check_parser = commands.add_parser("check")
    check_parser.add_argument("--current", required=True)
    check_parser.add_argument("--source", required=True)
    check_parser.add_argument("--require-applied", action="store_true")
    payload_parser = commands.add_parser("payload")
    payload_parser.add_argument("--source", required=True)
    commands.add_parser("rollback")
    args = parser.parse_args(argv)

    try:
        if args.command == "payload":
            print(json.dumps(apply_payload(source_ips(_load(args.source)))))
            return 0
        if args.command == "rollback":
            print(json.dumps(rollback_payload()))
            return 0
        status, messages = check(_load(args.current), source_ips(_load(args.source)))
    except RestrictionError as error:
        print(f"::error::{error}", file=sys.stderr)
        return 2

    for message in messages:
        level = "notice" if status == "in-sync" else "warning" if status == "not-applied" else "error"
        print(f"::{level}::{message}")
    if status == "drift" or (status == "not-applied" and args.require_applied):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
