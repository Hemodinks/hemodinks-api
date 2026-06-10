# Primeira execucao - Hemodinks API

## Pre-requisitos

Opcao Docker:

- Docker Desktop
- PowerShell

Opcao local:

- .NET 10 SDK
- SQL Server local, SQL Server Express ou Azure SQL
- EF Core CLI, se for usar comandos `dotnet ef`

## Subir com Docker

```powershell
cd "c:\George Marcone\GitHub\personal\HEMODINKS\hemodinks-api"
Copy-Item .env.example .env
```

Edite `.env`:

```text
MSSQL_SA_PASSWORD=uma_senha_forte
JWT_SECRET_KEY=uma_chave_com_32_caracteres_ou_mais
```

Suba os containers:

```powershell
docker-compose up -d
docker-compose ps
```

A API ficara em:

- `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Scalar: `http://localhost:5000/scalar`
- OpenAPI: `http://localhost:5000/openapi/v1.json`

## Rodar localmente

```powershell
dotnet restore
dotnet user-secrets set --project HemodinksAPI.Api "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Issuer" "HemodinksAPI"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:Audience" "HemodinksAPI"
dotnet user-secrets set --project HemodinksAPI.Api "JwtSettings:ExpirationMinutes" "60"
dotnet run --project HemodinksAPI.Api
```

As migrations sao aplicadas no startup. O seed inicial cria perfis, usuarios iniciais, licencas quando necessario e a tabela CBHPM quando estiver vazia.

## Testar login

```powershell
curl -X POST http://localhost:5000/api/users/authenticate `
  -H "Content-Type: application/json" `
  -d '{"email":"gmarcone@gmail.com","senha":"Senha@123"}'
```

Copie o token retornado e use:

```text
Authorization: Bearer <token>
```

## Testar CBHPM

```powershell
curl "http://localhost:5000/api/cbhpm?page=1&pageSize=10&procedimento=consulta" `
  -H "Authorization: Bearer <token>"
```

Se a tabela estiver populada, a resposta deve retornar itens e total proximo de `1677`.

## Testar Agenda

```powershell
$start = (Get-Date).ToUniversalTime().AddDays(1).ToString("o")
$end = (Get-Date).ToUniversalTime().AddDays(1).AddHours(1).ToString("o")

curl -X POST http://localhost:5000/api/events `
  -H "Authorization: Bearer <token>" `
  -H "Content-Type: application/json" `
  -d "{`"title`":`"Consulta teste`",`"start`":`"$start`",`"end`":`"$end`",`"notifyUser`":true,`"notifyMedicalProfile`":false,`"reminderPeriodMinutes`":60}"
```

Depois liste:

```powershell
curl "http://localhost:5000/api/events" `
  -H "Authorization: Bearer <token>"
```

## Testar frontend local

No repositorio do frontend:

```powershell
cd "c:\George Marcone\GitHub\personal\HEMODINKS\hemodinks-front"
Copy-Item .env.example .env.local
npm ci
npm run dev
```

URL padrao:

```text
http://localhost:5173
```

## Checklist

- [ ] API respondeu em `/healthz`
- [ ] Swagger abriu em `/swagger`
- [ ] Scalar abriu em `/scalar`
- [ ] Login retornou JWT
- [ ] `GET /api/cbhpm` retornou procedimentos
- [ ] `GET /api/licencas/current` retornou a licenca do usuario
- [ ] `GET /api/events` retornou a agenda
- [ ] Frontend aponta para `VITE_API_URL=http://localhost:5000`

## Documentos

- [README](./README.md)
- [Implementacao](./IMPLEMENTACAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Deploy](./docs/deployment.md)
- [Exemplos HTTP](./API.http)
