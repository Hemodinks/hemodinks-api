from __future__ import annotations

import argparse
import json
import os
import re
import sys
import unicodedata
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
from urllib import request
from urllib.error import HTTPError

try:
    from pypdf import PdfReader
except ImportError:
    print("Missing dependency: pypdf. Install with: python -m pip install -r scripts/requirements-cbhpm.txt", file=sys.stderr)
    raise


CODE_RE = re.compile(r"^(?P<code>\d\.\d{2}\.\d{2}\.\d{2}-\d)\s*(?P<rest>.*)$")
GROUP_RE = re.compile(r"^(?P<name>.+?)\s+\(\d\.\d{2}\.\d{2}\.\d{2}-\d\)$")
GROUP_CODE_RE = re.compile(r"^(?P<name>.+?)\s+(?P<code>\d\.\d{2}\.\d{2}\.\d{2}-\d)$")
PORTE_TOKEN_RE = r"(?:\d{1,2}[ABC]|0,\d{1,2}\s+de\s+\d{1,2}[ABC])"
COST_TOKEN_RE = r"(?:\d{1,6},\d{3,4}|[-\u2013])"
PORTE_RE = re.compile(
    rf"(?P<porte>{PORTE_TOKEN_RE})(?:\s+(?P<custo>{COST_TOKEN_RE}))?"
    rf"(?P<extra>(?:\s+(?:\d+|[-\u2013*]|\d+,\d{{3,4}}))*)\s*$",
    re.IGNORECASE,
)
PORTE_FRACIONARIO_RE = re.compile(
    r"^(?P<fator>0,\d{1,2})\s+de\s+(?P<porte>\d{1,2}[ABC])$",
    re.IGNORECASE,
)

UCO_REFERENCIA = Decimal("14.33")
VALORES_PORTE_REFERENCIA = {
    "1A": Decimal("12.86"),
    "1B": Decimal("25.72"),
    "1C": Decimal("38.58"),
    "2A": Decimal("51.45"),
    "2B": Decimal("67.82"),
    "2C": Decimal("80.26"),
    "3A": Decimal("109.67"),
    "3B": Decimal("140.14"),
    "3C": Decimal("160.52"),
    "4A": Decimal("191.04"),
    "4B": Decimal("209.13"),
    "4C": Decimal("236.26"),
    "5A": Decimal("254.34"),
    "5B": Decimal("274.69"),
    "5C": Decimal("291.64"),
    "6A": Decimal("317.65"),
    "6B": Decimal("349.30"),
    "6C": Decimal("382.08"),
    "7A": Decimal("412.60"),
    "7B": Decimal("456.68"),
    "7C": Decimal("540.33"),
    "8A": Decimal("583.29"),
    "8B": Decimal("611.55"),
    "8C": Decimal("648.85"),
    "9A": Decimal("689.55"),
    "9B": Decimal("753.99"),
    "9C": Decimal("830.84"),
    "10A": Decimal("891.89"),
    "10B": Decimal("966.50"),
    "10C": Decimal("1072.75"),
    "11A": Decimal("1134.93"),
    "11B": Decimal("1244.58"),
    "11C": Decimal("1365.54"),
    "12A": Decimal("1415.27"),
    "12B": Decimal("1521.53"),
    "12C": Decimal("1864.04"),
    "13A": Decimal("2051.69"),
    "13B": Decimal("2250.64"),
    "13C": Decimal("2489.16"),
    "14A": Decimal("2774.02"),
    "14B": Decimal("3018.19"),
    "14C": Decimal("3329.05"),
}


def clean_text(value: str) -> str:
    value = re.sub(r"\s+", " ", value).strip()
    value = re.sub(r"\.{4,}", " ", value)
    value = re.sub(r"(?<=[^\W\d_])\.(?=[^\W\d_])", " ", value)
    value = re.sub(r"\s+", " ", value)
    return value.strip(" .")


def fold_text(value: str) -> str:
    normalized = unicodedata.normalize("NFD", value)
    ascii_value = "".join(char for char in normalized if unicodedata.category(char) != "Mn")
    return ascii_value.upper()


def should_skip_line(value: str) -> bool:
    line = value.strip()
    if not line:
        return True

    folded = fold_text(line)

    if folded.startswith("CODIGO PROCEDIMENTO PORTE"):
        return True

    if folded in {
        "CODIGO",
        "PROCEDIMENTO",
        "PORTE",
        "CUSTO",
        "OPER.",
        "CUSTO OPER.",
        "N\u00b0 DE",
        "N DE",
        "NO DE",
        "AUX.",
        "ANEST.",
        "FILME",
        "OU DOC.",
        "OU DOC. INCID.",
        "INCID.",
    }:
        return True

    if folded.startswith("CLASSIFICACAO BRASILEIRA"):
        return True

    if folded.startswith(("PROCEDIMENTOS CLINICOS", "PROCEDIMENTOS CIRURGICOS", "PROCEDIMENTOS DIAGNOSTICOS")):
        return True

    if re.match(r"^\d+\s*$", line):
        return True

    if folded.startswith("CAPITULO"):
        return True

    return False


def parse_cost(value: str | None) -> Decimal | None:
    if not value or value in {"-", "\u2013"}:
        return None

    return Decimal(value.replace(".", "").replace(",", "."))


def resolve_valor_porte(porte: str | None) -> Decimal | None:
    if not porte:
        return None

    normalized = porte.strip().upper()
    valor = VALORES_PORTE_REFERENCIA.get(normalized)
    if valor is not None:
        return valor

    match = PORTE_FRACIONARIO_RE.match(normalized)
    if not match:
        return None

    valor_base = VALORES_PORTE_REFERENCIA.get(match.group("porte").upper())
    if valor_base is None:
        return None

    fator = Decimal(match.group("fator").replace(",", "."))
    return valor_base * fator


def calculate_valor_referencia(porte: str | None, custo_operacional: Decimal | None) -> float | None:
    valor_porte = resolve_valor_porte(porte)
    if valor_porte is None:
        return None

    valor = valor_porte + ((custo_operacional or Decimal("0")) * UCO_REFERENCIA)
    return float(valor.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP))


def decimal_to_json_number(value: Decimal | None) -> float | None:
    return None if value is None else float(value)


def parse_pdf(pdf_path: Path) -> list[dict[str, object]]:
    reader = PdfReader(str(pdf_path))
    rows: list[dict[str, object]] = []
    current: dict[str, object] | None = None
    current_group: str | None = None

    def finalize_current() -> None:
        nonlocal current
        if not current:
            return

        text = clean_text(" ".join(current["parts"]))  # type: ignore[index]
        match = PORTE_RE.search(text)

        if match:
            porte = match.group("porte")
            custo = parse_cost(match.group("custo"))
            procedimento = clean_text(text[: match.start()])
        else:
            porte = None
            custo = None
            procedimento = text

        folded_procedimento = fold_text(procedimento)
        if not procedimento or not porte or folded_procedimento.startswith("OBSERV"):
            current = None
            return

        rows.append(
            {
                "codigo": current["code"],
                "procedimento": procedimento,
                "porte": porte,
                "custoOperacional": decimal_to_json_number(custo),
                "valorReferencia": calculate_valor_referencia(porte, custo),
                "capitulo": None,
                "grupo": current.get("group"),
            }
        )
        current = None

    for page_index, page in enumerate(reader.pages, start=1):
        for raw_line in (page.extract_text() or "").splitlines():
            line = raw_line.strip()
            if should_skip_line(line):
                continue

            group_match = GROUP_RE.match(line)
            group_code_match = GROUP_CODE_RE.match(line)
            if group_match or (group_code_match and not CODE_RE.match(line)):
                finalize_current()
                current_group = clean_text((group_match or group_code_match).group("name"))
                continue

            match = CODE_RE.match(line)
            if match:
                finalize_current()
                current = {
                    "code": match.group("code"),
                    "parts": [match.group("rest")],
                    "page": page_index,
                    "group": current_group,
                }
                continue

            if current:
                current["parts"].append(line)  # type: ignore[index,union-attr]

    finalize_current()

    deduped: dict[str, dict[str, object]] = {}
    for row in rows:
        deduped[str(row["codigo"])] = row

    return list(deduped.values())


def post_json(url: str, payload: object, token: str | None = None) -> dict[str, object]:
    body = json.dumps(payload).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"

    http_request = request.Request(url, data=body, headers=headers, method="POST")
    try:
        with request.urlopen(http_request) as response:
            return json.loads(response.read().decode("utf-8"))
    except HTTPError as error:
        details = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {error.code}: {details}") from error


def authenticate(api_url: str, email: str, password: str) -> str:
    response = post_json(
        f"{api_url}/api/users/authenticate",
        {"email": email, "senha": password},
    )
    token = response.get("token")
    if not isinstance(token, str) or not token:
        raise RuntimeError("Authentication did not return a token")

    return token


def main() -> int:
    parser = argparse.ArgumentParser(description="Import CBHPM rows from the local PDF into Hemodinks API.")
    parser.add_argument(
        "--pdf",
        default="docs/CBHPM-2022_versao-agosto-2023.pdf",
        help="Path to the CBHPM PDF",
    )
    parser.add_argument("--api-url", default=os.environ.get("HEMODINKS_API_URL", "http://localhost:5000"))
    parser.add_argument("--token", default=os.environ.get("HEMODINKS_TOKEN"))
    parser.add_argument("--email", default=os.environ.get("HEMODINKS_EMAIL"))
    parser.add_argument("--password", default=os.environ.get("HEMODINKS_PASSWORD"))
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--output-json", help="Optional path to write the parsed payload instead of posting it.")
    args = parser.parse_args()

    pdf_path = Path(args.pdf)
    if not pdf_path.exists():
        print(f"PDF not found: {pdf_path}", file=sys.stderr)
        return 1

    items = parse_pdf(pdf_path)
    print(f"Parsed {len(items)} CBHPM rows from {pdf_path}")

    payload = {"items": items}
    if args.output_json:
        Path(args.output_json).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Wrote {args.output_json}")

    if args.dry_run or args.output_json:
        for item in items[:5]:
            print(f"{item['codigo']} | {item['procedimento']} | {item['porte']} | {item['valorReferencia']}")
        return 0

    token = args.token
    if not token and args.email and args.password:
        token = authenticate(args.api_url.rstrip("/"), args.email, args.password)

    if not token:
        print("Provide --token or --email/--password for an administrator user.", file=sys.stderr)
        return 1

    result = post_json(f"{args.api_url.rstrip('/')}/api/cbhpm/import", payload, token)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
