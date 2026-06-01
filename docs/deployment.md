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

Variaveis ja definidas no Blueprint:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://0.0.0.0:10000`
- `JwtSettings__Issuer=HemodinksAPI`
- `JwtSettings__Audience=HemodinksAPI`
- `JwtSettings__ExpirationMinutes=60`

Observacao: Render nao fornece SQL Server gerenciado. Use um SQL Server externo, por exemplo Azure SQL, SQL Server em VM, ou outro provider compativel.

## Vercel

Vercel nao executa containers Docker diretamente e nao e o destino ideal para uma Web API ASP.NET Core de longa duracao. O workflow de Vercel esta incluido apenas como gancho opcional para o caso de este repositorio ganhar uma parte Vercel-compativel no futuro, como frontend ou functions suportadas pela plataforma.

Para habilitar:

1. Crie/configure um projeto compativel no Vercel.
2. Adicione os secrets `VERCEL_TOKEN`, `VERCEL_ORG_ID` e `VERCEL_PROJECT_ID` no GitHub.
3. Adicione a variable `ENABLE_VERCEL_DEPLOY=true` no GitHub.

Sem essa variable, o job fica desativado e nao bloqueia o deploy real da API.
