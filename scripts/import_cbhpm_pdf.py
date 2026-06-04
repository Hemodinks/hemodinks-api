from __future__ import annotations

import argparse
import json
import os
import re
import sys
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
PORTE_RE = re.compile(r"(?P<porte>\d{1,2}[ABC])(?:\s+(?P<custo>(?:\d{1,6},\d{3}|-)))?$")


def clean_text(value: str) -> str:
    value = re.sub(r"\s+", " ", value).strip()
    value = re.sub(r"\.{4,}", " ", value)
    value = re.sub(r"(?<=[^\W\d_])\.(?=[^\W\d_])", " ", value)
    value = re.sub(r"\s+", " ", value)
    return value.strip(" .")


def should_skip_line(value: str) -> bool:
    line = value.strip()
    if not line:
        return True

    if "Procedimentos" in line and "Porte" in line:
        return True

    if line in {"Custo", "Oper.", "Custo Oper."}:
        return True

    if line.startswith("Classifica"):
        return True

    if re.match(r"^\d+\s*$", line):
        return True

    if line.upper().startswith("CAP"):
        return True

    return False


def parse_cost(value: str | None) -> float | None:
    if not value or value == "-":
        return None

    return float(value.replace(".", "").replace(",", "."))


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

        if not procedimento or not porte or procedimento.upper().startswith("OBSERV"):
            current = None
            return

        rows.append(
            {
                "codigo": current["code"],
                "procedimento": procedimento,
                "porte": porte,
                "custoOperacional": custo,
                "capitulo": None,
                "grupo": current.get("group"),
                "paginaPdf": current["page"],
            }
        )
        current = None

    for page_index, page in enumerate(reader.pages, start=1):
        if page_index < 23:
            continue

        for raw_line in (page.extract_text() or "").splitlines():
            line = raw_line.strip()
            if should_skip_line(line):
                continue

            group_match = GROUP_RE.match(line)
            if group_match:
                finalize_current()
                current_group = clean_text(group_match.group("name"))
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
    parser.add_argument("--pdf", default="Tabela-CBHPM-Geral.pdf", help="Path to Tabela-CBHPM-Geral.pdf")
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
            print(f"{item['codigo']} | {item['procedimento']} | {item['porte']}")
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
