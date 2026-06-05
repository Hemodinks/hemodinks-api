from __future__ import annotations

import math
from pathlib import Path
from textwrap import wrap

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4, landscape
from reportlab.pdfgen import canvas


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "Hemodinks-Documentacao-Tecnica.pdf"
PAGE_SIZE = landscape(A4)
WIDTH, HEIGHT = PAGE_SIZE
MARGIN = 36

NAVY = colors.HexColor("#0b1f33")
BLUE = colors.HexColor("#1d4ed8")
LIGHT_BLUE = colors.HexColor("#dbeafe")
GREEN = colors.HexColor("#047857")
LIGHT_GREEN = colors.HexColor("#d1fae5")
ORANGE = colors.HexColor("#c2410c")
LIGHT_ORANGE = colors.HexColor("#ffedd5")
GRAY = colors.HexColor("#4b5563")
LIGHT_GRAY = colors.HexColor("#f3f4f6")
RED = colors.HexColor("#b91c1c")
LIGHT_RED = colors.HexColor("#fee2e2")
PURPLE = colors.HexColor("#6d28d9")
LIGHT_PURPLE = colors.HexColor("#ede9fe")


def title(c: canvas.Canvas, text: str, subtitle: str | None = None) -> None:
    c.setFillColor(NAVY)
    c.setFont("Helvetica-Bold", 21)
    c.drawString(MARGIN, HEIGHT - 42, text)
    if subtitle:
        c.setFont("Helvetica", 10)
        c.setFillColor(GRAY)
        c.drawString(MARGIN, HEIGHT - 60, subtitle)
    c.setStrokeColor(colors.HexColor("#d1d5db"))
    c.line(MARGIN, HEIGHT - 72, WIDTH - MARGIN, HEIGHT - 72)


def footer(c: canvas.Canvas, page: int) -> None:
    c.setFont("Helvetica", 8)
    c.setFillColor(GRAY)
    c.drawString(MARGIN, 20, "Hemodinks - Documentacao Tecnica")
    c.drawRightString(WIDTH - MARGIN, 20, f"Pagina {page}")


def draw_wrapped(c: canvas.Canvas, text: str, x: float, y: float, width: int, size: int = 10, leading: int = 14) -> float:
    c.setFont("Helvetica", size)
    c.setFillColor(colors.black)
    chars = max(20, int(width / (size * 0.52)))
    for line in wrap(text, chars):
        c.drawString(x, y, line)
        y -= leading
    return y


def box(
    c: canvas.Canvas,
    x: float,
    y: float,
    w: float,
    h: float,
    label: str,
    fill=LIGHT_GRAY,
    stroke=GRAY,
    text=colors.black,
    font_size: int = 10,
    radius: int = 6,
) -> None:
    c.setFillColor(fill)
    c.setStrokeColor(stroke)
    c.roundRect(x, y, w, h, radius, fill=1, stroke=1)
    c.setFillColor(text)
    c.setFont("Helvetica-Bold", font_size)
    lines = wrap(label, max(10, int(w / (font_size * 0.5))))
    line_height = font_size + 2
    total = len(lines) * line_height
    current_y = y + (h + total) / 2 - line_height
    for line in lines:
        c.drawCentredString(x + w / 2, current_y, line)
        current_y -= line_height


def table_box(
    c: canvas.Canvas,
    x: float,
    y: float,
    w: float,
    title_text: str,
    fields: list[str],
    fill=colors.white,
    stroke=BLUE,
) -> float:
    row_h = 13
    h = 25 + row_h * len(fields)
    c.setFillColor(fill)
    c.setStrokeColor(stroke)
    c.roundRect(x, y, w, h, 5, fill=1, stroke=1)
    c.setFillColor(stroke)
    c.rect(x, y + h - 24, w, 24, fill=1, stroke=0)
    c.setFillColor(colors.white)
    c.setFont("Helvetica-Bold", 9)
    c.drawCentredString(x + w / 2, y + h - 16, title_text)
    c.setFillColor(colors.black)
    c.setFont("Helvetica", 7.5)
    current = y + h - 37
    for field in fields:
        c.drawString(x + 7, current, field)
        current -= row_h
    return h


def arrow(
    c: canvas.Canvas,
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    label: str | None = None,
    color=GRAY,
    dashed: bool = False,
) -> None:
    c.setStrokeColor(color)
    c.setFillColor(color)
    c.setLineWidth(1.3)
    if dashed:
        c.setDash(4, 3)
    else:
        c.setDash()
    c.line(x1, y1, x2, y2)
    angle = math.atan2(y2 - y1, x2 - x1)
    length = 8
    spread = math.pi / 7
    p1 = (x2 - length * math.cos(angle - spread), y2 - length * math.sin(angle - spread))
    p2 = (x2 - length * math.cos(angle + spread), y2 - length * math.sin(angle + spread))
    c.line(x2, y2, p1[0], p1[1])
    c.line(x2, y2, p2[0], p2[1])
    c.setDash()
    if label:
        c.setFont("Helvetica", 7)
        c.setFillColor(color)
        c.drawCentredString((x1 + x2) / 2, (y1 + y2) / 2 + 6, label)


def bullet_list(c: canvas.Canvas, items: list[str], x: float, y: float, width: int, size: int = 9) -> float:
    c.setFont("Helvetica", size)
    c.setFillColor(colors.black)
    for item in items:
        lines = wrap(item, max(20, int(width / (size * 0.52))))
        c.drawString(x, y, "- " + lines[0])
        y -= size + 4
        for line in lines[1:]:
            c.drawString(x + 10, y, line)
            y -= size + 4
    return y


def page_overview(c: canvas.Canvas, page: int) -> int:
    title(c, "Hemodinks - Documentacao Tecnica", "Backend, frontend, dados, Azure e fluxos principais")
    y = HEIGHT - 100
    y = draw_wrapped(
        c,
        "O Hemodinks e uma aplicacao web composta por frontend React/Vite, API ASP.NET Core/.NET 10, banco SQL Server/Azure SQL e Azure Blob Storage para arquivos. A API usa CQRS com MediatR, JWT Bearer, EF Core, Serilog, Swagger, Scalar e IMemoryCache para consultas CBHPM.",
        MARGIN,
        y,
        720,
        10,
        15,
    )
    y -= 8
    c.setFont("Helvetica-Bold", 12)
    c.setFillColor(NAVY)
    c.drawString(MARGIN, y, "URLs")
    y -= 20
    rows = [
        ("Frontend local", "http://localhost:5173"),
        ("Frontend producao", "https://hemodinks-saude.vercel.app"),
        ("API local", "http://localhost:5000"),
        ("Swagger", "/swagger"),
        ("Scalar", "/scalar"),
        ("OpenAPI JSON", "/openapi/v1.json"),
    ]
    c.setFont("Helvetica", 9)
    for left, right in rows:
        c.setFillColor(BLUE)
        c.drawString(MARGIN, y, left)
        c.setFillColor(colors.black)
        c.drawString(MARGIN + 145, y, right)
        y -= 16
    y -= 12
    c.setFont("Helvetica-Bold", 12)
    c.setFillColor(NAVY)
    c.drawString(MARGIN, y, "Recursos Azure")
    y -= 18
    bullet_list(
        c,
        [
            "Azure SQL Database: persistencia relacional via Entity Framework Core.",
            "Azure Blob Storage: containers profile-photos e patient-files.",
            "Azure Queue Storage / Service Bus: nao utilizado na versao atual; reservado para processos assincronos futuros.",
            "IMemoryCache: cache local da API, sem recurso Azure separado.",
        ],
        MARGIN,
        y,
        720,
    )
    footer(c, page)
    c.showPage()
    return page + 1


def page_architecture(c: canvas.Canvas, page: int) -> int:
    title(c, "Arquitetura e Comunicacao", "Fluxo de frontend, API, cache, banco e servicos Azure")
    box(c, 45, 370, 105, 52, "Browser", LIGHT_BLUE, BLUE)
    box(c, 195, 370, 125, 52, "React/Vite Frontend\nVercel", LIGHT_BLUE, BLUE)
    box(c, 365, 370, 135, 52, "ASP.NET Core API\nRender Docker", LIGHT_GREEN, GREEN)
    box(c, 555, 452, 130, 50, "IMemoryCache\nCBHPM", LIGHT_ORANGE, ORANGE)
    box(c, 555, 360, 130, 50, "Azure SQL\nDatabase", LIGHT_PURPLE, PURPLE)
    box(c, 555, 268, 130, 50, "Azure Blob\nStorage", LIGHT_PURPLE, PURPLE)
    box(c, 555, 176, 130, 50, "Azure Queue\nnao usado", LIGHT_RED, RED)
    arrow(c, 150, 396, 195, 396, "HTTPS")
    arrow(c, 320, 396, 365, 396, "REST + JWT")
    arrow(c, 500, 406, 555, 472, "cache local")
    arrow(c, 500, 390, 555, 385, "EF Core SQL")
    arrow(c, 500, 374, 555, 293, "Blob SDK")
    arrow(c, 500, 358, 555, 201, "futuro", RED, dashed=True)
    y = 130
    bullet_list(
        c,
        [
            "Swagger e Scalar sao servidos pela propria API e consomem o documento OpenAPI gerado por Swashbuckle.",
            "A consulta CBHPM aquece o cache na primeira chamada; filtros e paginacao seguintes rodam em memoria.",
            "Uploads usam Azure Blob Storage; o banco guarda a URL e os metadados.",
        ],
        MARGIN,
        y,
        760,
    )
    footer(c, page)
    c.showPage()
    return page + 1


def page_mer(c: canvas.Canvas, page: int) -> int:
    title(c, "MER - Modelo Entidade Relacional", "Tabelas principais, chaves e relacionamentos")
    users_fields = [
        "Id PK",
        "PerfilId FK",
        "Nome, Email UK, Telefone",
        "Cpf UK nullable",
        "FotoPerfil",
        "Senha hash",
        "DataCadastro, DataAtualizacao",
        "DataNascimento",
        "Ativo, PrecisaTrocarSenha",
    ]
    pacientes_fields = [
        "Id PK",
        "UserId FK UK",
        "Data, NomePaciente",
        "Hospital, Medico, Convenio",
        "CbhpmCodigo",
        "CbhpmPorte",
        "Procedimento",
        "Autorizacao, Pagamento",
        "RepasseGlosa, StatusPago",
    ]
    cbhpm_fields = [
        "Id PK",
        "Codigo UK",
        "Procedimento",
        "Porte",
        "CustoOperacional",
        "Capitulo, Grupo",
        "PaginaPdf",
    ]
    perfil_fields = ["Id PK", "Nome UK"]
    arquivos_fields = ["Id PK", "OwnerId FK", "NomeOriginal", "ContentType", "Url", "DataUpload"]

    table_box(c, 45, 365, 135, "Perfis", perfil_fields, stroke=PURPLE)
    table_box(c, 230, 305, 180, "Users", users_fields, stroke=BLUE)
    table_box(c, 470, 305, 180, "Pacientes", pacientes_fields, stroke=GREEN)
    table_box(c, 690, 320, 115, "CBHPMGeral", cbhpm_fields, stroke=ORANGE)
    table_box(c, 230, 120, 180, "UserArquivos", arquivos_fields, stroke=GRAY)
    table_box(c, 470, 120, 180, "PacienteArquivos", arquivos_fields, stroke=GRAY)

    arrow(c, 180, 392, 230, 392, "1:N")
    arrow(c, 410, 392, 470, 392, "1:1")
    arrow(c, 650, 392, 690, 392, "codigo logico")
    arrow(c, 320, 305, 320, 236, "1:N")
    arrow(c, 560, 305, 560, 236, "1:N")
    footer(c, page)
    c.showPage()
    return page + 1


def page_backend_flow(c: canvas.Canvas, page: int) -> int:
    title(c, "Fluxo de Classes do Backend", "Minimal APIs, MediatR, regras, cache, storage e EF Core")
    box(c, 55, 410, 125, 50, "Program.cs\nDI + middleware", LIGHT_BLUE, BLUE)
    box(c, 230, 410, 145, 50, "Endpoint Extensions\nUsers/Pacientes/CBHPM", LIGHT_BLUE, BLUE)
    box(c, 425, 410, 120, 50, "MediatR", LIGHT_GREEN, GREEN)
    box(c, 595, 458, 150, 45, "Command Handlers", LIGHT_GREEN, GREEN)
    box(c, 595, 382, 150, 45, "Query Handlers", LIGHT_GREEN, GREEN)
    box(c, 425, 295, 150, 48, "Regras de dominio\nPacienteRules", LIGHT_ORANGE, ORANGE)
    box(c, 225, 230, 145, 48, "ICbhpmCache\nCbhpmCache", LIGHT_ORANGE, ORANGE)
    box(c, 425, 215, 150, 48, "AppDbContext\nEF Core", LIGHT_PURPLE, PURPLE)
    box(c, 625, 230, 150, 48, "Storage Services\nAzure Blob", LIGHT_PURPLE, PURPLE)
    box(c, 425, 120, 150, 45, "SQL Server /\nAzure SQL", LIGHT_PURPLE, PURPLE)
    box(c, 625, 120, 150, 45, "Blob Storage", LIGHT_PURPLE, PURPLE)

    arrow(c, 180, 435, 230, 435)
    arrow(c, 375, 435, 425, 435)
    arrow(c, 545, 438, 595, 480)
    arrow(c, 545, 424, 595, 405)
    arrow(c, 670, 458, 575, 340)
    arrow(c, 670, 382, 575, 320)
    arrow(c, 425, 315, 370, 255)
    arrow(c, 500, 295, 500, 263)
    arrow(c, 575, 315, 625, 255)
    arrow(c, 500, 215, 500, 165)
    arrow(c, 700, 230, 700, 165)

    footer(c, page)
    c.showPage()
    return page + 1


def page_business_flow(c: canvas.Canvas, page: int) -> int:
    title(c, "Fluxo de Regra de Negocio", "Cadastro e edicao de paciente com selecao CBHPM")
    steps = [
        ("Usuario autenticado", 45, 410),
        ("Formulario paciente", 185, 410),
        ("Popup CBHPM", 325, 410),
        ("GET /api/cbhpm", 465, 410),
        ("Cache CBHPM", 605, 410),
        ("Seleciona item", 605, 310),
        ("Payload paciente", 465, 310),
        ("POST/PUT /api/pacientes", 325, 310),
        ("Valida perfil e CBHPM", 185, 310),
        ("Salva User + Paciente", 45, 310),
        ("Retorna PacienteDto", 325, 205),
    ]
    for label, x, y in steps:
        fill = LIGHT_GREEN if "Cache" in label or "Valida" in label else LIGHT_BLUE
        stroke = GREEN if fill == LIGHT_GREEN else BLUE
        box(c, x, y, 120, 48, label, fill, stroke, font_size=9)

    for i in range(4):
        arrow(c, steps[i][1] + 120, steps[i][2] + 24, steps[i + 1][1], steps[i + 1][2] + 24)
    arrow(c, 665, 410, 665, 358)
    arrow(c, 605, 334, 585, 334)
    arrow(c, 465, 334, 445, 334)
    arrow(c, 325, 334, 305, 334)
    arrow(c, 185, 334, 165, 334)
    arrow(c, 105, 310, 360, 253, "persistencia")

    c.setFont("Helvetica-Bold", 11)
    c.setFillColor(NAVY)
    c.drawString(45, 155, "Regras principais")
    bullet_list(
        c,
        [
            "Administrador pode cadastrar e editar pacientes; medico tem acesso conforme vinculo; paciente acessa o proprio cadastro.",
            "Se CbhpmCodigo existir, a API busca o procedimento no cache e usa os dados oficiais de codigo, porte e procedimento.",
            "Anexos sao enviados separadamente por endpoint multipart e armazenados no Azure Blob Storage.",
        ],
        45,
        135,
        760,
    )
    footer(c, page)
    c.showPage()
    return page + 1


def page_cbhpm_flow(c: canvas.Canvas, page: int) -> int:
    title(c, "Fluxo CBHPM e Cache", "Seed, leitura, filtro, paginacao e invalidacao")
    box(c, 55, 430, 120, 44, "Startup API", LIGHT_BLUE, BLUE)
    box(c, 215, 430, 120, 44, "Migrations", LIGHT_BLUE, BLUE)
    box(c, 375, 430, 120, 44, "CbhpmSeeder", LIGHT_BLUE, BLUE)
    box(c, 535, 430, 130, 44, "CBHPMGeral SQL", LIGHT_PURPLE, PURPLE)
    box(c, 55, 310, 130, 44, "GET /api/cbhpm", LIGHT_GREEN, GREEN)
    box(c, 225, 310, 130, 44, "ICbhpmCache", LIGHT_ORANGE, ORANGE)
    box(c, 395, 310, 130, 44, "Snapshot memoria", LIGHT_ORANGE, ORANGE)
    box(c, 565, 310, 130, 44, "Filtro + paginacao", LIGHT_GREEN, GREEN)
    box(c, 565, 215, 130, 44, "PagedResult", LIGHT_GREEN, GREEN)
    box(c, 55, 185, 130, 44, "POST /api/cbhpm/import", LIGHT_RED, RED)
    box(c, 225, 185, 130, 44, "SaveChanges", LIGHT_RED, RED)
    box(c, 395, 185, 130, 44, "Invalidate cache", LIGHT_RED, RED)

    arrow(c, 175, 452, 215, 452)
    arrow(c, 335, 452, 375, 452)
    arrow(c, 495, 452, 535, 452)
    arrow(c, 185, 332, 225, 332)
    arrow(c, 355, 332, 395, 332)
    arrow(c, 525, 332, 565, 332)
    arrow(c, 630, 310, 630, 259)
    arrow(c, 460, 310, 590, 430, "miss cache")
    arrow(c, 185, 207, 225, 207)
    arrow(c, 355, 207, 395, 207)
    arrow(c, 525, 207, 565, 430, "recarrega proxima leitura", RED, dashed=True)

    c.setFont("Helvetica-Bold", 11)
    c.setFillColor(NAVY)
    c.drawString(55, 130, "Politica")
    bullet_list(
        c,
        [
            "Expiracao absoluta de 12 horas e deslizante de 2 horas.",
            "Cache local por processo; em multiplas instancias, cada instancia aquece seu proprio cache.",
            "Importacao e seed invalidam a chave cbhpm-geral:v1.",
        ],
        55,
        112,
        760,
    )
    footer(c, page)
    c.showPage()
    return page + 1


def page_endpoints(c: canvas.Canvas, page: int) -> int:
    title(c, "Documentacao Viva da API", "Swagger, Scalar e endpoints principais")
    rows = [
        ("GET", "/healthz", "Health check publico"),
        ("POST", "/api/users/authenticate", "Login JWT"),
        ("GET", "/api/users", "Usuarios paginados"),
        ("GET", "/api/pacientes", "Pacientes paginados"),
        ("POST", "/api/pacientes", "Cadastro de paciente"),
        ("PUT", "/api/pacientes/{id}", "Edicao de paciente"),
        ("GET", "/api/cbhpm", "Consulta CBHPM paginada com cache"),
        ("POST", "/api/cbhpm/import", "Importacao CBHPM admin"),
        ("GET", "/api/dashboard/summary", "Resumo do dashboard"),
        ("GET", "/api/dashboard/notifications", "Notificacoes"),
    ]
    x = 55
    y = 455
    c.setFillColor(NAVY)
    c.setFont("Helvetica-Bold", 10)
    c.drawString(x, y, "Metodo")
    c.drawString(x + 80, y, "Rota")
    c.drawString(x + 350, y, "Uso")
    y -= 10
    c.setStrokeColor(colors.HexColor("#d1d5db"))
    c.line(x, y, WIDTH - 55, y)
    y -= 18
    c.setFont("Helvetica", 9)
    for method, route, desc in rows:
        c.setFillColor(BLUE)
        c.drawString(x, y, method)
        c.setFillColor(colors.black)
        c.drawString(x + 80, y, route)
        c.drawString(x + 350, y, desc)
        y -= 20
    y -= 16
    c.setFont("Helvetica-Bold", 11)
    c.setFillColor(NAVY)
    c.drawString(x, y, "Interfaces de documentacao")
    y -= 18
    bullet_list(
        c,
        [
            "Swagger UI: /swagger",
            "Scalar UI: /scalar",
            "OpenAPI JSON usado pelo Scalar: /openapi/v1.json",
            "Swagger JSON: /swagger/v1/swagger.json",
        ],
        x,
        y,
        760,
    )
    footer(c, page)
    c.showPage()
    return page + 1


def generate() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUTPUT), pagesize=PAGE_SIZE)
    page = 1
    page = page_overview(c, page)
    page = page_architecture(c, page)
    page = page_mer(c, page)
    page = page_backend_flow(c, page)
    page = page_business_flow(c, page)
    page = page_cbhpm_flow(c, page)
    page = page_endpoints(c, page)
    c.save()
    print(OUTPUT)


if __name__ == "__main__":
    generate()
