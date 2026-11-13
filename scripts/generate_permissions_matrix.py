from __future__ import annotations

"""Gera docs/MATRIZ_PERFIS_PERMISSOES.md e docs/MATRIZ_PERFIS_PERMISSOES_VISUAL.html.

Fonte unica da matriz de perfis x permissoes. A verdade de seguranca esta no codigo:
politicas em HemodinksAPI.Api/ApiServiceCollectionExtensions.Auth.cs, RequireAuthorization dos
endpoints, ClinicaModuleAccessMiddleware e HemodinksAPI.Tests/ApiAuthorizationMatrixTests.cs.
Atualize os dados abaixo junto com essas fontes e gere o PDF com:

    python3 scripts/generate_permissions_matrix.py
    google-chrome --headless --no-pdf-header-footer \
      --print-to-pdf=docs/MATRIZ_PERFIS_PERMISSOES_VISUAL.pdf docs/MATRIZ_PERFIS_PERMISSOES_VISUAL.html
"""

import html
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"
UPDATED = "12/11/2026"

CSS = r"""
    :root {
      --navy: #071a2d;
      --navy-2: #0d2942;
      --teal: #0f8279;
      --blue: #2563eb;
      --purple: #7c3aed;
      --orange: #c46508;
      --red: #c92a2a;
      --green: #16845b;
      --ink: #172033;
      --muted: #5e6a7c;
      --line: #d7dee8;
      --soft: #f3f6fa;
    }

    * { box-sizing: border-box; }
    html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    body { margin: 0; font-family: "Segoe UI", Arial, sans-serif; color: var(--ink); background: #dfe7ef; }
    @page { size: A4 landscape; margin: 9mm; }

    .page {
      width: 279mm;
      min-height: 192mm;
      margin: 8mm auto;
      padding: 11mm 12mm 9mm;
      background: white;
      position: relative;
      overflow: hidden;
      page-break-after: always;
      box-shadow: 0 12px 35px rgba(7, 26, 45, .16);
    }
    .page:last-child { page-break-after: auto; }
    .page::before { content: ""; position: absolute; inset: 0 0 auto; height: 5px; background: linear-gradient(90deg, var(--teal), var(--blue), var(--purple)); }
    .footer { position: absolute; bottom: 5mm; left: 12mm; right: 12mm; display: flex; justify-content: space-between; color: #758195; font-size: 8px; }

    h1, h2, h3, p { margin-top: 0; }
    h1 { font-size: 29px; letter-spacing: -.6px; color: white; margin-bottom: 8px; }
    h2 { font-size: 21px; color: var(--navy); margin-bottom: 5px; }
    h3 { font-size: 13px; color: var(--navy); margin-bottom: 6px; }
    .subtitle { color: #d6e7f3; font-size: 13px; max-width: 175mm; line-height: 1.5; }
    .eyebrow { text-transform: uppercase; letter-spacing: 1.4px; font-size: 8px; font-weight: 800; color: var(--teal); }
    .lead { color: var(--muted); font-size: 10px; line-height: 1.45; margin-bottom: 10px; }

    .cover { background: linear-gradient(135deg, #061728 0%, #0a3550 58%, #0c6e69 100%); color: white; padding: 17mm; }
    .cover::after { content: ""; position: absolute; width: 110mm; height: 110mm; border-radius: 50%; right: -28mm; top: -35mm; border: 25px solid rgba(255,255,255,.05); }
    .brand { font-size: 11px; font-weight: 900; letter-spacing: 1.7px; color: #7ee2d7; margin-bottom: 20mm; }
    .cover-grid { display: grid; grid-template-columns: 1.3fr .7fr; gap: 20mm; align-items: center; }
    .stat-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
    .stat { padding: 12px; border: 1px solid rgba(255,255,255,.18); background: rgba(255,255,255,.08); border-radius: 10px; }
    .stat strong { display: block; font-size: 25px; color: #fff; }
    .stat span { font-size: 9px; color: #cce0ec; }
    .rule-strip { display: grid; grid-template-columns: repeat(4, 1fr); gap: 7px; margin-top: 18mm; }
    .rule { border-top: 2px solid #53d3c7; padding-top: 7px; font-size: 9px; color: #d9e9f2; line-height: 1.35; }
    .rule b { color: white; display: block; margin-bottom: 2px; }

    .profile-grid { display: grid; grid-template-columns: repeat(6, 1fr); gap: 8px; margin: 10px 0 12px; }
    .profile { padding: 9px; border-radius: 9px; color: white; min-height: 54px; }
    .profile b { display: block; font-size: 11px; margin-bottom: 3px; }
    .profile span { font-size: 8px; line-height: 1.3; display: block; opacity: .9; }
    .p-admin { background: #157f77; }
    .p-medico { background: #2563be; }
    .p-paciente { background: #7250b5; }
    .p-controller { background: #b7630b; }
    .p-super { background: #a7194b; }
    .p-equipe { background: #3f6212; }

    table { width: 100%; border-collapse: separate; border-spacing: 0; font-size: 8.2px; line-height: 1.22; }
    th { padding: 6px 5px; background: var(--navy); color: white; text-align: center; border-right: 1px solid #29445c; }
    th:first-child { text-align: left; border-radius: 7px 0 0 0; }
    th:last-child { border-radius: 0 7px 0 0; border-right: 0; }
    td { padding: 5px; border-right: 1px solid var(--line); border-bottom: 1px solid var(--line); text-align: center; vertical-align: middle; }
    td:first-child { text-align: left; font-weight: 700; border-left: 1px solid var(--line); }
    tbody tr:nth-child(even) td { background: #f8fafc; }
    .yes { color: #08764e; font-weight: 800; }
    .limited { color: #9a5504; font-weight: 800; }
    .no { color: #9aa3af; font-weight: 700; }
    .platform { color: #a7194b; font-weight: 800; }
    .legend { display: flex; gap: 14px; align-items: center; font-size: 8px; color: var(--muted); margin: 6px 0 8px; }
    .legend span b { margin-right: 3px; }

    .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 11px; }
    .card { border: 1px solid var(--line); border-radius: 9px; padding: 10px; background: white; }
    .card.soft { background: var(--soft); }
    .card p, .card li { font-size: 8.5px; line-height: 1.4; color: var(--muted); }
    .card ul { margin: 4px 0 0; padding-left: 15px; }

    .module-flow { display: grid; grid-template-columns: 1fr 28px 1fr 28px 1fr; gap: 5px; align-items: center; margin: 12px 0; }
    .flow-box { min-height: 68px; border-radius: 10px; padding: 10px; border: 1px solid var(--line); }
    .flow-box strong { display: block; font-size: 12px; color: var(--navy); margin-bottom: 4px; }
    .flow-box span { color: var(--muted); font-size: 8.5px; line-height: 1.35; }
    .arrow { font-size: 22px; text-align: center; color: var(--teal); font-weight: 900; }
    .modules { display: grid; grid-template-columns: repeat(5, 1fr); gap: 8px; margin: 10px 0; }
    .module { border-radius: 9px; padding: 9px; border: 1px solid var(--line); border-top: 4px solid var(--blue); }
    .module b { display: block; font-size: 10px; margin-bottom: 3px; }
    .module span { font-size: 7.8px; color: var(--muted); line-height: 1.35; }
    .blocked { border-top-color: var(--red); background: #fff7f7; }

    .attention-table td { font-size: 8.5px; padding: 7px; }
    .attention-table td:first-child { color: var(--navy); }
    .badge { display: inline-block; padding: 2px 6px; border-radius: 999px; font-size: 7px; font-weight: 800; }
    .badge.warn { background: #fff0cf; color: #8c5200; }
    .badge.ok { background: #dff6eb; color: #08764e; }
    .badge.risk { background: #ffe1e1; color: #ad2020; }
    .callout { margin-top: 9px; border-left: 4px solid var(--orange); padding: 8px 10px; background: #fff8ea; border-radius: 0 8px 8px 0; color: #68430f; font-size: 8.5px; line-height: 1.4; }

    .compact td { padding: 2.6px 4px; }
    .compact th { padding: 4px 4px; }
    .compact-page .profile { min-height: 40px; padding: 6px; }
    .compact-page .profile-grid { margin: 6px 0 8px; }
    .compact-page .lead { margin-bottom: 6px; }

    @media print {
      body { background: white; }
      .page { margin: 0; box-shadow: none; }
    }
  """

PROFILES = [
    ("Administrador", "p-admin", "Administração operacional de uma clínica."),
    ("Médicos", "p-medico", "Atendimento e dados clínicos no escopo médico."),
    ("Pacientes", "p-paciente", "Acesso pessoal limitado."),
    ("Controller", "p-controller", "Operação clínica e financeira, sem administração geral."),
    ("SuperAdministrador", "p-super", "Administração da plataforma e troca de clínica."),
    ("Equipe", "p-equipe", "Identidade coletiva com operador identificado e auditado."),
]

# Cada celula: texto. Prefixos definem a cor no HTML: "-" negado, "~" limitado/escopo, "!" plataforma.
NO = "-—"
API_MATRIX = [
    ("Dashboard", ["R", "R", "R", "R", "R", "R"]),
    ("Clínicas (`/api/platform/clinicas`)", ["~R/U da própria + equipes", NO, NO, NO, "!CRUD + troca", NO]),
    ("Auditoria da plataforma", [NO, NO, NO, NO, "!R", NO]),
    ("Sessões ativas da plataforma", [NO, NO, NO, NO, "!R", NO]),
    ("Usuários", ["CRUD", "~R/U próprio", "~R/U próprio limitado", "~R próprio", "CRUD", "~R membros"]),
    ("Resetar senha de terceiro", ["Sim", NO, NO, NO, "Sim", NO]),
    ("Equipes (`/api/equipes`)", ["CRUD", NO, NO, NO, "CRUD", "~Trocar PIN próprio"]),
    ("Pacientes e cirurgias", ["CRUD", "~CRUD · escopo", "~R próprio", "CRU", "CRUD", "~CRU · escopo"]),
    ("Pendências de cadastro de pacientes (ano da consolidação)", ["R clínica", "~R · escopo", NO, "R clínica", "R clínica", NO]),
    ("Arquivos de pacientes", ["CRD", "~CRD · escopo", "~R próprios", "CRD", "CRD", "~CRD · escopo"]),
    ("Observações de pacientes", ["CRU", "~CRU · escopo", NO, "CRU", "CRU", "~CRU · escopo"]),
    ("Faturamento médico", ["R clínica", "~R · escopo", NO, "R clínica", "R clínica", "~R · escopo"]),
    ("Histórico de arquivos do faturamento", ["CRD", "~R · escopo", NO, "CRD", "CRD", "~R · escopo"]),
    ("Atendimentos cirúrgicos", ["CRUD", "~CRUD · escopo", NO, "CRUD", "CRUD", NO]),
    ("Faturamentos, glosas e recursos", ["CRUD", "~R · escopo", NO, "CRUD", "CRUD", NO]),
    ("Contas a receber e recebimentos", ["CRUD", NO, NO, "CRUD", "CRUD", NO]),
    ("Relatórios e auditoria financeira", ["R", NO, NO, "R", "R", NO]),
    ("Preços por convênio", ["CRUD", "R", NO, "CRUD", "CRUD", NO]),
    ("Grupos médicos", ["CRUD", NO, NO, "CRUD", "CRUD", NO]),
    ("Agenda e notificações", ["CRUD clínica", "~CRUD próprio/escopo", "~CRUD próprio", "~CRUD próprio", "CRUD clínica", "~CRUD · escopo"]),
    ("CBHPM", ["R + importar", "R", "R", "R", "R + importar", "R"]),
    ("Hospitais, convênios e OPME", ["R", "R", "R", "R", "R", "R"]),
    ("Licença própria", ["R", "R", "R", "R", "R", "R"]),
    ("Licenças de médicos", ["R/U/liberar", NO, NO, NO, "R/U/liberar", NO]),
    ("Exportações (`POST /api/exports`)", ["Solicitar", "~Solicitar · escopo", "~Solicitar · próprio", "Solicitar", "Solicitar", "~Solicitar · escopo"]),
    ("Monitoramento (erros e segurança)", ["~R/limpar da clínica", NO, NO, NO, "R/limpar todas", NO]),
    ("Aceites legais e privacidade", ["~Próprios", "~Próprios", "~Próprios", "~Próprios", "~Próprios", "~Próprios"]),
    ("Identidade visual pública", ["R", "R", "R", "R", "R", "R"]),
]

MENU = [
    ("Painel", ["Sim", "Sim", "Licença", "Licença", "Sim", "Licença"]),
    ("Pacientes", ["Sim", "Sim", "~Leitura", "Licença", "Sim", "Licença"]),
    ("Faturamento médico, histórico e relatórios", ["Sim", "Sim", NO, "Sim", "Sim", "Sim"]),
    ("Grupos médicos", ["Sim", NO, NO, "Sim", "Sim", NO]),
    ("Agenda e notificações", ["Sim", "Sim", "Sim", "Sim", "Sim", "Sim"]),
    ("Usuários", ["Sim", NO, NO, NO, "Sim", "Sim"]),
    ("Meu cadastro", [NO, "Sim", "Sim", NO, NO, NO]),
    ("Clínicas", ["~Própria", NO, NO, NO, "!Sim", NO]),
    ("Configuração", ["Sim", NO, NO, NO, "Sim", NO]),
    ("Tutoriais interativos", ["Sim", "Sim", "Sim", "Sim", "Sim", "Sim"]),
]

DIVERGENCES = [
    ("Médico exclui paciente", "Botão de exclusão só para Administrador/SuperAdministrador", "Permite excluir paciente do escopo (`PacienteExcluir`)", "Interface mais restritiva que a API."),
    ("Administrador em módulo não contratado", "Oculta o módulo (só o SuperAdministrador ignora o plano)", "Administrador e SuperAdministrador não são bloqueados pelo plano", "Interface mais restritiva que a API."),
    ("Equipe sem operador identificado", "Modo somente leitura", "Operações sensíveis exigem operador (`EquipeOperacaoSensivel`)", "Alinhado."),
    ("Paciente em Meu cadastro", "Edita nome, telefone, nascimento e foto", "`PUT` próprio preserva e-mail, CPF, perfil, status e dados médicos", "Alinhado."),
]

RULES = [
    "O usuário somente acessa dados da clínica selecionada na sessão (`ClinicaId` do JWT); consultas sem clínica resolvida falham fechadas.",
    "No plano `Parcial`, rotas de usuários, pacientes (inclui CBHPM, hospitais, convênios e OPME), faturamento médico, grupos médicos e agenda exigem o módulo contratado e retornam `403` quando ausente. Administrador e SuperAdministrador não são bloqueados por essa regra na API.",
    "Assinatura suspensa, cancelada ou vencida e licenças individuais de médicos podem bloquear funcionalidades licenciadas (`Dashboard.Visualizar`, `Pacientes.Visualizar`, `Pacientes.Gerenciar`, `Cbhpm.Consultar`).",
    "Médicos enxergam pacientes, faturamentos, atendimentos, arquivos, observações e eventos conforme seu vínculo e seus grupos médicos.",
    "O Administrador permanece restrito à própria clínica, inclusive nas rotas de plataforma; somente o SuperAdministrador cria, desativa e alterna entre clínicas.",
    "O perfil `Equipe` usa identidade coletiva; operações sensíveis exigem equipe ativa e operador identificado no token e são auditadas.",
]


def cls(cell: str) -> tuple[str, str]:
    if cell.startswith("-"):
        return cell[1:], "no"
    if cell.startswith("~"):
        return cell[1:], "limited"
    if cell.startswith("!"):
        return cell[1:], "platform"
    return cell, "yes"


def md_table(rows, first="Recurso"):
    names = [p[0] for p in PROFILES]
    out = [f"| {first} | " + " | ".join(names) + " |", "|---|" + "---|" * len(names)]
    for label, cells in rows:
        out.append(f"| {label} | " + " | ".join(cls(c)[0] for c in cells) + " |")
    return "\n".join(out)


def build_md() -> str:
    rules = "\n".join(f"{i}. {r}" for i, r in enumerate(RULES, 1))
    profiles = "\n".join(f"| {i} | {name} | {desc} |" for i, (name, _, desc) in enumerate(PROFILES, 1))
    div = "\n".join(f"| {a} | {b} | {c} | {d} |" for a, b, c, d in DIVERGENCES)
    return f"""# Matriz de perfis e permissões

Atualizado em {UPDATED}. Gerado por `scripts/generate_permissions_matrix.py` (não edite à mão).

Esta documentação descreve o comportamento efetivamente aplicado na API e a visibilidade atual da interface. As permissões sempre acumulam estas restrições:

{rules}

O inventário completo de rotas com o acesso por perfil está no [README](../README.md#endpoints-principais).

## Perfis

| ID | Perfil | Finalidade |
|---:|---|---|
{profiles}

## Legenda

| Código | Permissão |
|---|---|
| C | Cadastrar |
| R | Visualizar/listar |
| U | Alterar |
| D | Excluir ou desativar |
| Próprio | Somente o registro pertencente ao usuário autenticado |
| Escopo | Somente registros permitidos pelo vínculo médico/grupo ou pela equipe |
| — | Acesso negado |

## Matriz efetiva da API

Esta é a fonte de verdade para segurança. Ocultar um botão na interface não concede nem remove autorização na API.

{md_table(API_MATRIX)}

### Perfil Equipe

O perfil `Equipe` possui um fluxo de autenticação próprio e não deve ser criado diretamente pelo CRUD de usuários:

- o Administrador ou SuperAdministrador cria a identidade coletiva em `/api/equipes` (ou pela clínica em `/api/platform/clinicas/{{id}}/equipes`);
- cada membro ativo é associado a uma equipe e identificado por seleção ou PIN em `/api/equipe-auth/identificar`;
- o JWT contém a equipe, o operador e as versões de sessão usadas para revogação (`/api/session/renovar-equipe`);
- usuários, pacientes, arquivos, observações, agenda, faturamento médico e exportações respeitam o escopo da equipe;
- administração de clínicas, licenças, grupos médicos e o módulo financeiro permanecem bloqueados;
- operações sensíveis exigem uma equipe válida no token e são registradas na auditoria.

### Exclusão de dados

- A exclusão de clínica é lógica: a clínica é desativada e seus dados são preservados (somente SuperAdministrador).
- Usuários somente podem ser excluídos por Administrador ou SuperAdministrador.
- Pacientes podem ser excluídos por Administrador, SuperAdministrador e Médico (no seu escopo). Controller e Equipe não excluem pacientes.
- Eventos podem ser excluídos pelo proprietário; Administrador e SuperAdministrador gerenciam todos os eventos da clínica.

## Menu atual da interface

`Licença` significa que o item depende da feature de licença correspondente; todos os itens operacionais também dependem do módulo contratado no plano `Parcial` (o SuperAdministrador ignora essa restrição na interface).

{md_table(MENU, first="Item")}

## Divergências conhecidas entre interface e API

| Situação | Interface | API | Estado |
|---|---|---|---|
{div}

## Exportações

Todos os perfis autenticados podem solicitar `POST /api/exports` para `pacientes`, `faturamentos-medicos` e `cbhpm`, mas a solicitação não amplia o acesso:

| Recurso exportado | Escopo obrigatório |
|---|---|
| Pacientes | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico e Equipe: escopo do vínculo/grupo ou equipe; Paciente: somente o próprio prontuário. |
| Faturamentos médicos | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico e Equipe: seu escopo; Paciente: nenhum dado. |
| CBHPM | Somente dados que o perfil já pode consultar. Filtros `tipoCirurgia` e `statusCirurgia` são recusados. |

A API monta o conteúdo no servidor (`FileExportSnapshotBuilder`) reutilizando as queries e o escopo do perfil, e envia o snapshot ao worker `GenerateFileExport`. As telas do front geram suas planilhas e PDFs no navegador, a partir dos dados que a API já filtrou.

## Cobertura automatizada

- Backend: `HemodinksAPI.Tests/ApiAuthorizationMatrixTests.cs` — 34 operações representativas × 6 perfis (204 verificações), com equipe, membro, operador e JWT reais. Permissão negada precisa retornar `403`; concedida precisa alcançar o endpoint sem `401`, `403` ou `500`. Os testes funcionais de cada módulo validam payload, persistência, isolamento e regras de negócio.
- Frontend: `src/app/appAccess.test.ts` no `hemodinks-front` — permissões calculadas, menu, ações de pacientes, plataforma e plano `Parcial`.

```powershell
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName~ApiAuthorizationMatrixTests"
```

```powershell
npx vitest run src/app/appAccess.test.ts
```
"""


def html_table(rows, first="Recurso", font=None):
    head = "".join(f"<th>{html.escape(p[0])}</th>" for p in PROFILES)
    body = []
    for label, cells in rows:
        tds = "".join(f'<td class="{c}">{html.escape(t)}</td>' for t, c in (cls(x) for x in cells))
        body.append(f"<tr><td>{html.escape(label.replace('`', ''))}</td>{tds}</tr>")
    style = f' style="font-size:{font}"' if font else ""
    klass = ' class="compact"' if len(rows) > 15 else ""
    return f'<table{klass}{style}><thead><tr><th>{first}</th>{head}</tr></thead><tbody>{"".join(body)}</tbody></table>'


def build_html() -> str:
    profiles = "".join(f'<div class="profile {c}"><b>{n}</b><span>{d}</span></div>' for n, c, d in PROFILES)
    rules = "".join(f'<li>{html.escape(r.replace("`", ""))}</li>' for r in RULES)
    div = "".join(
        f"<tr><td>{html.escape(a)}</td><td>{html.escape(b)}</td><td>{html.escape(c.replace('`', ''))}</td>"
        f'<td><span class="badge {"ok" if d == "Alinhado." else "warn"}">{html.escape(d)}</span></td></tr>'
        for a, b, c, d in DIVERGENCES)
    legend = ('<div class="legend"><span><b class="yes">●</b> permitido</span><span><b class="limited">●</b> '
              'permitido com limite/escopo</span><span><b class="no">●</b> negado</span><span><b class="platform">'
              '●</b> função exclusiva de plataforma</span></div>')
    return f"""<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Hemodinks — Matriz de perfis e permissões</title>
  <style>{CSS}</style>
</head>
<body>
<section class="page cover">
  <div class="brand">HEMODINKS · GESTÃO DE CIRURGIAS</div>
  <div class="cover-grid">
    <div>
      <div class="eyebrow" style="color:#7ee2d7">SaaS multiclínica</div>
      <h1>Matriz de perfis<br>e permissões</h1>
      <p class="subtitle">Acessos por perfil, operações CRUD, módulos contratados e regras de isolamento aplicadas pela API, com a visibilidade atual da interface.</p>
    </div>
    <div class="stat-grid">
      <div class="stat"><strong>6</strong><span>perfis existentes</span></div>
      <div class="stat"><strong>34</strong><span>operações representativas testadas</span></div>
      <div class="stat"><strong>204</strong><span>verificações de autorização</span></div>
      <div class="stat"><strong>146</strong><span>rotas inventariadas</span></div>
    </div>
  </div>
  <div class="rule-strip">
    <div class="rule"><b>Tenant-scoped</b>Dados limitados à clínica selecionada.</div>
    <div class="rule"><b>Plano Parcial</b>Módulos não contratados retornam 403 (exceto administradores).</div>
    <div class="rule"><b>Escopo médico e de equipe</b>Registros seguem vínculos, grupos e equipe.</div>
    <div class="rule"><b>SuperAdministrador</b>Cria, desativa e alterna clínicas.</div>
  </div>
  <div class="footer" style="color:#bdd2df"><span>Documento técnico e funcional</span><span>{UPDATED}</span></div>
</section>
<section class="page compact-page">
  <div class="eyebrow">Perfis da aplicação</div>
  <h2>Quem pode fazer o quê na API?</h2>
  <p class="lead">C = cadastrar · R = visualizar · U = alterar · D = excluir/desativar. “Escopo” significa que o usuário só alcança registros relacionados a ele, ao seu grupo médico ou à sua equipe.</p>
  <div class="profile-grid">{profiles}</div>
  {html_table(API_MATRIX, font="7.4px")}
  {legend}
  <div class="footer"><span>Hemodinks · Matriz efetiva da API</span><span>2</span></div>
</section>
<section class="page">
  <div class="eyebrow">Experiência do usuário</div>
  <h2>Itens exibidos no menu</h2>
  <p class="lead">“Licença” depende da feature correspondente. Todos os itens operacionais dependem do módulo contratado no plano Parcial; na interface só o SuperAdministrador ignora essa restrição.</p>
  {html_table(MENU, first="Item visual", font="9px")}
  <div class="two-col" style="margin-top:12px">
    <div class="card soft">
      <h3>Regra de exclusão</h3>
      <ul>
        <li>Clínica: desativação lógica, somente SuperAdministrador.</li>
        <li>Usuários: somente Administrador e SuperAdministrador.</li>
        <li>Pacientes: Administrador, SuperAdministrador e Médico no escopo.</li>
        <li>Evento: o proprietário gerencia o próprio; administradores gerenciam a clínica.</li>
      </ul>
    </div>
    <div class="card soft">
      <h3>Regras que sempre se acumulam</h3>
      <ul>{rules}</ul>
    </div>
  </div>
  <div class="footer"><span>Hemodinks · Menu e regras</span><span>3</span></div>
</section>
<section class="page">
  <div class="eyebrow">Assinatura da clínica</div>
  <h2>Como funciona o plano Parcial</h2>
  <p class="lead">A seleção feita no cadastro da clínica controla a interface e a proteção das rotas da API.</p>
  <div class="module-flow">
    <div class="flow-box"><strong>1. SuperAdministrador</strong><span>Seleciona o plano Parcial e marca os módulos contratados no cadastro da clínica.</span></div>
    <div class="arrow">→</div>
    <div class="flow-box"><strong>2. Sessão da clínica</strong><span>O login e a troca de clínica recebem a lista efetiva de módulos liberados.</span></div>
    <div class="arrow">→</div>
    <div class="flow-box"><strong>3. Dupla proteção</strong><span>Menu e cards são ocultados; chamadas diretas a módulos não contratados recebem 403 (ClinicaModuleAccessMiddleware).</span></div>
  </div>
  <div class="modules">
    <div class="module"><b>Usuários</b><span>Rotas /api/users e menu Usuários.</span></div>
    <div class="module"><b>Pacientes</b><span>Pacientes, CBHPM, hospitais, convênios e OPME.</span></div>
    <div class="module"><b>Faturamento</b><span>Rotas /api/faturamentos-medicos.</span></div>
    <div class="module blocked"><b>Grupos médicos</b><span>Quando desmarcado: menu oculto e API retorna 403.</span></div>
    <div class="module blocked"><b>Agenda</b><span>Quando desmarcado: agenda oculta e API retorna 403.</span></div>
  </div>
  <div class="two-col">
    <div class="card"><h3>Quem não é bloqueado</h3><p>Na API, Administrador e SuperAdministrador não são bloqueados pelo plano. Na interface, só o SuperAdministrador vê módulos não contratados.</p></div>
    <div class="card"><h3>O que sempre permanece</h3><p>Painel, autenticação, troca de senha e funções da sessão. Clínicas e Configuração continuam disponíveis ao SuperAdministrador.</p></div>
    <div class="card"><h3>Trial</h3><p>Todos os módulos previstos ficam disponíveis até “Trial até”, respeitando perfil, escopo e validade da assinatura.</p></div>
    <div class="card"><h3>Completa</h3><p>Todos os módulos ficam disponíveis até “Assinatura válida até”, respeitando as permissões do perfil.</p></div>
  </div>
  <div class="footer"><span>Hemodinks · Plano Parcial</span><span>4</span></div>
</section>
<section class="page">
  <div class="eyebrow">Interface × API</div>
  <h2>Divergências conhecidas e exportações</h2>
  <p class="lead">A API é a fonte de verdade para segurança. A interface pode ser mais restritiva, nunca mais permissiva.</p>
  <table class="attention-table"><thead><tr><th>Situação</th><th>Interface</th><th>API</th><th>Estado</th></tr></thead><tbody>{div}</tbody></table>
  <div class="callout"><b>Exportações:</b> todos os perfis podem solicitar POST /api/exports (pacientes, faturamentos-medicos e cbhpm). A API monta o snapshot no servidor com as mesmas queries e o mesmo escopo do perfil; o worker GenerateFileExport apenas gera o arquivo. As telas do front exportam no navegador os dados que a API já filtrou.</div>
  <div class="two-col" style="margin-top:12px">
    <div class="card soft">
      <h3>Validação automatizada</h3>
      <p><span class="badge ok">204 verificações</span> ApiAuthorizationMatrixTests (34 operações × 6 perfis)</p>
      <p><span class="badge ok">appAccess.test.ts</span> menu, ações e plano Parcial no front</p>
    </div>
    <div class="card soft">
      <h3>Arquivos de referência</h3>
      <p><b>Políticas:</b> HemodinksAPI.Api/ApiServiceCollectionExtensions.Auth.cs</p>
      <p><b>Plano:</b> HemodinksAPI.Api/Middleware/ClinicaModuleAccessMiddleware.cs</p>
      <p><b>Front:</b> src/app/appAccess.ts</p>
      <p><b>Documento detalhado:</b> docs/MATRIZ_PERFIS_PERMISSOES.md</p>
    </div>
  </div>
  <div class="footer"><span>Hemodinks · Divergências e evidências</span><span>5</span></div>
</section>
</body>
</html>
"""


def main() -> None:
    (DOCS / "MATRIZ_PERFIS_PERMISSOES.md").write_text(build_md(), encoding="utf-8")
    (DOCS / "MATRIZ_PERFIS_PERMISSOES_VISUAL.html").write_text(build_html(), encoding="utf-8")


if __name__ == "__main__":
    main()
