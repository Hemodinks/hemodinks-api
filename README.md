# Hemodinks API

API ASP.NET Core/.NET 10 para gestao de usuarios, pacientes, arquivos, dashboard e consulta CBHPM.

## Stack

- .NET 10 e ASP.NET Core Minimal APIs
- Entity Framework Core 10 com SQL Server/Azure SQL
- CQRS com MediatR
- JWT Bearer para autenticacao e autorizacao
- Serilog para logs em console e arquivo
- Azure Blob Storage para fotos de perfil e anexos de pacientes
- IMemoryCache para consulta CBHPM em memoria
- Swagger/OpenAPI e Scalar para documentacao interativa
- Docker, Docker Compose, Render e GitHub Actions

## URLs

Ambiente local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` |
| Swagger UI | `http://localhost:5000/swagger` |
| Scalar UI | `http://localhost:5000/scalar` |
| OpenAPI JSON | `http://localhost:5000/openapi/v1.json` |
| Swagger JSON | `http://localhost:5000/swagger/v1/swagger.json` |

Ambiente publico atual:

| Recurso | URL |
| --- | --- |
| Frontend | `https://hemodinks-saude.vercel.app` |
| API | configure em `VITE_API_URL`, por exemplo `https://hemodinks-api.onrender.com` |

## Como executar

### Docker Compose

```powershell
Copy-Item .env.example .env
# Edite MSSQL_SA_PASSWORD e JWT_SECRET_KEY no .env
docker-compose up -d
```

O container da API aplica migrations no startup, cria os perfis, seeda usuarios quando necessario e carrega a tabela CBHPM a partir de `HemodinksAPI.Api/Data/SeedData/cbhpm-geral.json`.

### Desenvolvimento local

```powershell
cd HemodinksAPI.Api
dotnet restore
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=HemodinksDB;Integrated Security=true;TrustServerCertificate=true;Encrypt=false"
dotnet user-secrets set "JwtSettings:SecretKey" "troque_por_uma_chave_com_32_caracteres_ou_mais"
dotnet user-secrets set "JwtSettings:Issuer" "HemodinksAPI"
dotnet user-secrets set "JwtSettings:Audience" "HemodinksAPI"
dotnet run
```

## Configuracao

Use variaveis de ambiente, `.env` no Docker ou User Secrets localmente.

| Chave | Uso |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | SQL Server/Azure SQL |
| `JwtSettings__SecretKey` | chave HS256 com 32 bytes ou mais |
| `JwtSettings__Issuer` | emissor JWT |
| `JwtSettings__Audience` | audiencia JWT |
| `JwtSettings__ExpirationMinutes` | expiracao do token |
| `Cors__AllowedOrigins__0` | origem adicional do frontend |
| `AzureStorage__ConnectionString` | Storage Account Azure |
| `AzureStorage__ContainerName` | container de fotos, padrao `profile-photos` |
| `AzureStorage__PublicBaseUrl` | URL publica do container de fotos |
| `AzureStorage__PatientFilesContainerName` | container de anexos, padrao `patient-files` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container de anexos |
| `AzureStorage__PatientFileMaxBytes` | limite de upload de anexos |

Segredos nao devem ser gravados em `appsettings.json`.

## Autenticacao e perfis

O login retorna um JWT usado em `Authorization: Bearer <token>`.

Perfis seedados:

| Id | Perfil |
| --- | --- |
| 1 | Administrador |
| 2 | Medicos |
| 3 | Pacientes |

Principais regras:

- Administrador gerencia usuarios, pacientes, CBHPM e exclusoes.
- Medico visualiza/edita seus dados e pacientes vinculados ao proprio nome.
- Paciente acessa somente o proprio cadastro quando houver vinculo.

## Endpoints principais

| Metodo | Rota | Auth | Descricao |
| --- | --- | --- | --- |
| `GET` | `/healthz` | nao | health check |
| `POST` | `/api/users/authenticate` | nao | login JWT |
| `POST` | `/api/users/password/reset` | nao | reset por email |
| `GET` | `/api/users` | admin | lista paginada de usuarios |
| `POST` | `/api/users` | admin | cria usuario |
| `GET` | `/api/users/{id}` | sim | busca usuario |
| `PUT` | `/api/users/{id}` | sim | atualiza usuario |
| `DELETE` | `/api/users/{id}` | admin | exclui usuario |
| `PUT` | `/api/users/{id}/password` | sim | altera senha |
| `PUT` | `/api/users/{id}/password/reset` | admin | reset administrativo |
| `POST` | `/api/users/{id}/arquivos` | sim | upload de documento medico |
| `DELETE` | `/api/users/{id}/arquivos/{arquivoId}` | sim | exclui documento medico |
| `GET` | `/api/pacientes` | sim | lista paginada de pacientes |
| `GET` | `/api/pacientes/{id}` | sim | detalhe do paciente |
| `POST` | `/api/pacientes` | sim | cria paciente |
| `PUT` | `/api/pacientes/{id}` | sim | atualiza paciente |
| `DELETE` | `/api/pacientes/{id}` | admin | exclui paciente |
| `POST` | `/api/pacientes/{id}/arquivos` | sim | upload de anexo do paciente |
| `DELETE` | `/api/pacientes/{id}/arquivos/{arquivoId}` | sim | exclui anexo |
| `GET` | `/api/cbhpm` | sim | consulta CBHPM paginada |
| `POST` | `/api/cbhpm/import` | admin | importa/substitui itens CBHPM |
| `GET` | `/api/dashboard/summary` | sim | resumo do dashboard |
| `GET` | `/api/dashboard/notifications` | sim | notificacoes |

## CBHPM

A tabela `CBHPMGeral` foi criada por migration e recebe seed automatico de 1.677 procedimentos a partir do JSON gerado do PDF `Tabela-CBHPM-Geral.pdf`.

Consulta:

```http
GET /api/cbhpm?page=1&pageSize=10&codigo=1.01&procedimento=consulta&porte=2B
Authorization: Bearer <token>
```

Resposta padrao:

```json
{
  "items": [
    {
      "id": 1,
      "codigo": "1.01.01.01-2",
      "procedimento": "Em consultorio (no horario normal ou preestabelecido)",
      "porte": "2B",
      "custoOperacional": null,
      "capitulo": null,
      "grupo": null,
      "paginaPdf": 23
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalItems": 1677,
  "totalPages": 168
}
```

O backend usa `IMemoryCache` para manter a lista CBHPM em memoria. A primeira consulta carrega os dados do SQL Server; filtros, paginacao e busca passam a ser resolvidos em memoria ate expirar o cache ou ate uma importacao/seed invalidar a chave.

Configuracao atual do cache:

- chave: `cbhpm-geral:v1`
- expiracao absoluta: 12 horas
- expiracao deslizante: 2 horas
- invalidacao: importacao CBHPM e seed

## Banco de dados

Entidades principais:

- `Perfis`
- `Users`
- `Pacientes`
- `PacienteArquivos`
- `UserArquivos`
- `CBHPMGeral`

Relacionamentos:

- `Perfil 1:N Users`
- `User 1:1 Paciente`
- `Paciente 1:N PacienteArquivos`
- `User 1:N UserArquivos`
- `Paciente.CbhpmCodigo` referencia logicamente `CBHPMGeral.Codigo`

## Azure

Recursos usados pelo projeto:

| Recurso Azure | Uso |
| --- | --- |
| Azure SQL Database | persistencia relacional via EF Core/SQL Server provider |
| Azure Blob Storage | fotos de perfil no container `profile-photos` |
| Azure Blob Storage | anexos de pacientes no container `patient-files` |

Recurso Azure nao usado atualmente:

| Recurso | Status |
| --- | --- |
| Azure Queue Storage / Service Bus | nao ha produtor/consumidor no codigo atual; pode ser adicionado para processamento assincrono futuro |

## Documentacao interativa

Swagger e Scalar ficam ativos em qualquer ambiente publicado:

- Swagger: `/swagger`
- Scalar: `/scalar`
- OpenAPI consumido pelo Scalar: `/openapi/v1.json`

O Swagger/Scalar mostram o esquema `Bearer` para autenticar chamadas protegidas. Em producao, evite expor tokens reais em maquinas compartilhadas.

## Testes

```powershell
dotnet test .\HemodinksAPI.Tests\HemodinksAPI.Tests.csproj
```

## Documentos relacionados

- [Primeira execucao](./PRIMEIRA_EXECUCAO.md)
- [Implementacao](./IMPLEMENTACAO.md)
- [Troubleshooting](./TROUBLESHOOTING.md)
- [Deploy](./docs/deployment.md)
- [Exemplos HTTP](./API.http)
- [Documentacao tecnica PDF](./docs/Hemodinks-Documentacao-Tecnica.pdf)
