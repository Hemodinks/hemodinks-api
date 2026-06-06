# Deployment

Este backend foi preparado para Docker, Render, GitHub Actions e uso de recursos Azure.

## URLs

Local:

| Recurso | URL |
| --- | --- |
| API | `http://localhost:5000` |
| Health check | `http://localhost:5000/healthz` |
| Swagger | `http://localhost:5000/swagger` |
| Scalar | `http://localhost:5000/scalar` |
| OpenAPI | `http://localhost:5000/openapi/v1.json` |

Producao:

| Recurso | URL |
| --- | --- |
| Frontend | `https://hemodinks-saude.vercel.app` |
| API | `https://<api-publica>` configurada em `VITE_API_URL` |
| Swagger | `https://<api-publica>/swagger` |
| Scalar | `https://<api-publica>/scalar` |
| OpenAPI | `https://<api-publica>/openapi/v1.json` |

Se o servico Render usar o nome `hemodinks-api`, a URL publica normalmente fica no formato `https://hemodinks-api.onrender.com`, mas confirme no dashboard do Render.

## GitHub Actions

Workflows:

- `.github/workflows/ci.yml`: restaura, compila e executa testes em push/pull request para `main`.
- `.github/workflows/publish-container.yml`: publica imagem Docker no GitHub Container Registry em push para `main`, tags `v*.*.*` e execucao manual.
- `.github/workflows/vercel-deploy.yml`: gancho opcional, desativado por padrao.

Imagem:

```text
ghcr.io/hemodinks/hemodinks-api
```

Secrets:

- CI nao exige secrets.
- GHCR usa `GITHUB_TOKEN`.
- Vercel opcional: `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID`.

Variables:

- `ENABLE_VERCEL_DEPLOY=true` habilita workflow opcional da Vercel.

## Render

O `render.yaml` define:

- service: `hemodinks-api`
- runtime: `docker`
- branch: `main`
- porta interna: `10000`
- health check: `/healthz`
- auto deploy: depois que checks passam

Variaveis obrigatorias no Render:

| Chave | Descricao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | connection string do Azure SQL ou SQL Server externo |
| `JwtSettings__SecretKey` | chave JWT com 32 bytes ou mais |
| `AzureStorage__ConnectionString` | connection string da Storage Account |
| `AzureStorage__PublicBaseUrl` | URL publica do container `profile-photos` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL publica do container `patient-files` |
| `Cors__AllowedOrigins__0` | `https://hemodinks-saude.vercel.app` |

Variaveis ja declaradas no blueprint:

| Chave | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://0.0.0.0:10000` |
| `JwtSettings__Issuer` | `HemodinksAPI` |
| `JwtSettings__Audience` | `HemodinksAPI` |
| `JwtSettings__ExpirationMinutes` | `60` |
| `AzureStorage__ContainerName` | `profile-photos` |
| `AzureStorage__MaxBytes` | `1048576` |
| `AzureStorage__PatientFilesContainerName` | `patient-files` |
| `AzureStorage__PatientFileMaxBytes` | `10485760` |

Render nao fornece SQL Server gerenciado. Use Azure SQL Database, SQL Server em VM ou outro provider SQL Server compativel.

### Homologacao Render: confirmation

O arquivo `render.confirmation.yaml` define um servico separado:

- service: `hemodinks-api-confirmation`
- runtime: `docker`
- branch: `developer`
- environment: `Confirmation`
- health check: `/healthz`
- origem CORS esperada: `https://hemodinks-front-confirmation.onrender.com`

Use esse arquivo como blueprint/configuracao do ambiente de homologacao `confirmation`. Se o Render gerar uma URL diferente para o front, ajuste:

```text
Cors__AllowedOrigins__0=https://<front-confirmation>.onrender.com
```

Variaveis que devem ser diferentes de producao:

| Chave | Recomendacao |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | usar outro banco, por exemplo `HemodinksDBConfirmation` |
| `JwtSettings__SecretKey` | usar outra chave JWT |
| `JwtSettings__Issuer` | `HemodinksAPI.Confirmation` |
| `JwtSettings__Audience` | `HemodinksAPI.Confirmation` |
| `AzureStorage__ContainerName` | `profile-photos-confirmation` |
| `AzureStorage__PatientFilesContainerName` | `patient-files-confirmation` |
| `AzureStorage__PublicBaseUrl` | URL do container `profile-photos-confirmation` |
| `AzureStorage__PatientFilesPublicBaseUrl` | URL do container `patient-files-confirmation` |
| `Cors__AllowedOrigins__0` | URL do front de homologacao |

O arquivo `.env.confirmation.example` traz um modelo dessas variaveis.

Nao copie a connection string de producao para homologacao, a menos que queira intencionalmente que migrations, seeds, testes manuais e uploads usem os dados reais. Para homologacao segura, use banco e containers separados.

## Azure SQL Database

Uso:

- persistencia relacional da API
- migrations automaticas no startup
- seed automatico de perfis, usuarios iniciais e CBHPM

Checklist:

1. Crie o servidor SQL e o banco no Azure.
2. Libere firewall para o host da API.
3. Use connection string com `Encrypt=true;TrustServerCertificate=false` quando possivel.
4. Configure `ConnectionStrings__DefaultConnection` no Render.

## Azure Blob Storage

Containers usados:

- `profile-photos`: fotos de perfil de usuarios/pacientes.
- `patient-files`: anexos de pacientes.

Checklist:

1. Crie uma Storage Account.
2. Crie os containers ou permita que a API crie.
3. Configure o nivel de acesso de leitura conforme sua estrategia de seguranca.
4. Configure as URLs publicas:
   - `AzureStorage__PublicBaseUrl=https://<storage-account>.blob.core.windows.net/profile-photos`
   - `AzureStorage__PatientFilesPublicBaseUrl=https://<storage-account>.blob.core.windows.net/patient-files`

Se as URLs publicas nao forem informadas, a API usa a URL retornada pelo SDK do Azure Blob.

## Azure Queue / Service Bus

Nao ha recurso de fila em uso atualmente. Nao crie Azure Queue Storage ou Service Bus para esta versao, a menos que uma nova funcionalidade assincrona seja implementada.

Possiveis usos futuros:

- processamento de upload
- notificacoes
- relatorios
- auditoria assincrona

## Frontend

O frontend usa Vercel e deve receber:

```text
VITE_API_URL=https://<api-publica>
```

Origem publica atual permitida por padrao no CORS:

```text
https://hemodinks-saude.vercel.app
```

Para outras origens, configure `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1` etc.

## Validacao apos deploy

```powershell
curl https://<api-publica>/healthz
curl https://<api-publica>/openapi/v1.json
```

No navegador:

- `https://<api-publica>/swagger`
- `https://<api-publica>/scalar`
- `https://hemodinks-saude.vercel.app`
