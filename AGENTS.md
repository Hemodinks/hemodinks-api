# AGENTS.md — HemoDinks API

.NET 10, Minimal APIs, MediatR (CQRS), FluentValidation, EF Core + SQL Server.
Sistema de saúde multi-clínica: segurança e isolamento por clínica vêm antes de tudo.

## Mapa

- Entidades: `HemodinksAPI.Domain/Models` (`IClinicaOwnedEntity` = dado de uma clínica)
- Casos de uso: `HemodinksAPI.Application/Features/<Módulo>/` (Commands, Queries, validators)
- Endpoints: `HemodinksAPI.Api/*EndpointExtensions*.cs` — finos: mapeiam, validam e chamam MediatR
- Middlewares de sessão, tenant e auditoria: `HemodinksAPI.Api/*Middleware.cs`
- Dados: `HemodinksAPI.Infrastructure/Data/AppDbContext.cs` (filtro global de tenant e bloqueio de FK entre clínicas)
- Schema: `HemodinksAPI.Infrastructure/Data/Configurations/` (leia aqui, não nas migrations)
- Azure Functions: `HemodinksAPI.Workers/Functions/`
- Testes: `HemodinksAPI.Tests/<Área>*Tests.cs` (SQLite/InMemory). `*SqlServerTests` exigem SQL Server configurado.
- Codex: subagentes e política de esforço em `.codex/ORCHESTRATION.md`.

## Comandos

```bash
dotnet build HemodinksAPI.slnx
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName~<ClasseDeTeste>"          # focado
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName!~LoginBrowserTests"        # suíte do CI
dotnet ef migrations add <Nome> -p HemodinksAPI.Infrastructure -s HemodinksAPI.Api
```

## Não ler (gasta contexto e não ajuda)

`Data/Migrations/*.Designer.cs`, `*ModelSnapshot.cs`, `bin/`, `obj/`, `TestResults/`, `artifacts/`, `logs/`, `out/`.
Para entender o modelo, leia `Data/Configurations` e a entidade. Abra uma migration só se a tarefa for sobre ela.

## Invariantes — nunca quebrar

1. Isolamento por clínica: não remover nem contornar o filtro global. `ClinicaId` vem do contexto autenticado,
   nunca do payload. Novo `IgnoreQueryFilters` só com justificativa explícita no PR.
2. Perfis administrativos (Admin, SuperAdmin) não são bypass implícito de tenant. Autorização é sempre no backend.
3. Nunca registrar senha, PIN, token completo, secret, connection string ou CPF. Erros não retornam dado sensível.
4. Não reduzir o hash (PBKDF2-SHA256, 600 mil iterações) nem criar criptografia própria.
5. Migration destrutiva só com pedido explícito.
6. Sem commit, push, merge ou deploy sem autorização explícita. PRs vão para `developer`.
7. Sem dependência nova ou serviço pago sem requisito explícito.

## Alto risco: plano curto antes, testes negativos depois

Autenticação, autorização, sessão/JWT/refresh, `SecurityVersion`, senha/PIN/recuperação/senha temporária, MFA,
primeiro acesso, `ClinicaId`, middleware, migration, contrato consumido pelo `hemodinks-front`.

- Investigue o fluxo completo antes de editar.
- Teste obrigatório de tenant: recurso da Clínica A acessado pela Clínica B → negado ou 404, sem revelar existência.
- Mudou contrato de endpoint → avise que o `hemodinks-front` precisa acompanhar.

## Escopo

Menor mudança correta. Sem refatoração oportunista, renomeação ou troca de padrão sem pedido.
Problema fora do escopo → reporte, não corrija. Não presuma classe, tabela, endpoint ou teste sem localizar no código.

## Pronto quando

Testes focados passam (amplie a suíte conforme o risco), diff revisado, nada sensível introduzido.
Resposta final, só isto: o que mudou · arquivos principais · testes executados e resultado · riscos reais.

## Prioridade

Segurança > isolamento de dados > correção funcional > arquitetura > testes > performance > conveniência.
