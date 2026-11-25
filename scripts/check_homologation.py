"""Read-only deployment preflight. Never print credentials or response bodies."""
import json
import os
import sys
from urllib.request import Request, urlopen


class PreflightError(Exception):
    pass


# Homologacao usa a conta de storage compartilhada com containers proprios (render.confirmation.yaml).
# Sem connection string a API grava no disco efemero do Render e fotos/arquivos somem a cada deploy ou hibernacao.
STORAGE_CONTAINERS = {
    "AzureStorage__ContainerName": "profile-photos-confirmation",
    "AzureStorage__PatientFilesContainerName": "patient-files-confirmation",
}


# Agente New Relic (APM) da homologacao: valores nao secretos podem aparecer no diagnostico; a chave nunca.
NEW_RELIC_EXPECTED = {
    "CORECLR_ENABLE_PROFILING": "1",
    "NEW_RELIC_APP_NAME": "Hemodinks API Confirmation",
    "NEW_RELIC_DISTRIBUTED_TRACING_ENABLED": "true",
    "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_ENABLED": "true",
    "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_LOG_LEVEL_DENYLIST": "VERBOSE,TRACE,DEBUG,INFORMATION,INFO",
    "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_CONTEXT_DATA_ENABLED": "true",
    "NEW_RELIC_APPLICATION_LOGGING_FORWARDING_CONTEXT_DATA_INCLUDE":
        "RequestId,ClinicId,UsuariosOnline,TotalSessoes,Clinicas,SnapshotClinicaId,SnapshotUsuarios,SnapshotSessoes",
}
NEW_RELIC_SECRETS = {"NEW_RELIC_LICENSE_KEY"}
NEW_RELIC_PREFIXES = ("NEW_RELIC", "NEWRELIC", "CORECLR", "NRIA")


def newrelic_report(service, values):
    """Linhas de diagnostico do agente no Render. So nomes e valores nao secretos; nunca a chave."""
    details = service.get("serviceDetails", {})
    lines = [f"Render: plano={details.get('plan', '?')} regiao={details.get('region', '?')} "
             f"instancias={details.get('numInstances', '?')}"]
    for key in sorted(NEW_RELIC_SECRETS):
        lines.append(f"{key}: {'definida' if values.get(key) else 'AUSENTE'}")
    for key, expected in NEW_RELIC_EXPECTED.items():
        current = values.get(key)
        status = "ok" if current == expected else ("AUSENTE" if current is None else "DIFERENTE")
        shown = "" if current is None else f" ({current[:120]})"
        lines.append(f"{key}: {status}{shown}")
    known = set(NEW_RELIC_EXPECTED) | NEW_RELIC_SECRETS
    extras = sorted(key for key in values if key.upper().startswith(NEW_RELIC_PREFIXES) and key not in known)
    lines.append("Outras variaveis do agente (so nomes): " + (", ".join(extras) if extras else "nenhuma"))
    return lines


def storage_account(connection_string):
    """Nome da conta na connection string (AccountName ou BlobEndpoint), sem expor a chave."""
    parts = {}
    for segment in (connection_string or "").split(";"):
        key, separator, value = segment.partition("=")
        if separator:
            parts[key.strip().lower()] = value.strip()
    if parts.get("accountname"):
        return parts["accountname"].lower()
    endpoint = parts.get("blobendpoint", "")
    host = endpoint.split("://", 1)[-1].split("/", 1)[0]
    return host.split(".", 1)[0].lower() if host.endswith(".blob.core.windows.net") else None


def validate_storage(values, expected_account):
    connection_string = values.get("AzureStorage__ConnectionString")
    if not connection_string or not connection_string.strip():
        raise PreflightError("AzureStorage__ConnectionString vazia ou ausente no servico Render: fotos e arquivos "
                             "iriam para o disco efemero e sumiriam no proximo deploy.")
    if storage_account(connection_string) != expected_account.lower():
        raise PreflightError(f"AzureStorage__ConnectionString do Render nao aponta para a conta {expected_account}.")
    for key, container in STORAGE_CONTAINERS.items():
        if values.get(key) != container:
            raise PreflightError(f"Configure {key}={container} no servico Render (homologacao nunca usa o container de producao).")


def validate(service, variables, expected_host, connection, expected_storage_account):
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
        raise PreflightError("HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING difere da conexao configurada no Render. Copie o valor exato, sem espacos ou quebras extras.")
    validate_storage(values, expected_storage_account)


def main():
    names = {"RENDER_API_KEY": "HOMOLOGATION_RENDER_API_KEY",
             "RENDER_SERVICE_ID": "HOMOLOGATION_RENDER_SERVICE_ID",
             "ConnectionStrings__DefaultConnection": "HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING"}
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
    if not os.environ.get("EXPECTED_STORAGE_ACCOUNT"):
        raise PreflightError("Defina EXPECTED_STORAGE_ACCOUNT no workflow.")
    validate(service, variables, os.environ["EXPECTED_RENDER_HOST"],
             os.environ["ConnectionStrings__DefaultConnection"], os.environ["EXPECTED_STORAGE_ACCOUNT"])
    if os.environ.get("NEW_RELIC_DIAGNOSTICS") == "1":
        values = {item["envVar"]["key"]: item["envVar"].get("value") for item in variables}
        print("Diagnostico New Relic (somente leitura):")
        for line in newrelic_report(service, values):
            print("  " + line)
    print("Preflight aprovado: destino, branch, auto-deploy, flags, conexao SQL e storage correspondem. Nenhum banco foi acessado.")


if __name__ == "__main__":
    try:
        main()
    except PreflightError as error:
        print("::error::" + str(error), file=sys.stderr)
        sys.exit(1)
    except Exception:
        print("::error::Resposta Render inesperada; bloqueado sem exibir dados sensiveis.", file=sys.stderr)
        sys.exit(1)
