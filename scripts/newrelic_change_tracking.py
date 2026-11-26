#!/usr/bin/env python3
"""Registra o deploy no Change Tracking do New Relic (marcador "Deployments" do APM).

Producao: job deploy-api, depois da promocao blue/green (APM "Hemodinks API").
Homologacao: job deploy do Publish Homologation, depois que o Render fica live (APM "Hemodinks API Confirmation").
Nunca bloqueia o deploy: sem configuracao, ou se a NerdGraph falhar, so avisa e sai com 0
(o deploy ja foi feito quando este passo roda).

Ambiente:
  NEW_RELIC_API_KEY       User key (NRAK-...), a mesma do workflow New Relic Observability. Nunca e impressa.
  NEW_RELIC_ACCOUNT_ID    conta New Relic.
  NEW_RELIC_REGION        US (padrao) ou EU.
  NEW_RELIC_APM_APP_NAME  padrao "Hemodinks API".
  DEPLOY_ENVIRONMENT      production (padrao, BLUE_GREEN) ou homologation (BASIC).
  CANDIDATE_REVISION      producao: revisao do Container Apps promovida (vai na descricao).
  RENDER_DEPLOY_ID        homologacao: id do deploy no Render (vai na descricao).
  GITHUB_SHA, GITHUB_ACTOR, GITHUB_SERVER_URL, GITHUB_REPOSITORY, GITHUB_RUN_ID  preenchidos pelo Actions.
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import newrelic_observability as nr  # noqa: E402  (NerdGraph, entity_query e regioes compartilhados)

M_DEPLOYMENT_CREATE = """mutation($deployment: ChangeTrackingDeploymentInput!) {
  changeTrackingCreateDeployment(deployment: $deployment) { deploymentId entityGuid } }"""
APM_KIND = "domain = 'APM' AND type = 'APPLICATION'"
ENVIRONMENTS = {"production": "BLUE_GREEN", "homologation": "BASIC"}


def description(env: dict) -> str:
    if env.get("DEPLOY_ENVIRONMENT", "").strip() == "homologation":
        render = env.get("RENDER_DEPLOY_ID", "").strip()
        return f"Homologacao: deploy Render {render}" if render else "Homologacao: deploy Render"
    revision = env.get("CANDIDATE_REVISION", "").strip()
    return f"Producao: revisao {revision} promovida" if revision else "Producao: revisao promovida"


def deployment_input(guid: str, env: dict) -> dict:
    sha = env.get("GITHUB_SHA", "").strip()
    if len(sha) != 40:
        raise nr.ConfigError("GITHUB_SHA ausente ou invalido")
    environment = env.get("DEPLOY_ENVIRONMENT", "").strip() or "production"
    if environment not in ENVIRONMENTS:
        raise nr.ConfigError("DEPLOY_ENVIRONMENT deve ser production ou homologation")
    deployment = {
        "entityGuid": guid,
        "version": sha[:7],
        "commit": sha,
        "deploymentType": ENVIRONMENTS[environment],
        "description": description(env),
    }
    if env.get("GITHUB_ACTOR"):
        deployment["user"] = env["GITHUB_ACTOR"]
    server, repo, run = (env.get(k, "").strip() for k in ("GITHUB_SERVER_URL", "GITHUB_REPOSITORY", "GITHUB_RUN_ID"))
    if server and repo and run:
        deployment["deepLink"] = f"{server}/{repo}/actions/runs/{run}"
    return deployment


def record(client, account_id: int, app_name: str, env: dict) -> str:
    found = client.query(nr.Q_ENTITY, {"query": nr.entity_query(app_name, APM_KIND, account_id)})
    entity = nr.by_name(found["actor"]["entitySearch"]["results"]["entities"], app_name)
    if not entity:
        raise RuntimeError(f"aplicacao APM '{app_name}' nao encontrada na conta {account_id}")
    result = client.query(M_DEPLOYMENT_CREATE, {"deployment": deployment_input(entity["guid"], env)})
    return result["changeTrackingCreateDeployment"]["deploymentId"]


def main(env: dict) -> int:
    api_key = env.get("NEW_RELIC_API_KEY", "").strip()
    account = env.get("NEW_RELIC_ACCOUNT_ID", "").strip()
    if not api_key or not account.isdigit():
        print("::notice::Marcador de deploy no New Relic nao registrado: defina NEW_RELIC_API_KEY e NEW_RELIC_ACCOUNT_ID.")
        return 0
    app_name = env.get("NEW_RELIC_APM_APP_NAME", "").strip() or nr.DEFAULT_APM_APP
    try:
        client = nr.NerdGraph(api_key, env.get("NEW_RELIC_REGION", "US").strip().upper() or "US")
        deployment_id = record(client, int(account), app_name, env)
    except (RuntimeError, nr.ConfigError, KeyError, TypeError, OSError) as error:
        print(f"::warning::Marcador de deploy no New Relic nao registrado: {error}")
        return 0
    print(f"Deploy registrado no New Relic para '{app_name}' (deploymentId {deployment_id}).")
    return 0


if __name__ == "__main__":
    sys.exit(main(dict(os.environ)))
