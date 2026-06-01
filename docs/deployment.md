# Deployment

Este projeto esta pronto para CI/CD com GitHub Actions e deploy Docker no Render.

## GitHub Actions

Workflows:

- `.github/workflows/ci.yml`: restaura, compila e executa os testes em pushes e pull requests para `main`.
- `.github/workflows/publish-container.yml`: publica a imagem Docker no GitHub Container Registry em pushes para `main`, tags `v*.*.*` e execucao manual.
- `.github/workflows/vercel-deploy.yml`: workflow opcional para Vercel, desativado por padrao.

Imagem publicada:

```text
ghcr.io/hemodinks/hemodinks-api
```

Secrets necessarios para GitHub Actions:

- Nenhum para CI.
- Nenhum extra para publicar no GHCR; o workflow usa `GITHUB_TOKEN`.
- Para Vercel, se habilitado: `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID`.

Variables necessarias para GitHub Actions:

- `ENABLE_VERCEL_DEPLOY=true` para habilitar o workflow de Vercel.

## Render

O arquivo `render.yaml` define um Web Service Docker:

- Service: `hemodinks-api`
- Runtime: `docker`
- Branch: `main`
- Auto deploy: depois que os checks passam
- Health check: `/healthz`
- Porta: `10000`

Secrets/variaveis para preencher no Render:

- `ConnectionStrings__DefaultConnection`: connection string de um SQL Server externo.
- `JwtSettings__SecretKey`: chave JWT com pelo menos 32 bytes.
- `Cors__AllowedOrigins__0`: origem publica do frontend, por exemplo `https://hemodinks-saude.vercel.app`.
- `AzureStorage__ConnectionString`: connection string da Storage Account da Azure.
- `AzureStorage__PublicBaseUrl`: URL publica base do container, por exemplo `https://<storage-account>.blob.core.windows.net/profile-photos`.
- `AzureStorage__PatientFilesPublicBaseUrl`: URL publica base do container de anexos, por exemplo `https://<storage-account>.blob.core.windows.net/patient-files`.

Variaveis ja definidas no Blueprint:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://0.0.0.0:10000`
- `JwtSettings__Issuer=HemodinksAPI`
- `JwtSettings__Audience=HemodinksAPI`
- `JwtSettings__ExpirationMinutes=60`
- `AzureStorage__ContainerName=profile-photos`
- `AzureStorage__MaxBytes=1048576`
- `AzureStorage__PatientFilesContainerName=patient-files`
- `AzureStorage__PatientFileMaxBytes=10485760`

Observacao: Render nao fornece SQL Server gerenciado. Use um SQL Server externo, por exemplo Azure SQL, SQL Server em VM, ou outro provider compativel.

## Azure Storage para fotos de perfil

A API recebe a foto em base64, publica no Azure Blob Storage e salva no banco apenas a URL publica. Para configurar:

1. Crie uma Storage Account no Azure.
2. Crie ou permita que a API crie o container `profile-photos`.
3. Habilite acesso publico de leitura no nivel Blob para o container, ou configure uma URL publica equivalente em `AzureStorage__PublicBaseUrl`.
4. No Render, preencha `AzureStorage__ConnectionString` com a connection string da Storage Account.
5. No Render, preencha `AzureStorage__PublicBaseUrl` com `https://<storage-account>.blob.core.windows.net/profile-photos`.

Se `AzureStorage__ConnectionString` nao estiver configurada, cadastro/edicao de usuario sem foto continua funcionando, mas qualquer upload de foto retorna erro de configuracao.

## Azure Storage para anexos de pacientes

Os anexos de pacientes sao enviados para o container `patient-files`. Para configurar:

1. Crie ou permita que a API crie o container `patient-files`.
2. Habilite acesso publico de leitura no nivel Blob para esse container.
3. No Render, preencha `AzureStorage__PatientFilesPublicBaseUrl` com `https://<storage-account>.blob.core.windows.net/patient-files`.

Se `AzureStorage__PatientFilesPublicBaseUrl` ficar vazio, a API usa a URL direta retornada pelo Azure Blob, apontando para o container configurado em `AzureStorage__PatientFilesContainerName`.

## Vercel

Vercel nao executa containers Docker diretamente e nao e o destino ideal para uma Web API ASP.NET Core de longa duracao. O workflow de Vercel esta incluido apenas como gancho opcional para o caso de este repositorio ganhar uma parte Vercel-compativel no futuro, como frontend ou functions suportadas pela plataforma.

Para habilitar:

1. Crie/configure um projeto compativel no Vercel.
2. Adicione os secrets `VERCEL_TOKEN`, `VERCEL_ORG_ID` e `VERCEL_PROJECT_ID` no GitHub.
3. Adicione a variable `ENABLE_VERCEL_DEPLOY=true` no GitHub.

Sem essa variable, o job fica desativado e nao bloqueia o deploy real da API.
