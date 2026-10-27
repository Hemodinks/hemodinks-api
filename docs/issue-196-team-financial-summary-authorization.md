# Issue #196 - autorizacao da Equipe no resumo financeiro

## Diagnostico e correcao

O handler `ObterPacienteFinanceiroResumoQueryHandler` validava Paciente e Medico,
mas permitia a Equipe consultar qualquer paciente da mesma clinica. O endpoint
tambem nao propagava a equipe autenticada para a query.

A query agora recebe `CurrentEquipeId` exclusivamente do contexto autenticado.
Antes de ler valores financeiros, o handler exige equipe ativa vinculada ao
usuario de login e aplica `PacienteAccess.ApplyScope`: um membro ativo da equipe
deve estar associado ao medico principal ou a um dos auxiliares do cadastro do
paciente. Atendimento isolado nao concede acesso a Equipe.

Os filtros EF de ClinicaId, as regras existentes de Medico/Paciente, o DTO HTTP,
os calculos e o fallback legado permanecem preservados. Nao ha migration,
dependencia externa nova ou alteracao de dados reais.

## Evidencias reproduziveis

Antes da correcao, os tres casos de
`PacienteFinanceiroAuthorizationEndpointTests` falharam em dados sinteticos:
esperavam `403 Forbidden` para paciente de outra equipe da mesma clinica e
receberam `200 OK`. Depois da correcao: 5 testes direcionados aprovados,
0 falhas e 0 ignorados (3 HTTP e 2 SQL Server).

```powershell
$env:HEMODINKS_TEST_LOCALDB = '1'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --filter FullyQualifiedName~PacienteFinanceiroAuthorization
```

HTTP usa WebApplicationFactory, login real de Equipe e EF InMemory. Verifica
principal/auxiliares, equipe externa, tentativa de sobrescrever equipeId via URL,
membro desativado, paciente de outra clinica (404) e ausencia de token (401).

SQL executa o handler com filtros reais do AppDbContext em bancos LocalDB
descartaveis e unicos. Cobre legado e normalizado, os tres vinculos medicos,
equipe ausente/inexistente/inativa/de outro login, membro inativo, atendimento sem
vinculo no cadastro, pacientes inexistentes e isolamento nos dois sentidos entre
clinicas. Inclui regressao de Administrador, SuperAdministrador, Controller,
Medico e Paciente. Cada banco criado pelo teste e removido no finally.

## Limites

Validacao local com dados sinteticos; nao houve consulta a producao nem teste
de navegador. A rota da API foi exercitada por HTTP e as queries por SQL Server
separadamente. Revisao independente pelo agente Security, sem edicao paralela.
Nenhum commit, merge ou deploy executado.

A suite completa com LocalDB habilitado terminou em 23 min 55 s:
1.064 aprovados, 1 falha e 12 ignorados, total de 1.077 testes. Os cinco testes
novos da #196 tambem passaram nessa execucao. Resultado detalhado em
`out/issue-196-full.trx` (artefato local ignorado pelo Git, movido apos a execucao).
O teste
`EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`
falha ao aplicar migrations porque esta instancia nao tem SQL Server Full-Text
Search instalado. Os testes de navegador exigem `HEMODINKS_E2E_FRONT_PATH` e
nao foram habilitados. Essas limitacoes nao foram corrigidas nesta issue.

```powershell
$env:HEMODINKS_TEST_LOCALDB = '1'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --logger 'trx;LogFileName=issue-196-full.trx'
```

Build final: `dotnet build HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --verbosity quiet`,
concluido com 0 avisos e 0 erros. `git diff --check` sem erros.
