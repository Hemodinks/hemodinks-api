# Issue #172 — consultas críticas e migrations

Auditoria local em 16/10/2026, base `6bc78c5`. Complexidade alta e risco alto:
leitura de dados clínicos/financeiros com filtros de tenant. Escopo: pacientes,
agenda, faturamento médico/normalizado, resumo financeiro e listagem de clínicas.
Sem alteração de contrato, regra financeira, autenticação, schema ou produção.

AGENTS.md e `.codex/config.toml` foram lidos integralmente. Implementação,
investigação e testes pelo agente principal; `database` e `security` foram
acionados, mas ambos falharam por quota antes de fornecer revisão. Seus papéis
configuram esforço high; não houve revisão independente concluída. Modelo e
esforço efetivos do principal não foram informados pelo ambiente.

## Dependências e ordem

Execução solicitada explicitamente pelo usuário. As auditorias API #170/#171
possuem evidências locais em `issue-170-multiclinic-audit.md` e
`issue-171-session-credential-regression.md`, mas continuam abertas no GitHub.
As predecessoras frontend #162/#163 também estão abertas; #164 está fechada.
Esta execução antecipada fica limitada à baseline SQL e à correção de
carregamento: esses experimentos precisam de bancos sintéticos isolados e não
dependem de cache, viewport ou acessibilidade do frontend. Produzem a baseline
da API necessária para separar custo SQL de refetch/render na ordem 07/08.
Não representam conclusão das auditorias anteriores nem liberação de rollout.

## Evidências e correção

| Fluxo | Evidência na raiz do repositório | Resultado |
| --- | --- | --- |
| Pacientes | `HemodinksAPI.Application/Features/Pacientes/Queries/GetAllPacientesQueryHandler.cs:30`, `:40`, `:57`, `:61`; `PacienteProjection.cs:9` | `AsNoTracking`, projeção e paginação de 1–100 registros; count + página permanecem dois comandos com 6 ou 24 pacientes por clínica. Coleção de procedimentos e counts correlacionados são traduzidos no SQL. |
| Faturamento médico | `HemodinksAPI.Application/Features/Faturamentos/Queries/GetAllFaturamentosMedicosQueryHandler.cs:30`, `:38`, `:55`, `:61` | Dois comandos, projeção paginada; filtros de tenant e de perfil preservados. |
| Agenda | `HemodinksAPI.Application/Features/Events/Queries/EventQueryHandlers.cs:58`, `:83`; `EventFeatureRules.cs:12` | Leitura isolada: um comando; GET real sem lembretes vencidos: dois. Inclui usuários completos e não pagina. Não se alterou contrato nem processamento. |
| Relatório financeiro | `HemodinksAPI.Application/Features/Financeiro/Relatorios/FinanceiroResumoUseCases.cs:12`, `:24`, `:25`, `:34` | Quatro consultas agregadas no servidor, quatro linhas retornadas; totais e filtro por paciente de outra clínica testados. |
| Clínicas | `HemodinksAPI.Application/Features/Clinics/Platform/ClinicaPlatformRequestHandler.Queries.cs:8`, `:29` | Duas consultas: clínicas e contagem agrupada de funcionários. Perfil de plataforma usa contexto explicitamente global; administrador recebe apenas sua clínica. |
| Faturamento normalizado | `HemodinksAPI.Application/Features/Financeiro/Faturamentos/FaturamentoUseCases.cs:64`, `:74`, `:77`; `FinanceiroMapper.cs:17` | O mapper não lê contas/recebimentos, mas as três consultas de leitura os carregavam por `Full`, multiplicando linhas. `ForResponse` carrega apenas atendimento/paciente, itens, glosas e recursos. `Full` continua incluindo contas/recebimentos para todas as mutações. |
| Detalhe e pesquisa de faturamento | `HemodinksAPI.Application/Features/Financeiro/Faturamentos/FaturamentoManagementUseCases.cs:13`; `ContasReceber/ContaReceberManagementUseCases.cs:82` | Reutilizam `ForResponse`; ordenação, count, Skip/Take e autorização médica mantidos. |
| Isolamento | `HemodinksAPI.Infrastructure/Data/AppDbContext.cs:285`, `:305` | Filtros globais por contexto mantidos; nenhum `IgnoreQueryFilters` novo. Testes SQL verificam listas, IDs estrangeiros, resumo vazio, mudança de clínica, SuperAdmin tenant-scoped e contexto não resolvido. |

Não foi adicionado `AsSplitQuery`: a correção reduz o grafo sem introduzir novas
idas ao banco nem leituras separadas sujeitas a mudanças concorrentes.

## Benchmark reproduzível

SQL Server LocalDB **17.0.4025.3**, SDK **10.0.402**, EF Core **10.0.10**, Debug.
Bancos `HemodinksQueryAudit_<guid>`, criados de zero com `EnsureCreated` e
removidos no `finally`. Duas clínicas, cada uma com 6 ou 24 pacientes, eventos
e faturamentos; cada faturamento possui três itens, três glosas com três
recursos cada, três contas com três recebimentos cada. Dados inteiramente
sintéticos; nenhum arquivo de configuração da aplicação fornece a conexão.

Cada cenário tem um aquecimento e 20 execuções sequenciais. p95 = 19ª amostra
ordenada. O tempo inclui execução EF, materialização e assertions; não inclui
criação/seed, compilação inicial, inspeção de plano ou reexecução para contar
linhas. A baseline preservada é a consulta `Full` anterior com o mesmo mapper
e ordenação; é executada junto da consulta otimizada no mesmo processo/banco.
Não se impõe limiar de tempo dependente de hardware em testes.

`DbCommandInterceptor` conta comandos síncronos/assíncronos. A reexecução dos
SELECTs mede linhas recebidas antes da deduplicação EF; `SET STATISTICS XML ON`
obtém planos reais e registra apenas nomes de índices. SQL/valores dos
parâmetros não são impressos. A instrumentação permanece somente no projeto
de testes. Resultados finais: `out/issue-validation/issue172-final-targeted.trx`.

| Cenário | Comandos (6 / 24) | Linhas SQL (6 / 24) | p95 ms (6 / 24) |
| --- | --- | --- | --- |
| Pacientes, página 2 de tamanho 5 | 2 / 2 | 4 / 16 | 2,90 / 7,02 |
| Faturamento médico, página de tamanho 5 | 2 / 2 | 16 / 16 | 3,41 / 5,85 |
| Agenda, somente leitura | 1 / 1 | 6 / 24 | 1,29 / 3,47 |
| Resumo financeiro | 4 / 4 | 4 / 4 | 2,02 / 4,49 |
| Clínicas, plataforma | 2 / 2 | 4 / 4 | 1,21 / 1,77 |
| Faturamento, grafo anterior | 1 / 1 | 1.458 / 5.832 | 71,43 / 445,43 |
| Faturamento, resposta otimizada | 1 / 1 | 162 / 648 | 8,87 / 28,22 |
| Pesquisa de faturamento, página 2 de tamanho 5 | 2 / 2 | 28 / 136 | 2,44 / 8,49 |
| GET agenda com processador real, sem vencidos | 2 / 2 | 6 / 24 | 1,87 / 2,90 |

A remoção do ramo contas/recebimentos elimina **88,9% das linhas SQL** nas duas
cardinalidades. Os DTOs são comparados integralmente por serialização, incluindo
valores, itens, glosas, recursos e rowversion; paginação e detalhe também são
comparados à baseline. A diferença de p95 é evidência local, não SLA de produção.

O GET real com 6/24 lembretes vencidos apresentou **14/50 leituras e 7/25
comandos de escrita**, p95 **18,61/73,95 ms**. A fixture rearma vencimentos fora
do cronômetro e usa notificações gravadas em memória, sem envio externo. A clínica
vizinha permanece vencida e não recebe processamento/notificação. Há crescimento
por evento: candidatos + claim individual + carga + checagem de elegibilidade +
gravação final + consulta do GET. Evidência: `EventReminderProcessor.cs:39`,
`:48`, `:56`, `:69`, `:124`. Não foi removido o claim, necessário à concorrência;
uma otimização em lote precisa preservar lease, elegibilidade e disputas entre
réplicas. O caminho com notificações médicas/equipes pode ter custo adicional.

## Índices e projeções

- Pacientes: índices por clínica/data em `PacienteConfiguration.cs:81–83`;
  planos da fixture utilizaram `IX_Pacientes_ClinicaId_UserId`, chaves primárias
  e subconsultas das coleções. O sort pode envolver data do usuário e os filtros
  textuais usam `Contains`; não se presume cobertura por um único índice.
- Agenda: índices `(ClinicaId, Start, End, IsCompleted)` e
  `(ClinicaId, NextReminderAt, IsCompleted)` em `EventConfiguration.cs:61–62`.
  A fixture pequena escolheu PK de Events e índices/chaves de equipes/usuários.
- Financeiro: índices com clínica à esquerda para atendimento, itens, glosas,
  contas e recebimentos em `FinanceiroConfigurations.cs:78`, `:103`, `:122`,
  `:141`, `:162–163`, `:184`. Pesquisa paginada usou
  `IX_Faturamentos_ClinicaId_NumeroGuia`; resumo usou PKs das três tabelas
  agregadas. A consulta otimizada deixou de acessar contas e recebimentos.
- Faturamento médico: índices por clínica/paciente, pagamento e cadastro em
  `FaturamentoMedicoConfiguration.cs:88–95`.

Não se adicionaram índices especulativos: planos de 12/48 pacientes totais não
justificam custo de escrita e migration em produção. Todas as leituras medidas
mantêm o ChangeTracker vazio. A agenda ainda materializa entidades completas;
as projeções de pacientes/faturamento médico e agregações do resumo já evitam
isso. Não foi ativado lazy loading nem relaxada proteção de tenant.

## Migrations e reversibilidade

`scripts/Test-Migrations.ps1 -NoBuild` auditou **71 migrations**, listou sem
conexão e confirmou **nenhuma diferença entre modelo e snapshot**.
`scripts/Export-MigrationScripts.ps1 -NoBuild` gerou o script idempotente com
71 IDs protegidos pelo histórico e criação de `__EFMigrationsHistory`.
Artefatos: `out/issue-validation/issue172-migrations.log`, `issue172-export.log`
e `issue172-migrations/20261016-165631-migrations-idempotent.sql`.
SHA256 do script: `94E45E07ACF44EF89D68550EB5BA0EB5D6AFB51DD0006B44F6765602E9382713`.

Alertas históricos mantidos para revisão, sem reescrever migrations:

- Sem Designer: `20260608203000_BackfillCbhpmValorReferencia`,
  `20260610234500_EnsureEventReminderColumns`,
  `20260811233000_AddPacienteDataAtendimento`.
- `Up()` destrutivo detectado em `20260601200537_AddPatientsAndCpf` e
  `20260615150653_RemoveCbhpmPaginaPdf`.
- Repairs/backfills com SQL e `Down()` sem restauração completa requerem
  revisão operacional e backup/PITR; o scanner é estático e não prova reversão.
- **Falha reproduzida na aplicação do script idempotente integral em banco
  vazio:** `sqlcmd -b` terminou com código 1 e erro 207,
  `Invalid column name 'PerfilId'`. O batch de
  `20260601180816_AddUserProfiles` adiciona a coluna e executa um UPDATE raw
  que a referencia no mesmo batch, antes de sua resolução de metadados.
  Evidência: `20260601180816_AddUserProfiles.cs:15`, `:45`; linhas 108 e 143
  do script gerado. Log: `out/issue-validation/issue172-idempotent-apply.log`.
  O banco exclusivo `HemodinksMigrationAudit_<guid>` foi removido no `finally`.
  **Gerar SQL com guards não prova que ele é aplicável.** Não se reescreveu a
  migration histórica: corrigir a geração/execução dos batches em tarefa
  específica, com replay de banco vazio e de schema já migrado.
- A instância retorna `FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 0`.
  `20260810185452_Schema_AddFullTextSearchIndexes.cs:26` exige o recurso.
  É um segundo bloqueador para completar a cadeia, depois de corrigir o batch
  anterior. Aplicação integral duas vezes e rollback integral continuam
  pendentes em SQL Server com Full-Text e cópia descartável representativa.
  `EnsureCreated` nos benchmarks não valida a cadeia histórica.

Esta correção não precisa de migration: reversão é restaurar as três chamadas
de leitura para `Full` e remover `ForResponse`, sem alterar schema/dados.
Para futuras migrations, ensaiar rollout idempotente duas vezes, conferir
histórico/dados/FKs entre execuções e testar o script de retorno em cópia;
não executar `Down()` destrutivo contra dados reais sem plano de recuperação.

## Execução e limites

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter FullyQualifiedName~CriticalQuerySqlServerTests --environment HEMODINKS_TEST_LOCALDB=1 --results-directory out/issue-validation --logger 'trx;LogFileName=issue172-final-targeted.trx'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName!~CriticalQuerySqlServerTests' --environment HEMODINKS_TEST_LOCALDB=1 --results-directory out/issue-validation --logger 'trx;LogFileName=issue172-regression.trx'
pwsh ./scripts/Test-Migrations.ps1 -NoBuild
pwsh ./scripts/Export-MigrationScripts.ps1 -NoBuild -OutputDir out/issue-validation/issue172-migrations
```

O teste de regressão falhou antes da correção nas duas cardinalidades pelo
carregamento de recebimentos (`issue172-red.trx`). A execução direcionada final
passou: **2 aprovados, zero falhas/ignorados**, em 33s, incluindo todos os
cenários acima. A suíte restante foi executada separadamente para não repetir
o benchmark caro nem contaminar sua amostra com testes concorrentes.

A regressão restante terminou em **9min45s: 966 aprovados, três falhas e 12
ignorados, 981 casos**, no artefato `out/issue-validation/issue172-regression.trx`.
Somada à rodada direcionada: **968 aprovados, três falhas e 12 ignorados**.
Falhas, preservadas sem afrouxar assertions:

- `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`:
  migration interrompida por Full-Text indisponível.
- `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`:
  mesmo bloqueio de Full-Text.
- `PatientUserTenantSqlServerTests.SqlServer_RejectsCrossClinicPatientAndUserRelationships`:
  fixture aplica rollback antigo no schema atual e tenta remover
  `AK_Users_ClinicaId_Id`, ainda referenciada por
  `FK_EventEmailDeliveries_Users_ClinicaId_SenderUserId`.

São os mesmos bloqueios registrados nas auditorias #170/#174; as assertions
finais desses três casos não foram alcançadas. Os 12 ignorados são cenários
opt-in de `LoginBrowserTests.Browser_UsesRealApi`; não se declara E2E de
navegador executado nem suíte global verde. A compilação passou com os avisos
preexistentes referidos abaixo. `git diff --check` passou. A consulta ao catálogo
confirmou **zero bancos remanescentes** com os prefixos exclusivos desta auditoria.

Pendências reais: corrigir o batch idempotente histórico e completar o ensaio
integral de migrations/rollback com Full-Text;
cardinalidades/latência de rede e planos representativos de produção; custo dos
lembretes médicos/equipes; paginação dos contratos legados sem limite; avaliação
de índices com seletividade real. A auditoria não declara ausência universal de
N+1. Não houve execução de navegador/Playwright nem modificação no frontend;
testes HTTP existentes integram a regressão da API. Avisos NuGet preexistentes
de `System.Security.Cryptography.Pkcs 9.0.4` permanecem fora deste escopo.
Permanece também o achado anterior de autorização de Equipe no resumo
financeiro individual, descrito em `issue-176-payment-and-checklist-consistency.md`
na seção "Ledger financeiro é outro contrato". A presente medição não certifica
essa autorização nem modifica o endpoint.

Status: correção de leitura implementada e validada localmente; auditoria
registrada com bloqueios reais de migrations, fixtures e validação em escala.
Não foram criados commit, PR, merge ou deploy. Arquivos de produção alterados:
`FaturamentoUseCases.cs`, `FaturamentoManagementUseCases.cs` e
`ContaReceberManagementUseCases.cs`, todos sob
`HemodinksAPI.Application/Features/Financeiro/`. Adicionados somente
`HemodinksAPI.Tests/CriticalQuerySqlServerTests.cs` e este relatório.
