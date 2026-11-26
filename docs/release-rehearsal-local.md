# Ensaio local de release (#398)

Valida, sem homologação e sem custo, que as migrations e a API da `developer` funcionam sobre o estado atual
de produção (a `main`). Usado no release de dezembro de 2026. Roda só contra SQL Server em Docker e APIs em
`localhost`.

O que fica coberto:

- Migração pelo mesmo caminho do `apply-production-migrations`: SchemaGuard com usuário de runtime restrito,
  efbundle, SchemaGuard pós-migração e script idempotente sem efeito na segunda execução.
- Backup com checksum, restore e `ValidateRestoredDatabase.sql`.
- `scripts/release_rehearsal.py`: dados criados pela API da `main`, verificação na API da `developer`: login,
  inativar/reativar paciente com motivo, trilha de auditoria, MFA e isolamento entre duas clínicas (404 cruzado,
  trilha sem oráculo de existência).

Fora do escopo: tempos e locks do Azure SQL, restore do backup real de produção (#206), deploy, New Relic.

## Pré-requisitos

- Container `hemodinks-sqlserver-dev` no ar: `docker compose -f sqlserver/docker-compose.yml up -d` (porta 14330, Full-Text).
- User-secrets da Api com `ConnectionStrings:DefaultConnection` para esse container e `Seed:InitialPassword`.
- `Platform:SuperAdminEmails` do `appsettings.Development.json` contém o admin do seed (cria a clínica B).

Nos comandos abaixo, `$cs` é a connection string `sa` da base de ensaio (`Database=HemodinksDBRelease`) e
`$estado` é um caminho **fora do repositório** (o arquivo guarda as senhas geradas).

## 1. Base no estado de produção, criada pela API da main

```powershell
git worktree add --detach $env:TEMP\hd-main-wt origin/main
dotnet build $env:TEMP\hd-main-wt\HemodinksAPI.Api\HemodinksAPI.Api.csproj
$env:ASPNETCORE_ENVIRONMENT = "Development"; $env:ConnectionStrings__DefaultConnection = $cs
dotnet run --no-build --project $env:TEMP\hd-main-wt\HemodinksAPI.Api --no-launch-profile --urls http://localhost:5001
```

Em outro terminal, quando aparecer `Inicializacao do banco de dados concluida`:

```powershell
$env:HEMODINKS_REHEARSAL_ADMIN_EMAIL = "<admin do seed>"
$env:HEMODINKS_REHEARSAL_SEED_PASSWORD = "<Seed:InitialPassword>"
python -I scripts/release_rehearsal.py seed http://localhost:5001 $estado
```

Pare a API da main. O `seed` pode ser repetido: retoma do arquivo de estado.

## 2. Migração como em produção

```powershell
dotnet build HemodinksAPI.slnx -c Release
dotnet build tools/Hemodinks.SchemaGuard/Hemodinks.SchemaGuard.csproj -c Release
./scripts/Test-Migrations.ps1 -NoBuild -Configuration Release
dotnet ef migrations bundle --context AppDbContext -p HemodinksAPI.Infrastructure -s HemodinksAPI.Api `
  -c Release --no-build --self-contained -r win-x64 -o $env:TEMP\hd-release\efbundle.exe
```

O SchemaGuard só aceita um usuário contido membro apenas de `hemodinks_runtime`. No SQL local:

1. `sp_configure 'contained database authentication', 1` e `ALTER DATABASE HemodinksDBRelease SET CONTAINMENT = PARTIAL`.
2. `CREATE USER hemodinks_runtime_ensaio WITH PASSWORD = '<aleatória>'`.
3. Rode `scripts/ConfigureSqlRuntimeRole.sql` na mesma sessão, depois de definir `Runtime197ExpectedDatabase` e
   `Runtime197User` com `sp_set_session_context`.

Com `ConnectionStrings__DefaultConnection` apontando para esse usuário:

| Passo | Esperado |
| --- | --- |
| SchemaGuard antes | código 10 (histórico válido, migrations pendentes) |
| `efbundle.exe --connection $cs` | `Applying migration` de cada pendente e `Done.` |
| SchemaGuard depois | código 0 |
| Script idempotente (`migrations script --idempotent`) de novo | sem erro e sem novas linhas em `__EFMigrationsHistory` |

Depois remova o usuário contido e volte a base para `CONTAINMENT = NONE` (veja Armadilhas).

## 3. Backup, restore e integridade

```sql
BACKUP DATABASE HemodinksDBRelease TO DISK = N'/var/opt/mssql/data/ensaio.bak' WITH INIT, CHECKSUM, COPY_ONLY;
RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/data/ensaio.bak' WITH CHECKSUM;
RESTORE DATABASE [restore184-ensaio] FROM DISK = N'/var/opt/mssql/data/ensaio.bak' WITH CHECKSUM,
  MOVE N'HemodinksDBRelease' TO N'/var/opt/mssql/data/restore184-ensaio.mdf',
  MOVE N'HemodinksDBRelease_log' TO N'/var/opt/mssql/data/restore184-ensaio_log.ldf';
DBCC CHECKDB([restore184-ensaio]) WITH NO_INFOMSGS;
```

`ValidateRestoredDatabase.sql` só roda em base `restore184-*`, com `Restore184ExpectedDatabase` definido na
sessão. Esperado: toda linha `Violations` igual a 0.

## 4. Verificação na API da developer

```powershell
$env:ConnectionStrings__DefaultConnection = $cs
$env:Database__RunMigrationsOnStartup = "false"; $env:Database__RunMaintenanceOnStartup = "false"
$env:Seed__CbhpmOnStartup = "false"; $env:Seed__UsersOnStartup = "false"
dotnet run --no-build -c Release --project HemodinksAPI.Api --no-launch-profile --urls http://localhost:5002
python -I scripts/release_rehearsal.py verify http://localhost:5002 $estado
```

Esperado: `Resumo: N/N OK` e código de saída 0. O `verify` desliga o MFA que ativou no fim.

## Armadilhas

- `CONTAINMENT = PARTIAL` muda a collation do catálogo e faz o `ValidateRestoredDatabase.sql` falhar com
  *collation conflict* (Msg 468). Não é defeito do schema: valide com a base em `CONTAINMENT = NONE`.
- Script SQL passado ao `sqlcmd` por stdin com BOM UTF-8 falha na linha 1 (Msg 102). Remova o BOM ou use `-i`.
- O bundle com `-r win-x64` acrescenta `net10.0/win-x64` aos `packages.lock.json`: descarte com `git checkout`.
- Com a API local da porta 5000 rodando, `dotnet ef` sem `--no-build` falha (DLLs travadas).
- A configuração de MFA tem limite de taxa (`PasswordReset`): repetir o `verify` logo em seguida pode
  receber 429; o script espera o `retryAfterSeconds` uma vez.
- A API exige `X-Session-Refresh: 1` no login e `X-Clinica-Slug` quando o usuário tem mais de uma clínica e na
  conclusão do MFA.

## Limpeza

`git worktree remove $env:TEMP\hd-main-wt`, `DROP DATABASE` das bases de ensaio, apagar o `.bak`, o bundle e o
arquivo de estado, e `sp_configure 'contained database authentication', 0`.
