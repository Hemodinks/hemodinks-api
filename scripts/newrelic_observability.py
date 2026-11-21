#!/usr/bin/env python3
"""Alertas, SLOs e dashboard do New Relic versionados em observability/newrelic (#289).

Modos:
  validate  offline: JSON, placeholders, nomes unicos, runbooks e regra de dados sensiveis (CI).
  plan      le o estado atual via NerdGraph e mostra o que seria criado/atualizado (nao altera nada).
  apply     cria o que falta e sincroniza o que existe, casando por nome (idempotente).

Ambiente (plan/apply):
  NEW_RELIC_API_KEY          User key (NRAK-...). Nunca e impressa.
  NEW_RELIC_ACCOUNT_ID       conta New Relic.
  NEW_RELIC_REGION           US (padrao) ou EU.
  NEW_RELIC_APM_APP_NAME     padrao "Hemodinks API".
  NEW_RELIC_BROWSER_APP_NAME app do New Relic Browser do front; sem ela, itens do front sao ignorados.
  NEW_RELIC_ALERT_EMAIL      destino das notificacoes; sem ela, a rota de notificacao nao e criada.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONFIG_DIR = ROOT / "observability/newrelic"
RUNBOOK = ROOT / "docs/observability-newrelic.md"
RUNBOOK_URL = "https://github.com/Hemodinks/hemodinks-api/blob/main/docs/observability-newrelic.md"
ENDPOINTS = {"US": "https://api.newrelic.com/graphql", "EU": "https://api.eu.newrelic.com/graphql"}
DEFAULT_APM_APP = "Hemodinks API"
EXPIRATION_SECONDS = 3600
PLACEHOLDER = re.compile(r"\{\{(\w+)\}\}")

# Alertas e dashboards nao podem exibir CPF, token, senha, email, mensagem livre ou dado de paciente.
# Esses atributos podem filtrar (WHERE), nunca ser selecionados nem agrupados (SELECT/FACET).
SENSITIVE = re.compile(
    r"\b(message|errorMessage|error\.message|request\.uri|request\.parameters\.\w+|request\.headers\.\w+"
    r"|\w*cpf\w*|\w*token\w*|\w*senha\w*|\w*password\w*|\w*email\w*|\w*paciente\w*|\w*nome\w*)\b",
    re.IGNORECASE)


class ConfigError(ValueError):
    pass


# ---------------------------------------------------------------- configuracao

def load_config(config_dir: Path = CONFIG_DIR) -> dict:
    return {name: json.loads((config_dir / f"{name}.json").read_text(encoding="utf-8"))
            for name in ("alerts", "service-levels", "dashboard")}


def render(value, variables: dict):
    """Substitui {{VAR}} recursivamente; placeholder sem valor e erro."""
    if isinstance(value, str):
        def replace(match):
            key = match.group(1)
            if key not in variables:
                raise ConfigError(f"placeholder sem valor: {{{{{key}}}}}")
            return str(variables[key]).replace("'", "\\'")
        return PLACEHOLDER.sub(replace, value)
    if isinstance(value, list):
        return [render(item, variables) for item in value]
    if isinstance(value, dict):
        return {key: render(item, variables) for key, item in value.items()}
    return value


def enabled(item: dict, variables: dict) -> bool:
    return not item.get("requires") or bool(variables.get(item["requires"]))


def projection_clauses(query: str) -> list[str]:
    """Trechos de SELECT e FACET de uma NRQL (o WHERE pode citar atributos sensiveis)."""
    upper = query.upper()
    clauses = []
    select = upper.find("SELECT ")
    if select >= 0:
        end = upper.find(" FROM ", select)
        clauses.append(query[select + 7:end if end >= 0 else len(query)])
    for facet in re.finditer(r"\bFACET\b", upper):
        rest = query[facet.end():]
        stop = re.search(r"\b(SINCE|UNTIL|LIMIT|TIMESERIES|COMPARE|WITH|ORDER)\b", rest, re.IGNORECASE)
        clauses.append(rest[:stop.start() if stop else len(rest)])
    return clauses


def sensitive_projection(query: str) -> list[str]:
    found = []
    for clause in projection_clauses(query):
        # Aliases entre aspas ('Usuarios online') nao sao atributos.
        found += SENSITIVE.findall(re.sub(r"'[^']*'", "''", clause))
    return found


# Cada replica ativa registra o mesmo snapshot global de sessoes (#294): somar ou contar
# multiplica o valor pelo numero de replicas/revisoes. Lista fechada: so latest/max/min
# (e numeric) no SELECT; qualquer outra funcao (sum, count, average, percentile...) e recusada.
SNAPSHOT_QUERY = re.compile(
    r"ActiveSessionsSnapshot|\b(UsuariosOnline|TotalSessoes|SnapshotUsuarios|SnapshotSessoes)\b")
SNAPSHOT_AGGREGATIONS = {"latest", "max", "min"}


def replica_unsafe_aggregations(query: str) -> list[str]:
    """Funcoes do SELECT que somam o snapshot repetido por replica (vazio se seguro)."""
    if not SNAPSHOT_QUERY.search(query):
        return []
    # Quebra de linha ou tab apos SELECT nao pode esconder a agregacao.
    select = projection_clauses(re.sub(r"\s+", " ", query))[:1]
    functions = re.findall(r"\b(\w+)\s*\(", re.sub(r"'[^']*'", "''", select[0])) if select else []
    return sorted({f.lower() for f in functions} - SNAPSHOT_AGGREGATIONS - {"numeric"})


def runbook_anchors(path: Path = RUNBOOK) -> set[str]:
    anchors = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        match = re.match(r"\s*<a id=\"([\w-]+)\"></a>", line)
        if match:
            anchors.add(match.group(1))
    return anchors


def queries(config: dict) -> list[tuple[str, str]]:
    items = [(f"condicao '{c['name']}'", c["query"]) for c in config["alerts"]["conditions"]]
    for level in config["service-levels"]["serviceLevels"]:
        for key in ("validEvents", "goodEvents", "badEvents"):
            if key in level:
                items.append((f"SLO '{level['name']}' {key}", f"SELECT count(*) FROM {level[key]['from']} WHERE {level[key]['where']}"))
    for page in config["dashboard"]["pages"]:
        items += [(f"widget '{w['title']}'", w["query"]) for w in page["widgets"]]
    return items


def validate(config: dict, anchors: set[str] | None = None) -> list[str]:
    errors = []
    anchors = runbook_anchors() if anchors is None else anchors
    alerts, levels, dashboard = config["alerts"], config["service-levels"], config["dashboard"]

    names = [c["name"] for c in alerts["conditions"]]
    if len(names) != len(set(names)):
        errors.append("nomes de condicao duplicados")
    for condition in alerts["conditions"]:
        for field in ("name", "runbook", "query", "aggregationWindow", "threshold", "thresholdDuration"):
            if field not in condition:
                errors.append(f"condicao '{condition.get('name')}' sem {field}")
        if condition.get("runbook") not in anchors:
            errors.append(f"condicao '{condition.get('name')}' sem runbook em docs/observability-newrelic.md")
        if condition.get("thresholdDuration", 0) < condition.get("aggregationWindow", 0):
            errors.append(f"condicao '{condition.get('name')}' com thresholdDuration menor que a janela")
        if "warningThreshold" in condition and condition["warningThreshold"] >= condition["threshold"]:
            errors.append(f"condicao '{condition.get('name')}' com warning >= critical")

    level_names = [level["name"] for level in levels["serviceLevels"]]
    if len(level_names) != len(set(level_names)):
        errors.append("nomes de SLO duplicados")
    for level in levels["serviceLevels"]:
        if ("goodEvents" in level) == ("badEvents" in level):
            errors.append(f"SLO '{level['name']}' deve ter goodEvents ou badEvents (exatamente um)")
        if not 0 < level.get("target", 0) < 100:
            errors.append(f"SLO '{level['name']}' com meta fora de (0, 100)")
        if level.get("windowDays") not in (1, 7, 28):
            errors.append(f"SLO '{level['name']}' com janela fora de 1, 7 ou 28 dias")

    for page in dashboard["pages"]:
        for widget in page["widgets"]:
            layout = widget.get("layout", {})
            if layout.get("column", 0) + layout.get("width", 0) - 1 > 12:
                errors.append(f"widget '{widget['title']}' passa de 12 colunas")

    for label, query in queries(config):
        unknown = set(PLACEHOLDER.findall(query)) - {"APM_APP", "BROWSER_APP"}
        if unknown:
            errors.append(f"{label}: placeholder desconhecido {sorted(unknown)}")
        if sensitive_projection(query):
            errors.append(f"{label}: SELECT/FACET expoe atributo sensivel {sorted(set(sensitive_projection(query)))}")
        unsafe = replica_unsafe_aggregations(query)
        if unsafe:
            errors.append(f"{label}: snapshot de sessoes repete por replica; use latest/max/min, nao {unsafe}")
    return errors


# ---------------------------------------------------------------- NerdGraph

class NerdGraph:
    def __init__(self, api_key: str, region: str = "US"):
        if region not in ENDPOINTS:
            raise ConfigError("NEW_RELIC_REGION deve ser US ou EU")
        self._url, self._key = ENDPOINTS[region], api_key

    def query(self, document: str, variables: dict | None = None) -> dict:
        body = json.dumps({"query": document, "variables": variables or {}}).encode("utf-8")
        request = urllib.request.Request(self._url, data=body, method="POST", headers={
            "Content-Type": "application/json", "API-Key": self._key, "User-Agent": "hemodinks-observability"})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                payload = json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"NerdGraph HTTP {error.code}") from None
        if payload.get("errors"):
            raise RuntimeError(describe_errors(document, payload["errors"]))
        return payload["data"]


def operation_name(document: str) -> str:
    """Primeiro campo da operacao (ex.: alertsPolicyCreate), para saber qual chamada falhou."""
    body = document[document.find("{") + 1:]
    match = re.search(r"\b([A-Za-z]\w*)\s*[({]", body)
    kind = "mutation" if document.lstrip().startswith("mutation") else "query"
    return f"{kind} {match.group(1) if match else '?'}"


def describe_errors(document: str, errors: list[dict]) -> str:
    """Mensagem, caminho e detalhes de validacao da NerdGraph. Nunca inclui as variaveis enviadas."""
    parts = []
    for error in errors:
        detail = error.get("message", "?")
        if error.get("path"):
            detail += f" em {'.'.join(str(p) for p in error['path'])}"
        if error.get("extensions"):
            # No GitHub Actions, valores de secrets que aparecam aqui ja sao mascarados no log.
            detail += f" {json.dumps(error['extensions'], ensure_ascii=False)[:800]}"
        parts.append(detail)
    return f"NerdGraph ({operation_name(document)}): " + "; ".join(parts)


Q_POLICIES = """query($accountId: Int!, $name: String) { actor { account(id: $accountId) { alerts {
  policiesSearch(searchCriteria: {name: $name}) { policies { id name } } } } } }"""
M_POLICY_CREATE = """mutation($accountId: Int!, $policy: AlertsPolicyInput!) {
  alertsPolicyCreate(accountId: $accountId, policy: $policy) { id name } }"""
Q_CONDITIONS = """query($accountId: Int!, $policyId: ID) { actor { account(id: $accountId) { alerts {
  nrqlConditionsSearch(searchCriteria: {policyId: $policyId}) { nrqlConditions { id name } } } } } }"""
M_CONDITION_CREATE = """mutation($accountId: Int!, $policyId: ID!, $condition: AlertsNrqlConditionStaticInput!) {
  alertsNrqlConditionStaticCreate(accountId: $accountId, policyId: $policyId, condition: $condition) { id name } }"""
M_CONDITION_UPDATE = """mutation($accountId: Int!, $id: ID!, $condition: AlertsNrqlConditionUpdateStaticInput!) {
  alertsNrqlConditionStaticUpdate(accountId: $accountId, id: $id, condition: $condition) { id name } }"""
Q_DESTINATIONS = """query($accountId: Int!, $name: String) { actor { account(id: $accountId) { aiNotifications {
  destinations(filters: {name: $name}) { entities { id name } } } } } }"""
# As mutations de notificacao recusam devolvendo `error` (union), nao erro GraphQL.
AI_NOTIFICATIONS_ERROR = """error { __typename
    ... on AiNotificationsResponseError { description details type }
    ... on AiNotificationsDataValidationError { details fields { field message } }
    ... on AiNotificationsConstraintsError { constraints { dependencies name } }
    ... on AiNotificationsSuggestionError { description details type } }"""
M_DESTINATION_CREATE = """mutation($accountId: Int!, $destination: AiNotificationsDestinationInput!) {
  aiNotificationsCreateDestination(accountId: $accountId, destination: $destination) {
    destination { id name } """ + AI_NOTIFICATIONS_ERROR + """ } }"""
Q_CHANNELS = """query($accountId: Int!, $name: String) { actor { account(id: $accountId) { aiNotifications {
  channels(filters: {name: $name}) { entities { id name } } } } } }"""
M_CHANNEL_CREATE = """mutation($accountId: Int!, $channel: AiNotificationsChannelInput!) {
  aiNotificationsCreateChannel(accountId: $accountId, channel: $channel) {
    channel { id name } """ + AI_NOTIFICATIONS_ERROR + """ } }"""
Q_WORKFLOWS = """query($accountId: Int!, $name: String) { actor { account(id: $accountId) { aiWorkflows {
  workflows(filters: {name: $name}) { entities { id name } } } } } }"""
M_WORKFLOW_CREATE = """mutation($accountId: Int!, $workflow: AiWorkflowsCreateWorkflowInput!) {
  aiWorkflowsCreateWorkflow(accountId: $accountId, createWorkflowData: $workflow) { workflow { id name } errors { description } } }"""
Q_ENTITY = """query($query: String!) { actor { entitySearch(query: $query) { results { entities { guid name } } } } }"""
Q_SERVICE_LEVELS = """query($guid: EntityGuid!) { actor { entity(guid: $guid) { serviceLevel { indicators { id name } } } } }"""
M_SLI_CREATE = """mutation($guid: EntityGuid!, $indicator: ServiceLevelIndicatorCreateInput!) {
  serviceLevelCreate(entityGuid: $guid, indicator: $indicator) { id name } }"""
M_SLI_UPDATE = """mutation($id: ID!, $indicator: ServiceLevelIndicatorUpdateInput!) {
  serviceLevelUpdate(id: $id, indicator: $indicator) { id name } }"""
M_DASHBOARD_CREATE = """mutation($accountId: Int!, $dashboard: DashboardInput!) {
  dashboardCreate(accountId: $accountId, dashboard: $dashboard) { entityResult { guid name } errors { description } } }"""
M_DASHBOARD_UPDATE = """mutation($guid: EntityGuid!, $dashboard: DashboardInput!) {
  dashboardUpdate(guid: $guid, dashboard: $dashboard) { entityResult { guid name } errors { description } } }"""


def entity_query(name: str, kind: str, account_id: int) -> str:
    escaped = name.replace("\\", "\\\\").replace("'", "\\'")
    return f"name = '{escaped}' AND {kind} AND accountId = {int(account_id)}"


def created_or_raise(result: dict, key: str, label: str) -> dict:
    """aiNotifications devolve o objeto criado ou `error`; nunca seguir com None."""
    if result.get(key):
        return result[key]
    raise RuntimeError(f"{label} nao criado: {json.dumps(result.get('error'), ensure_ascii=False)[:800]}")


def by_name(items: list[dict], name: str) -> dict | None:
    return next((item for item in items if item.get("name") == name), None)


def condition_input(condition: dict) -> dict:
    terms = [{"priority": "CRITICAL", "operator": "ABOVE", "threshold": condition["threshold"],
              "thresholdDuration": condition["thresholdDuration"], "thresholdOccurrences": "ALL"}]
    if "warningThreshold" in condition:
        terms.append({"priority": "WARNING", "operator": "ABOVE", "threshold": condition["warningThreshold"],
                      "thresholdDuration": condition["thresholdDuration"], "thresholdOccurrences": "ALL"})
    return {
        "name": condition["name"],
        "description": condition.get("description", ""),
        "enabled": True,
        "nrql": {"query": condition["query"]},
        "signal": {"aggregationWindow": condition["aggregationWindow"], "aggregationMethod": "EVENT_FLOW",
                   "aggregationDelay": 120},
        "terms": terms,
        "violationTimeLimitSeconds": 86400,
        # Sem trafego (madrugada, SQL pausado) nao e incidente: nao abrir por perda de sinal e fechar o
        # incidente aberto apos 1 h sem dados. A NerdGraph exige a duracao quando close=true.
        "expiration": {"closeViolationsOnExpiration": True, "openViolationOnExpiration": False,
                       "expirationDuration": EXPIRATION_SECONDS},
        "runbookUrl": f"{RUNBOOK_URL}#{condition['runbook']}",
    }


def indicator_input(level: dict, account_id: int, create: bool) -> dict:
    events = {"validEvents": level["validEvents"]}
    events["goodEvents" if "goodEvents" in level else "badEvents"] = level.get("goodEvents") or level["badEvents"]
    if create:
        events["accountId"] = account_id
    return {
        "name": level["name"],
        "description": level.get("description", ""),
        "events": events,
        "objectives": [{"target": level["target"],
                        "timeWindow": {"rolling": {"count": level["windowDays"], "unit": "DAY"}}}],
    }


def dashboard_input(dashboard: dict, account_id: int, variables: dict) -> dict:
    pages = []
    for page in dashboard["pages"]:
        if not enabled(page, variables):
            continue
        pages.append({"name": page["name"], "widgets": [{
            "title": widget["title"],
            "layout": widget["layout"],
            "visualization": {"id": widget["visualization"]},
            "rawConfiguration": {"nrqlQueries": [{"accountIds": [account_id], "query": widget["query"]}]},
        } for widget in page["widgets"]]})
    return {"name": dashboard["name"], "description": dashboard.get("description", ""),
            "permissions": dashboard["permissions"], "pages": pages}


class Planner:
    """Casa por nome: cria o que falta e atualiza o resto. Em plan, so le."""

    def __init__(self, client, account_id: int, variables: dict, apply: bool, alert_email: str | None):
        self.client, self.account_id, self.variables = client, account_id, variables
        self.apply, self.alert_email = apply, alert_email
        self.actions: list[str] = []

    def record(self, action: str, kind: str, name: str):
        self.actions.append(f"{action:<8} {kind:<12} {name}")

    def run(self, config: dict):
        config = render(config, self.variables_for_render())
        policy_id = self.policy(config["alerts"]["policy"])
        self.conditions(config["alerts"]["conditions"], policy_id)
        self.notifications(config["alerts"]["notification"], policy_id)
        self.service_levels(config["service-levels"])
        self.dashboard(config["dashboard"])
        return self.actions

    def variables_for_render(self) -> dict:
        # Itens do front sao descartados antes do render quando o app Browser nao foi informado.
        return {key: value for key, value in self.variables.items() if value}

    def policy(self, policy: dict) -> str | None:
        found = self.client.query(Q_POLICIES, {"accountId": self.account_id, "name": policy["name"]})
        existing = by_name(found["actor"]["account"]["alerts"]["policiesSearch"]["policies"], policy["name"])
        if existing:
            self.record("ok", "politica", policy["name"])
            return existing["id"]
        self.record("criar", "politica", policy["name"])
        if not self.apply:
            return None
        return self.client.query(M_POLICY_CREATE, {"accountId": self.account_id, "policy": policy})["alertsPolicyCreate"]["id"]

    def conditions(self, conditions: list[dict], policy_id: str | None):
        existing = []
        if policy_id:
            found = self.client.query(Q_CONDITIONS, {"accountId": self.account_id, "policyId": policy_id})
            existing = found["actor"]["account"]["alerts"]["nrqlConditionsSearch"]["nrqlConditions"]
        for condition in conditions:
            if not enabled(condition, self.variables):
                self.record("ignorar", "condicao", f"{condition['name']} (sem {condition['requires']})")
                continue
            current = by_name(existing, condition["name"])
            self.record("atualizar" if current else "criar", "condicao", condition["name"])
            if not self.apply:
                continue
            if current:
                self.client.query(M_CONDITION_UPDATE, {"accountId": self.account_id, "id": current["id"],
                                                       "condition": condition_input(condition)})
            else:
                self.client.query(M_CONDITION_CREATE, {"accountId": self.account_id, "policyId": policy_id,
                                                       "condition": condition_input(condition)})

    def notifications(self, notification: dict, policy_id: str | None):
        if not self.alert_email:
            self.record("ignorar", "notificacao", "sem NEW_RELIC_ALERT_EMAIL")
            return
        account = {"accountId": self.account_id}
        destinations = self.client.query(Q_DESTINATIONS, {**account, "name": notification["destinationName"]})
        destination = by_name(destinations["actor"]["account"]["aiNotifications"]["destinations"]["entities"],
                              notification["destinationName"])
        self.record("ok" if destination else "criar", "destino", notification["destinationName"])
        if not destination and self.apply:
            created = self.client.query(M_DESTINATION_CREATE, {**account, "destination": {
                "type": "EMAIL", "name": notification["destinationName"],
                "properties": [{"key": "email", "value": self.alert_email}]}})
            destination = created_or_raise(created["aiNotificationsCreateDestination"], "destination", "destino")

        channels = self.client.query(Q_CHANNELS, {**account, "name": notification["channelName"]})
        channel = by_name(channels["actor"]["account"]["aiNotifications"]["channels"]["entities"],
                          notification["channelName"])
        self.record("ok" if channel else "criar", "canal", notification["channelName"])
        if not channel and self.apply:
            created = self.client.query(M_CHANNEL_CREATE, {**account, "channel": {
                "type": "EMAIL", "name": notification["channelName"], "product": "IINT",
                "destinationId": destination["id"], "properties": []}})
            channel = created_or_raise(created["aiNotificationsCreateChannel"], "channel", "canal")

        workflows = self.client.query(Q_WORKFLOWS, {**account, "name": notification["workflowName"]})
        workflow = by_name(workflows["actor"]["account"]["aiWorkflows"]["workflows"]["entities"],
                           notification["workflowName"])
        self.record("ok" if workflow else "criar", "workflow", notification["workflowName"])
        if not workflow and self.apply:
            created = self.client.query(M_WORKFLOW_CREATE, {**account, "workflow": {
                "name": notification["workflowName"], "workflowEnabled": True, "destinationsEnabled": True,
                "enrichmentsEnabled": False, "mutingRulesHandling": "NOTIFY_ALL_ISSUES",
                "issuesFilter": {"name": notification["workflowName"], "type": "FILTER", "predicates": [
                    {"attribute": "labels.policyIds", "operator": "EXACTLY_MATCHES", "values": [str(policy_id)]}]},
                "destinationConfigurations": [{"channelId": channel["id"]}]}})
            errors = created["aiWorkflowsCreateWorkflow"].get("errors") or []
            if errors:
                raise RuntimeError("workflow: " + "; ".join(e.get("description", "?") for e in errors))

    def service_levels(self, config: dict):
        found = self.client.query(Q_ENTITY, {"query": entity_query(config["entity"], "domain = 'APM' AND type = 'APPLICATION'", self.account_id)})
        entity = by_name(found["actor"]["entitySearch"]["results"]["entities"], config["entity"])
        if not entity:
            raise RuntimeError(f"aplicacao APM '{config['entity']}' nao encontrada na conta")
        current = self.client.query(Q_SERVICE_LEVELS, {"guid": entity["guid"]})["actor"]["entity"]["serviceLevel"]
        indicators = (current or {}).get("indicators") or []
        for level in config["serviceLevels"]:
            existing = by_name(indicators, level["name"])
            self.record("atualizar" if existing else "criar", "SLO", level["name"])
            if not self.apply:
                continue
            if existing:
                self.client.query(M_SLI_UPDATE, {"id": existing["id"],
                                                 "indicator": indicator_input(level, self.account_id, create=False)})
            else:
                self.client.query(M_SLI_CREATE, {"guid": entity["guid"],
                                                 "indicator": indicator_input(level, self.account_id, create=True)})

    def dashboard(self, dashboard: dict):
        found = self.client.query(Q_ENTITY, {"query": entity_query(dashboard["name"], "type = 'DASHBOARD'", self.account_id)})
        existing = by_name(found["actor"]["entitySearch"]["results"]["entities"], dashboard["name"])
        self.record("atualizar" if existing else "criar", "dashboard", dashboard["name"])
        if not self.apply:
            return
        payload = dashboard_input(dashboard, self.account_id, self.variables)
        if existing:
            result = self.client.query(M_DASHBOARD_UPDATE, {"guid": existing["guid"], "dashboard": payload})["dashboardUpdate"]
        else:
            result = self.client.query(M_DASHBOARD_CREATE, {"accountId": self.account_id, "dashboard": payload})["dashboardCreate"]
        if result.get("errors"):
            raise RuntimeError("dashboard: " + "; ".join(e.get("description", "?") for e in result["errors"]))


def prune(config: dict, variables: dict) -> dict:
    """Remove itens que dependem de variavel ausente antes do render (ex.: front sem app Browser)."""
    config = json.loads(json.dumps(config))
    config["alerts"]["conditions"] = [c if enabled(c, variables) else {**c, "query": ""} for c in config["alerts"]["conditions"]]
    config["dashboard"]["pages"] = [p for p in config["dashboard"]["pages"] if enabled(p, variables)]
    return config


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mode", choices=("validate", "plan", "apply"))
    args = parser.parse_args(argv)

    config = load_config()
    errors = validate(config)
    if errors:
        for error in errors:
            print(f"::error::{error}")
        return 1
    if args.mode == "validate":
        print(f"Configuracao valida: {len(config['alerts']['conditions'])} condicoes, "
              f"{len(config['service-levels']['serviceLevels'])} SLOs, {len(config['dashboard']['pages'])} paginas.")
        return 0

    api_key = os.environ.get("NEW_RELIC_API_KEY", "").strip()
    account = os.environ.get("NEW_RELIC_ACCOUNT_ID", "").strip()
    if not api_key or not account.isdigit():
        print("::error::defina NEW_RELIC_API_KEY (User key) e NEW_RELIC_ACCOUNT_ID")
        return 2
    variables = {"APM_APP": os.environ.get("NEW_RELIC_APM_APP_NAME", "").strip() or DEFAULT_APM_APP,
                 "BROWSER_APP": os.environ.get("NEW_RELIC_BROWSER_APP_NAME", "").strip()}
    planner = Planner(NerdGraph(api_key, os.environ.get("NEW_RELIC_REGION", "US").strip().upper() or "US"),
                      int(account), variables, apply=args.mode == "apply",
                      alert_email=os.environ.get("NEW_RELIC_ALERT_EMAIL", "").strip() or None)
    try:
        actions = planner.run(prune(config, variables))
    except (RuntimeError, ConfigError) as error:
        print(f"::error::{error}")
        return 1
    print(("Aplicado" if args.mode == "apply" else "Plano (nada foi alterado)") + ":")
    for action in actions:
        print(f"  {action}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
