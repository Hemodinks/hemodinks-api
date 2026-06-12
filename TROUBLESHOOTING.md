# Troubleshooting - Hemodinks API

## API nao responde

Verifique:

```powershell
curl http://localhost:5000/healthz
docker-compose ps
docker logs hemodinks-api
```

Localmente:

```powershell
cd HemodinksAPI.Api
dotnet run
```

Se a porta 5000 estiver ocupada:

```powershell
Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue
dotnet run --urls "http://localhost:5001"
```

## Swagger ou Scalar nao abre

URLs esperadas:

- `http://localhost:5000/swagger`
- `http://localhost:5000/scalar`
- `http://localhost:5000/openapi/v1.json`

Se `/openapi/v1.json` falhar, rode:

```powershell
dotnet build .\HemodinksAPI.Api\HemodinksAPI.Api.csproj
```

## Banco nao conecta

Confira a connection string:

```powershell
$env:ConnectionStrings__DefaultConnection
```

Docker:

```powershell
docker logs hemodinks-mssql
docker-compose restart sqlserver api
```

Azure SQL:

- firewall libera o host da API?
- usuario/senha estao corretos?
- banco existe?
- a connection string esta no Render em `ConnectionStrings__DefaultConnection`?

## Migrations falham

Listar migrations:

```powershell
dotnet ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure --no-connect
```

Aplicar manualmente:

```powershell
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
```

Em desenvolvimento, para reset completo:

```powershell
dotnet ef database drop -f --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
```

## Agenda retorna `Invalid object name 'Events'`

Isso indica que a API subiu apontando para um banco que ainda nao recebeu a migration da agenda. Em producao no Render, confirme que a variavel abaixo esta configurada no servico:

```text
Database__RunMigrationsOnStartup=true
```

Depois faca um novo deploy ou reinicie o servico. O startup deve registrar `Iniciando migracao do banco de dados` e aplicar a migration `20260610234500_EnsureEventReminderColumns`, que cria a tabela `Events` quando ela ainda nao existe.

Se o servico foi criado manualmente no dashboard e nao pelo `render.yaml`, adicione essa variavel no dashboard do Render tambem.

## Login retorna 401

Possiveis causas:

- senha incorreta
- token expirado
- `JwtSettings__SecretKey`, `Issuer` ou `Audience` diferentes entre ambientes
- usuario inativo

Teste:

```powershell
curl -X POST http://localhost:5000/api/users/authenticate `
  -H "Content-Type: application/json" `
  -d '{"email":"gmarcone@gmail.com","senha":"Senha@123"}'
```

## CBHPM retorna vazio

Verifique:

```sql
SELECT COUNT(*) FROM CBHPMGeral;
SELECT TOP 10 Codigo, Procedimento, Porte FROM CBHPMGeral ORDER BY Codigo;
```

Com API rodando, teste sem filtros:

```powershell
curl "http://localhost:5000/api/cbhpm?page=1&pageSize=10" `
  -H "Authorization: Bearer <token>"
```

Se a tabela estiver vazia:

- confirme se `HemodinksAPI.Infrastructure/Data/SeedData/cbhpm-geral.json` foi copiado no publish
- reinicie a API para rodar o seed
- ou use `POST /api/cbhpm/import` com usuario administrador

Se os filtros nao retornarem:

- teste sem `codigo`, `procedimento` e `porte`
- use codigo parcial, por exemplo `1.01`
- use procedimento sem acentos quando estiver em duvida

## Agenda retorna `Invalid column name 'NextReminderAt'`

Isso indica que o banco tem a tabela `Events`, mas ainda nao tem as colunas de lembrete esperadas pela versao atual da API.

Solucoes:

1. Publique a versao que contem a migration `20260610234500_EnsureEventReminderColumns`.
2. Confirme `Database__RunMigrationsOnStartup=true` no ambiente.
3. Reinicie a API para o startup executar `Database.MigrateAsync()`.
4. Se precisar aplicar manualmente:

```powershell
dotnet ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure
```

Valide no SQL Server:

```sql
SELECT COL_LENGTH('dbo.Events', 'NextReminderAt') AS NextReminderAtColumn;
```

Se retornar `NULL`, a migration ainda nao foi aplicada no banco usado pela API.

## Upload para Azure Blob falha

Verifique variaveis:

```text
AzureStorage__ConnectionString
AzureStorage__ContainerName
AzureStorage__PublicBaseUrl
AzureStorage__PatientFilesContainerName
AzureStorage__PatientFilesPublicBaseUrl
```

Containers esperados:

- `profile-photos`
- `patient-files`

Se a Storage Account nao permitir criacao de container pela API, crie os containers manualmente no portal Azure.

## CORS no frontend

Origem padrao permitida:

```text
https://hemodinks-saude.vercel.app
```

Para outras origens:

```text
Cors__AllowedOrigins__0=https://sua-origem
Cors__AllowedOrigins__1=http://localhost:5173
```

No frontend:

```text
VITE_API_URL=https://<api-publica>
```

## Cache CBHPM parece desatualizado

O cache e em memoria por instancia da API. Ele expira sozinho, mas pode ficar com dados antigos ate:

- expirar a janela de cache
- reiniciar a API
- rodar uma importacao CBHPM, que invalida a chave

## Logs

Docker:

```powershell
docker logs -f hemodinks-api
```

Local:

```powershell
Get-Content .\HemodinksAPI.Api\logs\hemodinks-api-*.txt -Wait
```

## Testes

Backend:

```powershell
dotnet test .\HemodinksAPI.Tests\HemodinksAPI.Tests.csproj
```

Frontend:

```powershell
cd ..\hemodinks-front
npm test
npm run build
```
