"""Ensaio local de release: dados criados pela API da main, verificacao pela API da developer (#398).

Uso (passo a passo em docs/release-rehearsal-local.md):
  python -I scripts/release_rehearsal.py seed   <url_api_main>      <estado.json>
  python -I scripts/release_rehearsal.py verify <url_api_developer> <estado.json>

Variaveis: HEMODINKS_REHEARSAL_ADMIN_EMAIL (admin do seed, SuperAdmin da plataforma) e
HEMODINKS_REHEARSAL_SEED_PASSWORD (o Seed:InitialPassword da API), so no seed.

Cria clinica, troca senha e liga MFA: so aceita API em localhost. O arquivo de estado guarda as
senhas geradas; fica fora do repositorio e deve ser apagado no fim. Nunca imprime token, senha ou CPF.
"""
import base64
import hashlib
import hmac
import json
import os
import secrets
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ORIGIN = "http://localhost:5173"
LOCAL_HOSTS = {"localhost", "127.0.0.1", "::1"}
CLINICA_B_SLUG = "ensaio-clinica-b"
ADMIN_B_EMAIL = "admin.ensaio.b@hemodinks.local"
MOTIVO = "Ensaio de release"


class RehearsalError(Exception):
    pass


def require_local(base_url):
    host = urllib.parse.urlsplit(base_url).hostname
    if host not in LOCAL_HOSTS:
        raise RehearsalError(f"recusado: o ensaio altera dados e so roda contra localhost (recebido: {host})")
    return base_url.rstrip("/")


def require_outside_repo(state_path):
    path = Path(state_path).resolve()
    if path == ROOT or ROOT in path.parents:
        raise RehearsalError("recusado: o arquivo de estado guarda senhas; use um caminho fora do repositorio")
    return path


def cpf(seed):
    digits = [int(c) for c in f"{seed:09d}"[-9:]]
    for size in (10, 11):
        rest = sum(v * (size - i) for i, v in enumerate(digits)) % 11
        digits.append(0 if rest < 2 else 11 - rest)
    return "".join(map(str, digits))


def cnpj(seed):
    digits = [int(c) for c in f"{seed:08d}0001"]
    for weights in ([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]):
        rest = sum(a * b for a, b in zip(digits, weights)) % 11
        digits.append(0 if rest < 2 else 11 - rest)
    return "".join(map(str, digits))


def totp(secret_b32, at=None):
    """RFC 6238 (SHA-1, 30 s, 6 digitos), o mesmo formato dos apps autenticadores."""
    key = base64.b32decode(secret_b32.upper() + "=" * (-len(secret_b32) % 8))
    counter = int((time.time() if at is None else at) // 30)
    digest = hmac.new(key, struct.pack(">Q", counter), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    return f"{(struct.unpack('>I', digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000:06d}"


def wait_next_totp_window():
    # O servidor recusa reutilizar o passo TOTP ja usado.
    time.sleep(31 - (time.time() % 30))


def strong_password():
    return f"Ens-{secrets.token_hex(8)}-{secrets.choice('ABCDEFGH')}9!"


def items(payload):
    if isinstance(payload, list):
        return payload
    if isinstance(payload, dict):
        for key in ("items", "data", "pacientes", "itens"):
            if isinstance(payload.get(key), list):
                return payload[key]
    return []


class Api:
    def __init__(self, base):
        self.base, self.token, self.cookies = base, None, {}

    def call(self, method, path, body=None, headers=None, retry_429=True):
        h = {"Content-Type": "application/json", "Origin": ORIGIN, "X-Session-Refresh": "1"}
        if self.token:
            h["Authorization"] = f"Bearer {self.token}"
        if self.cookies:
            h["Cookie"] = "; ".join(f"{k}={v}" for k, v in self.cookies.items())
        h.update(headers or {})
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method, headers=h)
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                status, raw, hdrs = resp.status, resp.read(), resp.headers
        except urllib.error.HTTPError as err:
            status, raw, hdrs = err.code, err.read(), err.headers
        for cookie in hdrs.get_all("Set-Cookie") or []:
            name, _, rest = cookie.partition("=")
            self.cookies[name.strip()] = rest.split(";", 1)[0]
        try:
            payload = json.loads(raw) if raw else None
        except ValueError:
            payload = None
        if status == 429 and retry_429:
            wait = int((payload or {}).get("retryAfterSeconds") or hdrs.get("Retry-After") or 60)
            print(f"   limite de taxa (429); aguardando {wait}s", flush=True)
            time.sleep(min(wait, 300) + 1)
            return self.call(method, path, body, headers, retry_429=False)
        return status, payload

    def login(self, email, senha, slug=None):
        """Retorna (status, resposta). Com MFA ativo a resposta traz mfaDesafio e nenhum token."""
        self.token, self.cookies = None, {}
        status, ctx = self.call("POST", "/api/users/login-context", {"email": email, "senha": senha})
        if status != 200:
            return status, ctx
        clinicas = (ctx or {}).get("clinicas") or []
        if slug is None and len(clinicas) == 1:
            slug = clinicas[0].get("slug") or clinicas[0].get("clinicaSlug")
        status, resp = self.call("POST", "/api/users/authenticate", {"email": email, "senha": senha},
                                 {"X-Clinica-Slug": slug} if slug else None)
        if status == 200 and isinstance(resp, dict) and resp.get("token"):
            self.token = resp["token"]
        return status, resp


class Report:
    def __init__(self):
        self.results = []

    def check(self, name, ok, detail=""):
        self.results.append((name, bool(ok)))
        print(f"[{'OK' if ok else 'FALHOU'}] {name}" + (f" - {detail}" if detail else ""), flush=True)
        return ok

    def finish(self):
        failed = sum(1 for _, ok in self.results if not ok)
        print(f"\nResumo: {len(self.results) - failed}/{len(self.results)} OK")
        return 1 if failed else 0


def error_text(payload):
    return json.dumps(payload, ensure_ascii=True)[:300] if payload else ""


def change_first_password(api, user_id, current, report, label):
    new = strong_password()
    status, _ = api.call("PUT", f"/api/users/{user_id}/password",
                         {"userId": user_id, "senhaAtual": current, "novaSenha": new})
    report.check(f"main: troca de senha do primeiro acesso ({label})", status in (200, 204), f"HTTP {status}")
    return new if status in (200, 204) else current


def create_medico(api, tag, seed):
    status, user = api.call("POST", "/api/users/", {
        "nome": f"Medico Ensaio {tag}", "email": f"medico.ensaio.{tag.lower()}@hemodinks.local",
        "telefone": f"+5581990000{seed % 1000:03d}", "cpf": cpf(seed), "crm": str(seed % 100000), "crmUf": "PE"})
    if status not in (200, 201):
        print(f"   criar medico: HTTP {status} {error_text(user)}")
        return None
    return user.get("id")


def create_patient(api, name, seed, medico_id):
    status, patient = api.call("POST", "/api/pacientes/", {
        "nomePaciente": name, "cpf": cpf(seed), "tipoCirurgia": "Eletiva", "medicoUserId": medico_id,
        "dataAtendimento": time.strftime("%Y-%m-%d"), "diagnostico": MOTIVO})
    if status not in (200, 201):
        print(f"   criar paciente: HTTP {status} {error_text(patient)}")
        return None
    return patient.get("id")


def seed_clinic_data(api, st, tag, base_seed, count, report):
    medico = st.get(f"medico{tag}") or create_medico(api, tag, base_seed + 99)
    st[f"medico{tag}"] = medico
    if not report.check(f"main: medico da clinica {tag}", medico, str(medico)):
        return
    key = f"pacientes{tag}"
    if len(st.get(key) or []) >= count:
        report.check(f"main: pacientes da clinica {tag} (ja criados)", True, str(st[key]))
        return
    created = [create_patient(api, f"Ensaio {tag}{i}", base_seed + i, medico) for i in range(1, count + 1)]
    st[key] = [pid for pid in created if pid]
    report.check(f"main: pacientes criados na clinica {tag}", len(st[key]) == count, str(st[key]))


def seed(base, state_path, report):
    admin_email = os.environ.get("HEMODINKS_REHEARSAL_ADMIN_EMAIL")
    initial_pw = os.environ.get("HEMODINKS_REHEARSAL_SEED_PASSWORD")
    if not admin_email:
        raise RehearsalError("defina HEMODINKS_REHEARSAL_ADMIN_EMAIL")
    st = json.loads(state_path.read_text()) if state_path.exists() else {"admin_email": admin_email}

    def save():
        state_path.write_text(json.dumps(st))

    api = Api(base)
    if "admin_pw" not in st:
        if not initial_pw:
            raise RehearsalError("defina HEMODINKS_REHEARSAL_SEED_PASSWORD")
        status, resp = api.login(admin_email, initial_pw)
        if not report.check("main: login do admin A com a senha inicial", status == 200, f"HTTP {status}"):
            return
        st["admin_pw"] = change_first_password(api, resp["id"], initial_pw, report, "admin A")
        save()
    status, resp = api.login(admin_email, st["admin_pw"], st.get("clinicaA_slug"))
    if not report.check("main: login do admin A", status == 200 and not resp.get("precisaTrocarSenha"), f"HTTP {status}"):
        return
    st["clinicaA_slug"] = resp.get("clinicaSlug")

    if "adminB_pw" not in st:
        st["adminB_pw"] = strong_password()
        status, resp = api.call("POST", "/api/platform/clinicas/", {
            "nome": "Clinica Ensaio B", "slug": CLINICA_B_SLUG, "cnpj": cnpj(secrets.randbelow(89_999_999) + 10_000_000),
            "administradorNome": "Admin Ensaio B", "administradorEmail": ADMIN_B_EMAIL,
            "administradorSenha": st["adminB_pw"], "administradorTelefone": None, "plano": None,
            "modulosLiberados": None, "assinaturaStatus": None, "trialAte": None, "assinaturaValidaAte": None,
            "limiteUsuarios": None, "fotoClinica": None, "equipeInicial": None})
        if not report.check("main: clinica B criada pela plataforma", status in (200, 201), f"HTTP {status} {error_text(resp) if status >= 400 else ''}"):
            return
        save()

    seed_clinic_data(api, st, "A", 700000, 3, report)
    save()

    api_b = Api(base)
    status, resp = api_b.login(ADMIN_B_EMAIL, st["adminB_pw"])
    if not report.check("main: login do admin B", status == 200, f"HTTP {status}"):
        return
    if resp.get("precisaTrocarSenha"):
        st["adminB_pw"] = change_first_password(api_b, resp["id"], st["adminB_pw"], report, "admin B")
        save()
        api_b.login(ADMIN_B_EMAIL, st["adminB_pw"])
    seed_clinic_data(api_b, st, "B", 800000, 2, report)
    save()


def verify(base, state_path, report):
    st = json.loads(state_path.read_text())
    a, b = Api(base), Api(base)
    status, _ = a.login(st["admin_email"], st["admin_pw"], st["clinicaA_slug"])
    report.check("developer: login do admin A (senha definida antes da migration)", status == 200 and a.token, f"HTTP {status}")
    status, resp = b.login(ADMIN_B_EMAIL, st["adminB_pw"])
    if isinstance(resp, dict) and resp.get("mfaDesafio"):
        raise RehearsalError("o admin B ficou com MFA ativo de uma execucao interrompida; recrie a base do ensaio")
    report.check("developer: login do admin B", status == 200 and b.token, f"HTTP {status}")

    ids_a_esperados, ids_b_esperados = set(st["pacientesA"]), set(st["pacientesB"])
    status, page = a.call("GET", "/api/pacientes/?page=1&pageSize=100")
    ids_a = {x.get("id") for x in items(page)}
    report.check("developer: clinica A lista os pacientes criados antes da migration",
                 status == 200 and ids_a_esperados <= ids_a, f"HTTP {status}, {len(ids_a)} itens")
    report.check("developer: clinica A nao ve pacientes da B", not (ids_b_esperados & ids_a))
    status, page = b.call("GET", "/api/pacientes/?page=1&pageSize=100")
    ids_b = {x.get("id") for x in items(page)}
    report.check("developer: clinica B lista so os seus", status == 200 and ids_b == ids_b_esperados, f"HTTP {status}, {len(ids_b)} itens")

    alvo = st["pacientesA"][0]
    for method, path, body, label in (("GET", f"/api/pacientes/{alvo}", None, "lendo"),
                                      ("DELETE", f"/api/pacientes/{alvo}", {"motivo": MOTIVO}, "inativando"),
                                      ("POST", f"/api/pacientes/{alvo}/reativacao", {"motivo": MOTIVO}, "reativando")):
        status, _ = b.call(method, path, body)
        report.check(f"developer: B {label} paciente de A -> 404", status == 404, f"HTTP {status}")
    status, _ = a.call("GET", f"/api/pacientes/{alvo}")
    report.check("developer: tentativas da B nao alteraram o paciente da A", status == 200, f"HTTP {status}")

    status, _ = a.call("DELETE", f"/api/pacientes/{alvo}")
    report.check("developer: inativar sem motivo e recusado", status == 400, f"HTTP {status}")
    status, _ = a.call("DELETE", f"/api/pacientes/{alvo}", {"motivo": MOTIVO})
    report.check("developer: A inativa paciente antigo", status in (200, 204), f"HTTP {status}")
    status, page = a.call("GET", "/api/pacientes/?page=1&pageSize=100")
    report.check("developer: inativado sai da lista padrao", alvo not in {x.get("id") for x in items(page)})
    status, _ = a.call("POST", f"/api/pacientes/{alvo}/reativacao", {"motivo": MOTIVO})
    report.check("developer: A reativa paciente", status in (200, 204), f"HTTP {status}")
    status, page = a.call("GET", "/api/pacientes/?page=1&pageSize=100")
    report.check("developer: reativado volta a lista padrao", alvo in {x.get("id") for x in items(page)})

    status, trilha = a.call("GET", "/api/auditoria-clinica?page=1&pageSize=50")
    acoes = {str(x.get("acao")) for x in items(trilha)}
    report.check("developer: trilha da clinica A responde", status == 200, f"HTTP {status}, {len(items(trilha))} eventos")
    report.check("developer: trilha registra inativar e reativar",
                 "paciente.inativar" in acoes and "paciente.reativar" in acoes, ", ".join(sorted(acoes))[:200])
    report.check("developer: trilha da A nao mostra acoes da B",
                 not any(x.get("usuarioNome") == "Admin Ensaio B" for x in items(trilha)))

    # pacienteId na trilha so ecoa o ID da URL: comparar com um ID inexistente prova que nao ha oraculo.
    b.call("GET", "/api/pacientes/999999")
    status, trilha_b = b.call("GET", "/api/auditoria-clinica?page=1&pageSize=50")
    sobre_alvo = [x for x in items(trilha_b) if str(x.get("recursoId")) == str(alvo)]
    report.check("developer: trilha da B so tem as proprias tentativas negadas sobre o ID da A",
                 status == 200 and sobre_alvo and all(not x.get("sucesso") and x.get("statusCode") == 404 for x in sobre_alvo),
                 f"HTTP {status}, {len(sobre_alvo)} registros")

    def forma(recurso_id):
        for x in items(trilha_b):
            if str(x.get("recursoId")) == recurso_id and x.get("acao") == "paciente.ler":
                return (x.get("statusCode"), x.get("sucesso"), x.get("pacienteNome"),
                        str(x.get("pacienteId")) == str(x.get("recursoId")))
        return None
    da_a, inexistente = forma(str(alvo)), forma("999999")
    report.check("developer: trilha da B nao distingue paciente da A de ID inexistente, sem nome",
                 da_a is not None and da_a == inexistente and da_a[2] is None, f"{da_a} vs {inexistente}")

    verify_mfa(base, st, b, report)


def verify_mfa(base, st, b, report):
    status, _ = b.call("GET", "/api/users/me/mfa")
    report.check("developer: estado de MFA responde", status == 200, f"HTTP {status}")
    status, cfg = b.call("POST", "/api/users/me/mfa/configuracao", {})
    secret = cfg.get("segredo") if isinstance(cfg, dict) else None
    if not report.check("developer: configuracao de MFA gera segredo", status == 200 and secret, f"HTTP {status}"):
        return
    status, resp = b.call("POST", "/api/users/me/mfa/ativacao", {"senha": st["adminB_pw"], "codigo": totp(secret)})
    if not report.check("developer: ativacao de MFA com TOTP", status == 200, f"HTTP {status} {error_text(resp) if status >= 400 else ''}"):
        return
    try:
        mfa_login = Api(base)
        status, resp = mfa_login.login(ADMIN_B_EMAIL, st["adminB_pw"])
        desafio = (resp or {}).get("mfaDesafio") if isinstance(resp, dict) else None
        report.check("developer: login com MFA ativo exige segundo fator, sem token",
                     status == 200 and desafio and not resp.get("token"), f"HTTP {status}")
        if not desafio:
            return
        slug = {"X-Clinica-Slug": CLINICA_B_SLUG}
        token = desafio.get("token")
        status, _ = mfa_login.call("POST", "/api/users/authenticate/mfa", {"desafio": token, "codigo": "000000"}, slug)
        report.check("developer: codigo MFA errado e recusado", status in (400, 401, 403), f"HTTP {status}")
        wait_next_totp_window()
        status, resp = mfa_login.call("POST", "/api/users/authenticate/mfa", {"desafio": token, "codigo": totp(secret)}, slug)
        ok = report.check("developer: codigo MFA correto conclui o login",
                          status == 200 and isinstance(resp, dict) and resp.get("token"), f"HTTP {status}")
        if ok:
            mfa_login.token = resp["token"]
            status, _ = mfa_login.call("GET", "/api/pacientes/?page=1&pageSize=5")
            report.check("developer: sessao aberta com MFA acessa pacientes da B", status == 200, f"HTTP {status}")
            b = mfa_login
    finally:
        # Desliga o MFA para o ensaio poder rodar de novo na mesma base.
        wait_next_totp_window()
        status, _ = b.call("POST", "/api/users/me/mfa/desativacao", {"senha": st["adminB_pw"], "codigo": totp(secret)})
        report.check("developer: desativacao de MFA", status in (200, 204), f"HTTP {status}")


def main(argv):
    if len(argv) != 4 or argv[1] not in ("seed", "verify"):
        print(__doc__)
        return 2
    report = Report()
    try:
        base = require_local(argv[2])
        state_path = require_outside_repo(argv[3])
        (seed if argv[1] == "seed" else verify)(base, state_path, report)
    except RehearsalError as err:
        print(f"ERRO: {err}")
        return 2
    return report.finish()


if __name__ == "__main__":
    sys.exit(main(sys.argv))
