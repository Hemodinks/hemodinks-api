# Release de dezembro/2026: runbook e ensaio (API #398, front #328)

Promocao `developer` -> `main` da API (85 commits no PR #414, 46 sem merges) e do front
(42 commits no PR #335, 22 sem merges). Com os PRs que entraram na `main` depois (#422, #423,
#425, #428), a API tem 100 commits (51 sem merges) a frente da producao (`36361d9`). O front usa o
contrato novo da API (paciente "Inativar", MFA, trilha de auditoria), entao a **API sobe
primeiro**. Ensaio feito em 25/11/2026, sem alterar producao.

## Evidencias do ensaio (25/11/2026)

### Migrations

As 9 migrations novas (a producao termina em `20261116221408_Schema_EmailChangeConfirmationAttempts`):

| Migration | O que faz no `Up` |
| --- | --- |
| `20261123163221_Schema_AuditoriaClinica` | tabela nova |
| `20261123174813_Schema_PacienteInativacao` | `Pacientes.Ativo` (NOT NULL, default `true`), 2 colunas anulaveis, indice nao unico |
| `20261123190050_Schema_PacienteLoginBloqueado` | coluna NOT NULL com default `false` |
| `20261123190740_Schema_AtendimentoCancelamento` | 3 colunas anulaveis |
| `20261123195221_Schema_ArquivoExclusaoLogica` | 4 colunas anulaveis |
| `20261124131933_Schema_RefreshTokenReuseDetection` | 2 colunas anulaveis, indice nao unico |
| `20261124140539_Schema_Mfa` | `MfaHabilitado` (NOT NULL, default `false`), colunas anulaveis, 2 tabelas novas |
| `20261125150814_Schema_AuditoriaDetalhe` | `AuditoriasClinica.PacienteId`/`Detalhe` anulaveis, `ConfiguracoesSistema.AuditarAtendimentos` (NOT NULL, default `false`), indice nao unico; preenche `PacienteId` das linhas de paciente ja existentes (`TRY_CONVERT`, so na propria tabela) |
| `20261126174803_Schema_RefreshTokenFamily` | tabela nova `AuthenticationSessionRefreshHistory` (familia de refresh tokens, #389), FK com cascade para `AuthenticationSessions`, 3 indices nao unicos |

- Todas sao aditivas: `Drop*` so nos `Down`; colunas obrigatorias tem default; indices
  unicos so em tabelas novas (vazias). Nenhuma pode falhar por causa de dados existentes.
- A revisao antiga continua funcionando com o schema novo (blue/green): ela nao conhece
  as colunas novas e os defaults cobrem os INSERTs dela.
- **Homologacao como ensaio real no Azure SQL:** estava na mesma migration da producao
  (`20261116221408`) e recebeu as 7 pelo mesmo bundle EF do pipeline:
  - run `45304397391` (24/11 01:24 UTC): 5 migrations em 10 s, schema verificado em 4 s;
  - run `45457826026` (24/11 18:20 UTC): 2 migrations em 7 s, schema verificado em 3 s.
  A diferenca para producao e o volume de dados e o lote unico; como tudo e aditivo,
  isso nao muda o resultado. Estimativa para producao: menos de 1 minuto.
- Ensaio local nao foi possivel: o LocalDB nao tem Full-Text Search, exigido pela
  baseline. A imagem `sqlserver/Dockerfile.fts` (a mesma do CI) serve para isso se o
  Docker Desktop estiver ligado.
  - **Atualizacao 26/11/2026:** o ensaio local foi feito no Docker com essa imagem, a partir
    do estado da `main` e pelo caminho de producao (SchemaGuard, efbundle, restore e
    `ValidateRestoredDatabase.sql`); ver o comentario de 26/11 na #398 e
    `docs/release-rehearsal-local.md`. Cobriu as 8 primeiras migrations.
- A `20261125150814_Schema_AuditoriaDetalhe` entrou depois do ensaio de 25/11: confirmar a aplicacao
  na homologacao (run do `publish-homologation`) antes do release.
- **A `20261126174803_Schema_RefreshTokenFamily` (#389, PR #420) entrou em 26/11, depois dos
  ensaios**, e ja esta na `main` (PR #423). Validada no SQL Server do CI e local: replay completo
  das migrations, `Test-Migrations.ps1` sem drift e testes SQL Server da familia (obrigatorios
  no CI). Nao passou pela homologacao, que esta pausada por custo.
- Marcador de schema `20261126174803_Schema_RefreshTokenFamily` conferido em
  `scripts/ValidateRestoredDatabase.sql`, `HemodinksAPI.Tests/RestoreDatabaseIntegrityTests.cs`
  e `docs/issue-184-backup-recovery.md` (antes, `20261125150814_Schema_AuditoriaDetalhe`).

### Producao hoje (somente leitura)

- Container App `hemodinks-api-prod`: `blue` = `--sha-36361d9a1e` (100%), `green` =
  `--sha-c0ff959dc7` (0%). As duas usam `connectionstrings-runtime-prod-v2`, entao
  passam na trava do #380.
- `HemodinksDB`: PITR de 7 dias (ponto mais antigo em 18/11/2026 13:58 UTC), diferencial a
  cada 12 h, redundancia local.
- New Relic: o passo "Record deployment in New Relic" so existe na `developer` e vai
  rodar pela primeira vez neste release. Secret `NEW_RELIC_API_KEY` presente (API: repo;
  front: environment `azure-production`) e variaveis de conta/regiao configuradas.
- Front: build da `developer` com 64 MB em 180 arquivos (60 MB sao videos de tutorial),
  abaixo do limite de 250 MB do Static Web Apps Free.

## Ordem do release

1. **Homologacao verde**: `publish-homologation` (modo `publish`) na `developer`, e smoke
   manual na homologacao (lista abaixo).
2. **API: PR `developer` -> `main`**, com CI verde e merge. O push na `main` dispara o
   `Publish Production` em modo build: imagens por digest, bundle de migrations e
   SchemaGuard. Esperar esse run terminar verde.
3. **API: `Publish Production` manual** (`workflow_dispatch` na `main`, `deploy_to_azure`
   marcado). Ele reaproveita o run do push e, na ordem:
   - `migrate-production` (aprovacao do environment `production`): valida identidades,
     relata a identidade de migration (#204), SchemaGuard, aplica o bundle **uma vez** e
     verifica o runtime;
   - `deploy-api`: revisao candidata com 0%, warm-up e smoke, promocao, marcador no New
     Relic e politica de revisoes;
   - `deploy-function-app`: workers.
   Nao usar o `apply-production-migrations` separado: o `migrate-production` ja aplica.
4. **Conferir** o passo "Record deployment in New Relic" sem `::warning::` e o marcador no
   card Deployments do APM. Smoke de producao (abaixo).
5. **Front: PR `developer` -> `main`** so depois do passo 4. Conferir o passo do New
   Relic do front e o marcador no app Browser "Hemodins Front PROD".

## Smoke

API (producao), logo apos a promocao:

- `/livez` 200 e `/healthz` 200 (o primeiro 503 pode ser o banco saindo da pausa);
- login com e sem MFA;
- listagem de pacientes em duas clinicas, com isolamento (uma nao ve a outra).

Depois do front:

- login com MFA (ativar e desativar em Opcoes -> Configuracoes);
- inativar e reativar paciente;
- trilha de auditoria (Admin);
- troca de clinica limpando o cache.

## Rollback

- **API**: `rollback-production` volta o trafego para a `green`. A trava do #380 recusa
  alvo sem `connectionstrings-runtime-*`. O schema **nao** volta: as migrations sao
  aditivas e o codigo antigo funciona com elas. Nao rodar `Down`.
- **Atencao de seguranca:** o codigo antigo nao conhece MFA, bloqueio de login de
  paciente nem deteccao de reuso de refresh token. Depois de um rollback:
  - usuarios com MFA ativado entram so com senha;
  - pacientes inativados ou bloqueados voltam a conseguir entrar.
  Tratar rollback como incidente: voltar para a versao nova (corrigida) o quanto antes e
  revisar os logins feitos no periodo.
- **Front**: reverter na `main` e redeployar. O front novo com a API antiga quebra o
  paciente "Inativar", o MFA e a trilha. Por isso, se a API voltar, o front volta junto.
- **Banco**: restaurar por PITR so em caso de corrupcao de dados, nunca para desfazer as
  migrations. O ensaio real de restauracao ainda nao foi feito (#206).
