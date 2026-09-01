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

Em `Development` e `Testing`, essas rotas sobem automaticamente. Em ambiente publicado, elas so existem se `ApiDocumentation__Enabled=true`.

Se `/openapi/v1.json` falhar, rode:

```powershell
dotnet build .\HemodinksAPI.Api\HemodinksAPI.Api.csproj
```

No Render ou outro ambiente publicado, confirme:

```text
ApiDocumentation__Enabled=true
```

## Reset de senha por email nao chega

Valide a ordem de tentativa da API. O reset sempre exige confirmacao por token; quando um canal configurado falha em runtime, a API registra o erro, tenta o proximo e nunca altera a senha atual:

1. `PasswordResetFunctions__BaseUrl` valida + `PasswordResetFunctions__FunctionKey` preenchida -> chamada HTTP direta para o Function App.
2. `AsyncQueues__PasswordResetEnabled=true` -> fila Azure `password-reset-emails`.
3. SMTP direto na API.

Se estiver usando Function HTTP, confirme:

```text
PasswordResetFunctions__BaseUrl=https://<function-app>.azurewebsites.net
PasswordResetFunctions__FunctionKey=<secret>
```

Notas:

- `PasswordResetFunctions__BaseUrl` sem `https://` ou `http://` e tratada como invalida e faz a API cair para fila ou SMTP.
- A API normaliza automaticamente o sufixo `/api`, entao a URL pode ser informada com ou sem esse trecho.
- Se a Function App foi movida/recriada na Azure, gere uma nova function key e atualize `PasswordResetFunctions__FunctionKey`.

Se estiver usando fila, confira:

```text
AsyncQueues__PasswordResetEnabled=true
AsyncQueues__ConnectionString=<ou AzureStorage__ConnectionString>
AsyncQueues__PasswordResetEmailQueueName=password-reset-emails
```

Tambem confirme que o `HemodinksAPI.Workers` esta ativo e escutando a mesma fila.

Depois de mover Storage Account ou Function App entre Resource Groups, revise juntos:

```text
AsyncQueues__ConnectionString
AzureStorage__ConnectionString
AzureWebJobsStorage
PasswordResetEmailQueueName
FileExportQueueName
```

As filas configuradas na API precisam estar na mesma Storage Account que o worker escuta em `AzureWebJobsStorage`.

Se estiver usando SMTP direto, confira:

```text
Email__Provider=GmailSmtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Username=<usuario>
Email__Smtp__Password=<app-password>
Email__FromEmail=<remetente>
Frontend__ResetPasswordUrl=https://<frontend>/reset-password
```

### Email de reset com link ou layout antigo

Sintomas comuns:

- o email aponta para `https://hemodinks-homologacao.vercel.app/reset-password` e abre `404 DEPLOYMENT_NOT_FOUND`;
- o email chega em texto simples com `Clique aqui para criar uma nova senha`;
- producao usa o layout novo, mas homologacao nao.

Em homologacao, o front atual deve ser:

```text
Frontend__ResetPasswordUrl=https://hemodinks-homologacao.gestao-saude.tec.br/reset-password
```

No Render confirmation, prefira SMTP direto pela API para evitar worker antigo:

```text
AsyncQueues__PasswordResetEnabled=false
PasswordResetFunctions__BaseUrl=
PasswordResetFunctions__FunctionKey=
```

Se a homologacao precisar usar fila ou Function, atualize tambem o `HemodinksAPI.Workers` para o mesmo commit da API e confira estas variaveis no worker:

```text
Frontend__ResetPasswordUrl=https://hemodinks-homologacao.gestao-saude.tec.br/reset-password
PasswordResetEmailQueueName=password-reset-emails-confirmation
Email__BrandLogoUrl=<url publica da logomarca>
```

Se todos os canais falharem em runtime, a API registra o erro, invalida o token gerado e preserva a senha atual. Nesse caso, procure nos logs por:

```text
Falha ao enviar reset de senha via
Erro ao enviar email de reset de senha
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
dotnet tool restore
pwsh ./scripts/Test-Migrations.ps1
dotnet tool run dotnet-ef migrations list --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --no-connect
```

Aplicar manualmente:

```powershell
dotnet tool run dotnet-ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api
```

Em desenvolvimento, para reset completo:

```powershell
dotnet tool run dotnet-ef database drop -f --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api
dotnet tool run dotnet-ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api
```

Para preparar um rollback revisado antes de mexer em producao:

```powershell
pwsh ./scripts/Export-MigrationScripts.ps1
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
  -d '{"email":"gmarcone@gmail.com","senha":"SUA_SENHA"}'
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
dotnet tool run dotnet-ef database update --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api
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
