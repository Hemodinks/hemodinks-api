# Baseline de migrations (#231)

## O que mudou

- `20260908224929_Baseline` substitui as 65 migrations de `20260601133147_InitialCreate` a
  `20260908224929_AddTemporaryAccessCredentials`. As 11 migrations seguintes continuam como estavam.
- A baseline mantém o `MigrationId` `20260908224929_AddTemporaryAccessCredentials`. Bancos existentes
  (produção, homologação, cópias restauradas) já têm esse ID em `__EFMigrationsHistory`, então nem o script
  idempotente nem o bundle aplicam nada nesses bancos. Só bancos novos executam o `Up()`.
- O corte fica antes de `20260909170626_Schema_EnforcePatientUserClinicRelationships` porque
  `PatientUserTenantSqlServerTests` (obrigatório no CI) executa o Down/Up dessa migration. Com isso, todos os
  testes de `SQL_REQUIRED` ficam intactos.

Arquivos:

| Arquivo | Conteúdo |
| --- | --- |
| `Data/Migrations/20260908224929_Baseline.cs` | Schema gerado pelo EF a partir do modelo do commit `9a587c0`. |
| `Data/Migrations/20260908224929_Baseline.Designer.cs` | `TargetModel` da antiga `AddTemporaryAccessCredentials`, sem alteração (só o nome da classe). |
| `Data/Migrations/20260908224929_Baseline.FullText.cs` | Catálogo, 8 índices Full-Text e stoplist desligado em `Pacientes`, com o SQL original. |
| `Data/Migrations/20260908224929_Baseline.Legacy.cs` | Estado que a cadeia antiga deixava e o modelo não descreve: 4 defaults, 1 índice e os backfills da clínica padrão. |
| `tools/Hemodinks.SchemaGuard/ConsolidatedMigrations.cs` | Os 64 IDs removidos, para o SchemaGuard aceitar o histórico antigo. |
| `scripts/CompareDatabaseSchema.sql` | Consulta somente leitura usada na comparação abaixo. |

## SchemaGuard

O SchemaGuard só libera o deploy quando o histórico aplicado é prefixo exato das migrations compiladas. Sem
ajuste, os 76 IDs de produção contra as 12 do código dariam `InconsistentHistory` e bloqueariam todo deploy.
`SchemaHistoryGuard.NormalizeApplied` remove os 64 IDs consolidados antes da checagem. Se só parte deles
estiver presente, ou se faltar o ID da baseline, o histórico continua inconsistente (fail-closed). Um ID
desconhecido continua bloqueando como antes.

## Como a baseline foi gerada

1. Worktree no commit `9a587c0`, o último em que `20260908224929_AddTemporaryAccessCredentials` era a migration mais recente.
2. Removidas as migrations do worktree e executado
   `dotnet ef migrations add Baseline --context AppDbContext -p HemodinksAPI.Infrastructure -s HemodinksAPI.Api -o Data/Migrations`.
3. Conferido que o `BuildTargetModel` gerado é idêntico ao do `.Designer.cs` da migration de corte (`diff` vazio).
4. Copiado o `Up()`/`Down()` gerado e acrescentados o Full-Text e o estado legado.

## Validação (12/11/2026)

Feita em SQL Server 2022 com Full-Text (`sqlserver/Dockerfile.fts`, o mesmo do CI):

| Verificação | Resultado |
| --- | --- |
| Script idempotente da cadeia antiga (`developer`) em banco vazio | aplicado sem erro |
| Script idempotente da cadeia nova em banco vazio | aplicado sem erro |
| `CompareDatabaseSchema.sql` nos dois bancos | `diff` vazio (1137 linhas cada: colunas, defaults, índices, FKs, checks, Full-Text, propriedades estendidas e conteúdo de todas as tabelas) |
| Script novo sobre o banco criado pela cadeia antiga (simula produção) | 0 linhas afetadas, histórico continua com 76 IDs, schema e dados idênticos |
| SchemaGuard deste commit e do `developer` sobre esse banco, com usuário de runtime restrito | os dois: "history matches", código 0 |
| `scripts/Test-Migrations.ps1` | aprovado, sem drift entre modelo e snapshot |
| `CompareDatabaseSchema.sql` em produção × banco novo pela baseline | `diff` vazio nas 1089 linhas de schema; só diferem `rows\|` (dados reais) e `trial\|` (TrialAte de produção é de julho) |

Única diferença esperada entre um banco novo e um antigo: a ordem física das colunas (`column_id`). A cadeia
antiga acrescentava colunas no fim com `ALTER TABLE ADD`; a baseline cria cada tabela de uma vez. O EF sempre
referencia colunas pelo nome.

## Testes removidos

Testavam migrations que deixaram de existir. Os bancos que passaram por elas já estão migrados.

- `LegacyFinancialBackfillMigrationTests` (backfill de `20260722202127`).
- `FullMigrationReplaySqlServerTests.FullText_RollbackAndReapplyPreservesDataAndSchema` (Down/Up de `20260810185452`).
- `FullMigrationReplaySqlServerTests.HistoricalBackfills_PreserveExistingRowsAndTenantOwnership` (backfills de junho e julho).

`FullScript_UpgradesPreviouslyAppliedMigrationPrefixes` passou a partir do ID da baseline (estado de produção)
e de `20261012210010_AddEventAuthenticatedCreator`. `FullReplay_AppliesAllMigrationsAndCanRunTwice` continua
validando o replay completo, a execução dupla e os 8 índices Full-Text.

## Rollback

- Aplicação: a imagem anterior tem as 76 migrations e o SchemaGuard antigo, que aceitam o mesmo histórico
  (validado acima). Rollback blue/green não muda.
- Banco: nada muda em bancos existentes, então não há rollback de banco.
- Código: reverter o PR restaura as 65 migrations; bancos criados pela baseline têm o mesmo schema e o mesmo
  `MigrationId`, então continuam compatíveis.

## Limitações

- Para repetir a comparação com produção, rode `CompareDatabaseSchema.sql` de preferência numa cópia restaurada
  (a consulta lê todas as tabelas) e compare com um banco novo. Diferenças nas linhas `rows|` e `trial|` são
  esperadas (dados reais); as demais devem ser vazias.
- `__EFMigrationsHistory` fica com 76 linhas nos bancos antigos e 12 nos novos. Não apague as 64 linhas antigas:
  o SchemaGuard exige o conjunto completo.

## Próxima baseline

Repita o procedimento a partir de um commit em que a migration de corte era a última, preserve o `MigrationId`
dela, inclua o SQL manual que o modelo não gera, acrescente os IDs removidos em `ConsolidatedMigrations`
(criando um segundo conjunto, com a sua própria baseline) e compare os bancos com `CompareDatabaseSchema.sql`.
