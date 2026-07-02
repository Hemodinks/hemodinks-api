# Migrations do Hemodinks API

Esta pasta usa **EF Core Migrations**. Ela guarda o historico do schema, backfills de dados e migrations de reparo operacional do projeto.

## O que cada arquivo faz

- `2026..._NomeDaMigration.cs`: migration principal com `Up()` e `Down()`.
- `2026..._NomeDaMigration.Designer.cs`: metadata gerada pelo EF Core para aquela migration.
- `AppDbContextModelSnapshot.cs`: estado atual do modelo usado pelo EF para gerar a proxima migration.

## O que nao deve ser apagado

- Nao apague `AppDbContextModelSnapshot.cs`.
- Nao apague migrations antigas ja aplicadas em ambiente algum.
- Nao apague `.Designer.cs` sem validar se a migration correspondente foi criada manualmente de proposito.

Apagar arquivos antigos sem um baseline planejado pode quebrar:

- `dotnet ef migrations add`
- `dotnet ef migrations script`
- `dotnet ef database update`
- deploys que ainda dependem da cadeia historica completa

## Tipos de migration usados neste repo

- `Schema`: cria/altera tabelas, colunas, indices e relacionamentos.
- `Data`: backfill, seed corretivo ou transformacao de dados.
- `Repair`: corrige bancos que ficaram em estado intermediario entre releases.

Exemplos atuais:

- `20260617171244_AddAgendaNotifications`: schema.
- `20260619191959_AddFaturamentosMedicos`: schema + backfill.
- `20260610234500_EnsureEventReminderColumns`: repair.

## Convencao recomendada para futuras migrations

Manter o prefixo de timestamp do EF e incluir o tipo no nome:

- `20260701120000_Schema_AddFoo`
- `20260701123000_Data_BackfillFoo`
- `20260701130000_Repair_FixFoo`

Isso melhora leitura, auditoria e decisao de rollout/rollback.

## Politica segura de rollout

Antes de publicar uma release com migration:

1. Rode `pwsh ./scripts/Test-Migrations.ps1`.
2. Gere o SQL de rollout: `pwsh ./scripts/Export-MigrationScripts.ps1`.
3. Confirme backup/PITR do banco.
4. Publique a aplicacao.
5. Valide `/healthz` e logs logo apos o deploy.

## Politica segura de rollback

Rollback de banco **nao** deve depender apenas de `Down()`.

Preferencia operacional:

1. rollback da aplicacao para a versao anterior
2. restore/PITR do banco quando a migration alterou dados ou fez reparo operacional
3. script SQL direcionado apenas quando revisado antes

Observacoes importantes:

- migrations com `migrationBuilder.Sql(...)` exigem revisao manual
- migrations de repair podem ter `Down()` vazio de forma intencional
- `Down()` que remove tabela/coluna pode causar perda de dados se usado sem backup

Para gerar SQL direcionado entre duas migrations:

```powershell
pwsh ./scripts/Export-MigrationScripts.ps1 `
  -FromMigration 20260624140047_MakeUserBirthDateOptional `
  -ToMigration 20260622214352_AddConfiguracoesSistema
```

## Auditoria automatizada

Use:

```powershell
pwsh ./scripts/Test-Migrations.ps1
```

O script verifica:

- existencia do snapshot
- presencia de `Up()` e `Down()`
- `Down()` vazio sem justificativa explicita
- migrations com SQL manual
- migrations com operacoes destrutivas
- drift entre modelo atual e ultima migration via EF CLI
