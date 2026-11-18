"""Avalia o resultado do what-if do Bicep de infraestrutura (issue #288).

Le o JSON de `az deployment sub what-if --no-pretty-print --output json` e falha se houver
diferenca entre `infra/` e o Azure (drift). Nao chama a Azure.

Imprime apenas tipo/nome do recurso, caminho da propriedade e tipo de mudanca, nunca valores:
o repositorio e publico e os logs do Actions ficam visiveis.

Ruido conhecido do what-if (propriedades injetadas pelo provedor ou somente leitura, que nao
mudam nada num deploy) fica em KNOWN_NOISE. Cada entrada cobre um tipo de recurso, um caminho
exato e um tipo de mudanca; nao usar curinga.

Uso:
  python3 -B scripts/infra_what_if.py whatif.json
Saida: 0 sem drift; 1 com drift; 2 entrada invalida ou what-if com erro.
"""

import json
import os
import sys

# (tipo do recurso em minusculas, caminho da propriedade, propertyChangeType)
KNOWN_NOISE = {
    # Function App: o provedor injeta defaults de siteConfig que o GET de sites nao devolve.
    ("microsoft.web/sites", "properties.siteConfig.localMySqlEnabled", "Create"),
    ("microsoft.web/sites", "properties.siteConfig.netFrameworkVersion", "Create"),
    # Static Web App: campos somente leitura que nao podem ser declarados no template.
    ("microsoft.web/staticsites", "properties.stableInboundIP", "Delete"),
    ("microsoft.web/staticsites", "properties.trafficSplitting", "Delete"),
}

UNCHANGED = {"NoChange", "Ignore"}


class WhatIfError(Exception):
    pass


def resource_label(resource_id):
    """Converte o resourceId em (tipo, nome) sem a assinatura nem o resource group."""
    tail = resource_id.rsplit("/providers/", 1)[-1]
    segments = tail.split("/")
    if len(segments) < 3 or len(segments) % 2 == 0:
        raise WhatIfError(f"resourceId inesperado no what-if: {tail}")
    namespace, rest = segments[0], segments[1:]
    resource_type = "/".join([namespace] + rest[0::2])
    name = "/".join(rest[1::2])
    return resource_type, name


def flatten(delta, prefix=""):
    """Achata o delta aninhado em (caminho, propertyChangeType) das folhas."""
    for item in delta or []:
        path = item.get("path", "")
        full = f"{prefix}.{path}" if prefix and path else (prefix or path)
        children = item.get("children")
        if children:
            yield from flatten(children, full)
        else:
            yield full, item.get("propertyChangeType", "")


def find_drift(result):
    """Retorna a lista de diferencas reais (texto sem valores)."""
    if not isinstance(result, dict) or "changes" not in result:
        raise WhatIfError("JSON do what-if sem a lista 'changes'.")
    if result.get("error") or result.get("status") not in (None, "Succeeded"):
        raise WhatIfError(f"what-if terminou com status {result.get('status')!r}.")

    drift = []
    for change in result["changes"] or []:
        change_type = change.get("changeType", "")
        if change_type in UNCHANGED:
            continue
        resource_type, name = resource_label(change.get("resourceId", ""))
        if change_type != "Modify":
            # Create, Delete, Deploy e Unsupported sao sempre diferenca.
            drift.append(f"{change_type} {resource_type}/{name}")
            continue
        real = [
            (path, kind)
            for path, kind in flatten(change.get("delta"))
            if kind != "NoEffect" and (resource_type.lower(), path, kind) not in KNOWN_NOISE
        ]
        drift.extend(f"Modify {resource_type}/{name}: {path} ({kind})" for path, kind in real)
    return drift


def write_summary(lines):
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    with open(path, "a", encoding="utf-8") as summary:
        summary.write("### Infra what-if\n\n")
        if lines:
            summary.write("Diferencas entre `infra/` e o Azure:\n\n")
            summary.writelines(f"- `{line}`\n" for line in lines)
        else:
            summary.write("Sem diferencas entre `infra/` e o Azure.\n")


def main(argv):
    if len(argv) != 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2
    try:
        with open(argv[1], encoding="utf-8") as handle:
            result = json.load(handle)
        drift = find_drift(result)
    except (OSError, ValueError, WhatIfError) as error:
        print(f"::error::{error}", file=sys.stderr)
        return 2

    write_summary(drift)
    if drift:
        print("::error::Infraestrutura diverge de infra/ (drift):")
        for line in drift:
            print(f"  {line}")
        return 1
    print("Sem diferencas entre infra/ e o Azure.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
