"""Read-only deployment preflight. Never print credentials or response bodies."""
import json
import os
import sys
from urllib.request import Request, urlopen


class PreflightError(Exception):
    pass


def validate(service, variables, expected_host, connection):
    if service.get("serviceDetails", {}).get("url") != "https://" + expected_host:
        raise PreflightError("Destino Render diferente da homologacao esperada.")
    if service.get("branch") != "developer":
        raise PreflightError("A branch do servico Render deve ser developer.")
    if service.get("autoDeploy") != "no":
        raise PreflightError("Desligue Auto-Deploy no servico Render de homologacao.")
    values = {item["envVar"]["key"]: item["envVar"].get("value") for item in variables}
    for key in ("Database__RunMigrationsOnStartup", "Database__RunMaintenanceOnStartup",
                "Seed__CbhpmOnStartup", "Seed__UsersOnStartup"):
        if key not in values:
            raise PreflightError(f"Configure {key}=false diretamente no servico Render (variavel nao encontrada).")
        if values[key] != "false":
            raise PreflightError(f"Configure {key}=false no servico Render.")
    if "ConnectionStrings__DefaultConnection" not in values:
        raise PreflightError("ConnectionStrings__DefaultConnection nao encontrada diretamente no servico Render.")
    if values["ConnectionStrings__DefaultConnection"] != connection:
        raise PreflightError("HOMOLOGATION_SQL_CONNECTION_STRING difere da conexao configurada no Render. Copie o valor exato, sem espacos ou quebras extras.")


def main():
    names = {"RENDER_API_KEY": "HOMOLOGATION_RENDER_API_KEY",
             "RENDER_SERVICE_ID": "HOMOLOGATION_RENDER_SERVICE_ID",
             "ConnectionStrings__DefaultConnection": "HOMOLOGATION_SQL_CONNECTION_STRING"}
    for key, setting in names.items():
        if not os.environ.get(key):
            raise PreflightError(f"Configure {setting} no environment GitHub homologation.")
    base = "https://api.render.com/v1/services/" + os.environ["RENDER_SERVICE_ID"]

    def read(url):
        request = Request(url, headers={"Authorization": "Bearer " + os.environ["RENDER_API_KEY"]})
        try:
            with urlopen(request, timeout=30) as response:
                return json.load(response)
        except Exception:
            raise PreflightError("Falha ao consultar Render. Verifique chave, permissoes e disponibilidade da API.") from None

    service = read(base)
    variables = read(base + "/env-vars?limit=100")
    # Fail closed on truncation rather than ignoring missing environment variables.
    if len(variables) >= 100:
        raise PreflightError("Lista Render atingiu limite de 100 variaveis; revisar paginacao antes do deploy.")
    validate(service, variables, os.environ["EXPECTED_RENDER_HOST"],
             os.environ["ConnectionStrings__DefaultConnection"])
    print("Preflight aprovado: destino, branch, auto-deploy, flags e conexao correspondem. Nenhum banco foi acessado.")


if __name__ == "__main__":
    try:
        main()
    except PreflightError as error:
        print("::error::" + str(error), file=sys.stderr)
        sys.exit(1)
    except Exception:
        print("::error::Resposta Render inesperada; bloqueado sem exibir dados sensiveis.", file=sys.stderr)
        sys.exit(1)
