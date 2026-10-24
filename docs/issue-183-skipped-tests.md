# Issue #183 — execução dos testes ignorados

Complexidade moderada; risco alto pela cobertura de sessão, integridade entre
clínicas e migrations. Escopo restrito a testes, fixtures e gates CI; nenhuma
regra de negócio, migration de produção ou autorização alterada.

## Plano e dependências

1. Reproduzir e inventariar os 17 skips; diferenciar ausência de infraestrutura
   de falha de produto.
2. Usar a conexão SQL de teste existente nos cinco casos relacionais e corrigir
   a preparação histórica da fixture sem enfraquecer assertions.
3. Separar os 12 casos de navegador em integração reproduzível, sem secrets;
   exigir resultados reais e zero skips em cada gate.
4. Executar testes SQL, navegador, suíte API, regressões dos gates e revisar diff.

API #171 e Front #162 permaneciam abertas na consulta ao GitHub; estado da issue
não foi usado como prova de execução. As evidências de implementação/regressão
de sessão estão no [relatório da #171](issue-171-session-credential-regression.md)
(258 testes de segurança e 12 de navegador aprovados em 22/10, sem skips);
o frontend fixado em
`81d0b4afc2f4ae4f49f2900dc454b6410aa01efb` inclui o merge de isolamento da #162.
Não se declara homologação ou encerramento dessas issues por esta alteração.

## Inventário nominal 17/17

Não havia `[Fact(Skip=...)]` nesses casos: todos usavam `Assert.Skip` condicional.
O baseline local `logs/issue183/baseline-five.trx` reproduziu os cinco skips SQL.
O CI fornecia `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING`, mas esses casos
exigiam exclusivamente `HEMODINKS_TEST_LOCALDB=1`. PatientUserTenant e SurgeryType
também ignoravam a conexão configurada e construíam LocalDB diretamente.

| # | Teste (namespace `HemodinksAPI.Tests`) | Motivo e decisão |
| --- | --- | --- |
| 1 | `PatientUserTenantSqlServerTests.SqlServer_RejectsCrossClinicPatientAndUserRelationships` | Flag LocalDB ausente; crítico, CI normal com SQL de teste. |
| 2 | `SurgeryTypeSqlServerTests.SurgeryType_MigrationBackfillsLegacyRowsAndAllowsOldWritersWithoutChangingExistingData` | Flag LocalDB ausente; crítico, CI normal, compatibilidade de escritor antigo/idempotência. |
| 3 | `SurgeryTypeSqlServerTests.SurgeryType_RollbackRefusesUrgencyInEitherTableBeforeChangingSchemaOrData` | Flag LocalDB ausente; crítico, CI normal, rollback sem perda de dados. |
| 4 | `EventCreatorMigrationTests.CreatorMigration_PreservesLegacyUnknownCreatorAndRejectsCrossClinicAtDatabaseBoundary` | Flag LocalDB ausente; crítico, CI normal, legado e FK entre clínicas. |
| 5 | `PacienteCommandHandlerTests.DeletePaciente_SqlServer_EnforcesScopeAndCascadesOnlyAuthorizedPatient` | Flag LocalDB ausente; crítico, CI normal, exclusão por ID conhecido e proteção de clínica remota. |
| 6 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "individual")` | Frontend ausente; gate E2E obrigatório. |
| 7 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "selection")` | Mesmo motivo; gate E2E obrigatório. |
| 8 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "pin")` | Mesmo motivo; gate E2E obrigatório. |
| 9 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "invalid-pin")` | Mesmo motivo; gate E2E obrigatório. |
| 10 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "anonymous")` | Mesmo motivo; gate E2E obrigatório. |
| 11 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "anonymous-write")` | Mesmo motivo; gate E2E obrigatório. |
| 12 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "cross-tenant")` | Mesmo motivo; gate E2E obrigatório. |
| 13 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "operator-swap")` | Mesmo motivo; gate E2E obrigatório. |
| 14 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "replay")` | Mesmo motivo; gate E2E obrigatório. |
| 15 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "cancel")` | Mesmo motivo; gate E2E obrigatório. |
| 16 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "layout")` | Mesmo motivo; gate E2E obrigatório. |
| 17 | `LoginBrowserTests.Browser_UsesRealApi(scenario: "bootstrap-lock")` | Mesmo motivo; gate E2E obrigatório. |

O motivo exato comum aos 12 era: `Set HEMODINKS_E2E_FRONT_PATH to the frontend
checkout to run Playwright against an isolated real API.` Os cinco SQL
orientavam configurar a flag LocalDB; EventCreator mencionava migration aditiva
e SurgeryType seus bancos LocalDB descartáveis. Isso era pré-condição ausente,
não 17 falhas comprovadas da aplicação.

Criticidade: os cinco relacionais são altos por protegerem integridade e
compatibilidade. Entre os navegadores, os fluxos de sessão, autorização,
replay e isolamento são altos; `layout` é regressão visual/acessibilidade de
criticidade moderada e permanece no mesmo gate controlado. Nenhum dos 17 foi
excluído da cobertura ou relegado a execução manual silenciosa.

## Correção e gates

- `SqlServerTestConnection.RequireConfigured` aceita conexão explicitamente
  dedicada a testes ou opt-in LocalDB. Cada fixture continua gerando um nome de
  banco único e removendo somente seu banco em `finally`; nunca lê appsettings.
- PatientUserTenant usa `EnsureCreated` com o schema atual. Antes do rollback
  antigo, desfaz as migrations posteriores de creator e entregas de email,
  cujas três FKs dependem da chave composta de Users; após Up válido, repõe as
  duas em ordem cronológica. Preserva erro 547, transação/rollback, auditoria SQL
  real e seis negativas entre clínicas. Não se alteram migrations de produção.
- CI e validação de publicação executam a suíte API excluindo explicitamente
  `LoginBrowserTests`. O gate TRX exige os cinco testes SQL e rejeita qualquer
  resultado diferente de Passed, arquivo ausente, zero resultados ou duplicação.
  A identidade é o `testId`: nomes de teorias podem ser truncados pelo xUnit e
  coincidir legitimamente. Os cinco nomes obrigatórios devem ocorrer uma vez.
- Workflow reutilizável `login-e2e.yml` executa os 12 cenários separadamente com
  frontend público fixado por SHA, Node 22, `npm ci` e Chromium. Chamado por CI
  e publicação; esta depende também de seu sucesso. Sem PAT, Azure, secrets ou
  credenciais Git persistidas. Não usa `pull_request_target`.
- A fixture E2E usa API Kestrel real e dados fictícios em InMemory; não substitui
  os testes relacionais. Porta estrita, rate limits e assertions preservados.
  O runner confere o JSON Playwright: pelo menos um teste aprovado por cenário,
  zero skipped/unexpected/flaky. O TRX exige exatamente os 12 nomes; individual
  também executa o teste adicional de limitação real no frontend fixado.

## Reprodução

Use somente servidor SQL descartável, com Full-Text instalado. Defina
`HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` no ambiente do processo, sem
registrar seu valor. A factory substitui o catálogo por um nome único.
O provisionamento e healthcheck do contêiner ficam nos steps SQL existentes de
[`ci.yml`](../.github/workflows/ci.yml); instalação do frontend e Chromium no
[`login-e2e.yml`](../.github/workflows/login-e2e.yml).

```powershell
dotnet build HemodinksAPI.Tests/HemodinksAPI.Tests.csproj -c Release
dotnet test HemodinksAPI.slnx -c Release --no-build --filter 'FullyQualifiedName!~LoginBrowserTests' --logger 'trx;LogFileName=api.trx' --results-directory TestResults
python -B scripts/check-test-results.py --suite api TestResults/api.trx

# Checkout do frontend no SHA acima; instalar dependências e Chromium antes.
# Definir HEMODINKS_E2E_FRONT_PATH como caminho absoluto desse checkout.
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~LoginBrowserTests' --logger 'trx;LogFileName=login-e2e.trx' --results-directory TestResults
python -B scripts/check-test-results.py --suite login-e2e TestResults/login-e2e.trx
python -B -m unittest discover -s scripts/tests -p test_test_results_gate.py -v
```

## Resultados e limites

- Baseline SQL: cinco ignorados, zero executados com aprovação.
- Após correção, SQL Server descartável, sem flag LocalDB: **cinco aprovados,
  zero ignorados**, 11s (`logs/issue183/sql-five.trx`). Contêiner removido.
- Build Release aprovado: zero erros, 24 IDE0005 preexistentes no primeiro
  build; resultado final incremental zero erros/avisos.
- Doze testes do gate aprovados, incluindo nomes truncados legítimos e IDs
  duplicados; seis regressões de gates de produção aprovadas. O gate também
  rejeitou o TRX real do baseline com cinco skips.
- E2E: **12 aprovados, zero falhas/ignorados**, 4min12s; validação JSON
  Playwright e gate TRX aprovados (`logs/issue183/login-e2e.trx`).
- Suíte API completa (navegador separado): **1.053 aprovados, zero falhas e
  zero ignorados**, 13min15s (`logs/issue183/api-full.trx`), incluindo os cinco
  SQL. Gate TRX aprovado sobre o resultado real; contêiner SQL removido.
- Total das duas suítes: **1.065 aprovados, zero ignorados**, em execuções
  separadas, sem contar novamente os cinco SQL direcionados. Os três workflows
  passaram no parse YAML; DevOps também validou a sintaxe de 39 steps Bash.

DevOps (medium) e Security (high) revisaram independentemente, sem editar.
Principal implementou e executou os testes; modelo/esforço efetivos não expostos.
Não houve commit, merge, deploy ou alteração de dados reais pelo agente.
CI remoto da alteração ainda não foi executado; execução local não comprova CI
verde no GitHub. A publicação das alterações depende do fluxo normal de revisão.
Status: implementação e validação local concluídas. Permanecem pendentes o
commit/PR para `developer` e a confirmação de execução dos novos gates no GitHub;
nenhum commit, push ou PR foi criado automaticamente sem autorização explícita.
