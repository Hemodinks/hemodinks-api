# Issue #201 — cold start e latência da API e do Azure SQL

Medição de 03/11/2026, período de 01/10 a 03/11/2026. Complexidade média, risco
baixo: somente observabilidade. **Nenhuma alteração** de escala, `minReplicas`,
auto-pause, probes, firewall ou secrets. Coleta feita só com leituras de plano de
controle (Azure Monitor metrics, Activity Log), que não abrem conexão SQL nem
iniciam réplicas. Não foram enviadas requisições sintéticas a PROD ou HOMOL.

## Fontes disponíveis

| Fonte | O que mede | Limite |
| --- | --- | --- |
| Azure Monitor, Container App (`Replicas`, `Requests`, `ResponseTime`) | Réplicas, volume e tempo de resposta no ingress | Agregação por minuto (média/máximo); sem percentil por requisição |
| Activity Log, `databases/resume` | Início e fim de cada retomada do SQL serverless | Resolução de ~30 s (fim registrado em passos de 30 s) |
| Logs da API (Serilog, console) | Etapas de startup, probe do banco, warm-up, cada requisição | **Não são retidos**: o ambiente do Container Apps não tem destino de logs (`appLogsConfiguration` vazio). Só o log ao vivo |
| New Relic (agente .NET ativo em PROD) | Transações HTTP com percentis | Não acessível nesta sessão |
| OpenTelemetry (`http.server.request.duration`, runtime) | Histogramas por requisição | **Não exportado** em PROD: nenhuma variável `OTEL_*` configurada |

## Linha de base (outubro de 2026, PROD)

### Container Apps

- 190 arranques de réplica (0 → 1). Em 110 deles houve requisição em até 2 min.
- 17.622 requisições no período.

| Situação | Estatística por minuto | p50 | p95 | p99 |
| --- | --- | --- | --- | --- |
| 1º minuto com requisição após arranque, **com o SQL retomando** (n=65) | máximo do minuto | 1,99 s | 4,25 s | 6,94 s |
| 1º minuto com requisição após arranque, **com o SQL já Online** (n=45) | máximo do minuto | 1,94 s | 4,01 s | 13,1 s |
| Réplica de pé há ≥ 5 min (n≈965) | média do minuto | 30 ms | 362 ms | 969 ms |
| Réplica de pé há ≥ 5 min | máximo do minuto | 71 ms | 1,61 s | 3,87 s |

O primeiro minuto após o arranque tem o pior caso ~28× maior que o minuto típico
de uma réplica aquecida (p50 do máximo: 1,99 s contra 71 ms). Com essa
granularidade, os dados **não mostram** diferença clara entre "SQL retomando" e
"SQL já Online". O ingress agrega por minuto, e o front-end antecipa a retomada
do banco com `/api/warmup` antes do login.

### Azure SQL serverless (Activity Log)

| Banco | Retomadas | p50 | p95 | máximo |
| --- | --- | --- | --- | --- |
| `HemodinksDB` (PROD) | 145 | ≤ 30 s | ≤ 60 s | ≤ 60 s |
| `HemodinksDBConfirmation` (HOMOL) | 93 | ≤ 60 s | ≤ 60 s | ≤ 60 s |

Valores quantizados em passos de ~30 s: servem como limite superior, não como
medida precisa.

### Custo

Ver `docs/issue-209-azure-sql-finops.md`. Excedente de vCore de 01/10 a 02/11:
PROD R$ 134,79 e HOMOL R$ 27,74. O custo vem do número de despertares, e cada
um mantém o banco Online até o auto-pause.

## Instrumentação adicionada

1. **Abertura de conexão SQL** (`DatabaseConnectionTimingInterceptor`, nos
   contextos `AppDbContext` e `PlatformDbContext`). Mede as aberturas que o EF
   Core já faz, sem consulta adicional:
   - histograma `hemodinks.db.connection.open.duration` (ms, tag `outcome`),
     no medidor `Hemodinks.Database`;
   - log `DatabaseConnectionOpenSlow` (Warning) quando a abertura leva ≥ 1 s,
     sinal típico de retomada do serverless;
   - log `DatabaseConnectionOpenFailed` com duração, tipo do erro e número SQL
     (ex.: 40613, banco indisponível). Com `EnableRetryOnFailure`, cada
     tentativa gera um registro.

   Os logs contêm só duração, tipo do contexto e número do erro: sem connection
   string, servidor, nome do banco, SQL ou texto de exceção.
2. **Separação do cold start do Container App** no log de cada requisição:
   - `ProcessUptimeMs`: idade do processo quando a requisição terminou;
   - `FirstApplicationRequest = true`, uma vez por processo, na primeira
     requisição não-probe concluída (`/`, `/healthz`, `/readyz` e `/livez` são
     ignoradas). É, em geral, a requisição que tirou a réplica do zero.

Com os logs de etapa já existentes (`configuration_and_services`, `host_build`,
`database_initialization`, `http_started`, `first_database_ready`), a espera de
uma primeira requisição passa a ser decomposta em:

- **inicialização do processo**: `http_started` e `ProcessUptimeMs` da primeira
  requisição;
- **retomada do SQL**: `DatabaseConnectionOpenSlow` ou `DatabaseConnectionOpenFailed`
  na mesma requisição (`TraceIdentifier`);
- **aplicação**: o restante do `Elapsed` da requisição.

## Como medir sem acordar componentes

- **Nunca** criar ping periódico para medir. Os dados vêm do tráfego real.
- Logs: a API escreve em JSON no console. Para reter sem custo adicional, a
  opção já paga é o New Relic (agente ativo). Ativar o envio de logs do agente
  ou um exportador OTLP para o New Relic (`OTEL_EXPORTER_OTLP_EXTERNAL_ENDPOINT`)
  é **mudança de configuração em PROD e exige aprovação**. Ligar o Log Analytics
  no ambiente do Container Apps tem custo por GB e não foi feito.
- Plano de controle (grátis, não acorda nada): `az monitor metrics list` no
  Container App (`Replicas`, `Requests`, `ResponseTime`, PT1M) e Activity Log
  `databases/resume`, como nesta medição.
- Comparação antes/depois: repetir a tabela acima para novembro, depois do deploy
  da #209, que removeu os despertares sem usuário, e desta instrumentação.

## Testes

`HemodinksAPI.Tests/ColdStartInstrumentationTests.cs` (11 testes, LocalDB):

- abertura rápida gera métrica e nenhum log; abertura lenta gera um aviso só com
  duração e contexto;
- **negativo**: falha real de conexão (porta local fechada, senha fictícia) é
  medida, e o log não contém endereço, senha nem nome do banco;
- abertura real contra SQL Server é medida;
- probes não contam como primeira requisição; a marcação ocorre uma única vez.

Regressão ampla em SQL Server 2022 com Full-Text, em container local
(`sqlserver/docker-compose.yml`, igual ao CI): **527 aprovados, 0 falhas, 0
ignorados** (filtros ApiEndpoint, EventEmail, Health, Warmup, Session,
TeamLogin, PasswordReset, EventReminder, CriticalQuery, ColdStart, Startup).

Não há impacto em autorização, isolamento por `ClinicaId` ou dados pessoais: os
novos campos são duração, idade do processo e um booleano.

## Limitações

- **Percentis HTTP por requisição** (p50/p95/p99 reais) não estão disponíveis sem
  New Relic ou exportação OTLP. As tabelas acima usam agregados por minuto.
- O `ResponseTime` do ingress pode não incluir toda a espera de ativação da
  réplica (escala a partir do zero); não foi possível confirmar pela documentação
  disponível nesta sessão.
- Os novos logs só aparecem no log ao vivo até que haja retenção aprovada.
- Medido só em PROD para Container Apps; HOMOL roda no Render Free, sem métricas
  acessíveis por aqui.
