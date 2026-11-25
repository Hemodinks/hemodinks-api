#!/usr/bin/env python3
"""Filtros complementares das duas partes do "Build and test" da API.

Parte 2: so as classes listadas em ci-test-shard-2.txt. Parte 1: todo o resto (exceto LoginBrowserTests, que
roda no gate de login proprio). Juntas, executam cada teste exatamente uma vez; o gate "Build and test" confere
o resultado combinado (zero falhas, zero ignorados, IDs unicos e testes SQL obrigatorios).

    python3 -B scripts/ci_test_shards.py 1   # imprime o --filter da parte 1
"""
from pathlib import Path
import re
import sys

LIST = Path(__file__).with_name("ci-test-shard-2.txt")
EXCLUDED = "FullyQualifiedName!~LoginBrowserTests"
CLASS_NAME = re.compile(r"^HemodinksAPI\.Tests\.[A-Za-z_][A-Za-z0-9_]*$")


def shard_two_classes(path: Path = LIST) -> list[str]:
    classes = [line.strip() for line in path.read_text(encoding="utf-8").splitlines()
               if line.strip() and not line.lstrip().startswith("#")]
    for name in classes:
        if not CLASS_NAME.match(name) or "LoginBrowserTests" in name:
            raise ValueError(f"classe invalida na lista da parte 2: {name}")
    if len(set(classes)) != len(classes):
        raise ValueError("classe repetida na lista da parte 2")
    if not classes:
        raise ValueError("lista da parte 2 vazia")
    return classes


def test_filter(shard: int, classes: list[str]) -> str:
    # Ponto final: "X." nao casa com uma classe "XY" (FullyQualifiedName e Namespace.Classe.Metodo).
    if shard == 1:
        return "&".join([EXCLUDED] + [f"FullyQualifiedName!~{name}." for name in classes])
    if shard == 2:
        return "(" + "|".join(f"FullyQualifiedName~{name}." for name in classes) + ")"
    raise ValueError("parte deve ser 1 ou 2")


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in ("1", "2"):
        print("uso: ci_test_shards.py 1|2", file=sys.stderr)
        sys.exit(2)
    print(test_filter(int(sys.argv[1]), shard_two_classes()))
