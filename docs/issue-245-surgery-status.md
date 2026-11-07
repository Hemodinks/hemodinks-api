# Issue #245 — Status da cirurgia (Consolidada / Não consolidada)

## Regra

- `Pacientes.StatusCirurgia` é derivado de `Pacientes.DataAtendimento` (data da consolidação):
  preenchida → `Consolidada`; nula → `NaoConsolidada` (default).
- O status nunca vem do payload. O setter de `Paciente.DataAtendimento` recalcula o status,
  então create, update e qualquer escritor futuro mantêm a coluna consistente.
- Leituras do EF usam o campo de apoio e preservam o valor persistido.

## Schema

Migration `20261107043000_Schema_AddSurgeryConsolidationStatus` (aditiva):

- coluna `nvarchar(20) NOT NULL DEFAULT N'NaoConsolidada'` — escritores antigos continuam inserindo;
- backfill `Consolidada` onde `DataAtendimento IS NOT NULL`;
- `CK_Pacientes_StatusCirurgia` e índice `IX_Pacientes_ClinicaId_StatusCirurgia`.

`Down()` remove só a coluna derivada; `DataAtendimento` fica intacta e um novo `Up()` recalcula.

## Contrato (consumido pelo `hemodinks-front`)

- `PacienteDto.statusCirurgia`: `"Consolidada" | "NaoConsolidada"`.
- Parâmetro opcional `statusCirurgia` (`Consolidada` | `NaoConsolidada`; ausente = todas) em
  `GET /api/pacientes/` e `GET /api/faturamentos-medicos/`. Valor inválido → 400.
- `POST /api/exports/`: filtro `statusCirurgia` aceito para `pacientes` e `faturamentos-medicos`,
  rejeitado para `cbhpm`. As colunas da exportação não mudaram.

## Fora do escopo

Endpoints do financeiro normalizado (`/api/atendimentos-cirurgicos`, `/api/faturamentos`,
`/api/financeiro/*`) não recebem o filtro: o front não os consome nas telas de pacientes,
faturamento e relatórios.
