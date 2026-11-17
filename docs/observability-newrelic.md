# Observabilidade no New Relic: SLOs, alertas e dashboard (#289)

Fonte da verdade: `observability/newrelic/` (`alerts.json`, `service-levels.json`, `dashboard.json`).
Aplicação: `scripts/newrelic_observability.py`, pelo workflow **New Relic Observability**.
Não edite alertas, SLOs ou o dashboard pela interface do New Relic: a próxima aplicação sobrescreve.

Princípios:
- Só o agente New Relic (.NET APM na API e Browser no front). Sem OpenTelemetry, Sentry ou serviço pago novo.
- **Sem monitor sintético que toque o banco.** Um probe periódico em `/healthz` mantinha o Azure SQL
  serverless acordado (#209). A saúde do banco é medida passivamente, no tráfego real (503 `database_unavailable`).
- Alertas e widgets **não exibem** CPF, token, senha, e-mail, mensagem livre, URI com identificador nem dado de
  paciente. Esses atributos só podem aparecer em `WHERE`; o `validate` reprova `SELECT`/`FACET` com eles.
- Falta de tráfego (madrugada, SQL pausado) não abre incidente: as condições não alertam por perda de sinal,
  e um incidente aberto fecha sozinho após 1 h sem dados.

## SLOs

Todos sobre a aplicação APM `Hemodinks API` (produção), janela móvel de **28 dias**.

| SLO | Evento válido | Evento bom/ruim | Meta |
| --- | --- | --- | --- |
| API - Disponibilidade | requisições, exceto `/livez`, `/healthz`, `/alive`, `/health` | ruim: HTTP ≥ 500 | 99,5% |
| Login - Latência | `POST /api/users/authenticate`, `/api/users/login-context`, `/api/session/renovar`, `/api/session/restaurar` | bom: ≤ 2 s | 95% |
| Rotas críticas - Latência | `/api/pacientes*`, `/api/events*`, `/api/faturamentos*` | bom: ≤ 2 s | 95% |
| Banco - Disponibilidade | requisições, exceto health | ruim: HTTP 503 | 99% |

Notas:
- A meta de latência de 95% (e não 99%) absorve o primeiro acesso após o auto-pause do SQL e o cold start (#201).
- O SLO de banco conta todo 503. Os outros 503 da API (política de senha ou e-mail indisponível) são raros;
  se distorcerem a medida, filtre pelo código na resposta quando houver atributo próprio.
- Taxa de 5xx é o complemento do SLO de disponibilidade e também tem alerta próprio.

## Rota de notificação

Política `HemoDinks API - Producao` (incidente por condição) → workflow `HemoDinks API - Producao` →
canal/destino de e-mail `HemoDinks - Email de alertas`. O endereço vem de `NEW_RELIC_ALERT_EMAIL`
(secret do repositório); não fica versionado. Sem ele, o script não cria a rota de notificação e avisa no plano.

## Alertas e runbooks

Cada condição aponta para a seção abaixo (`runbookUrl`). Primeiro passo comum: abrir o dashboard
**HemoDinks - Saude da producao** e conferir se houve deploy recente (`docs/production-blue-green-runbook.md`).

<a id="alerta-api-erros-5xx"></a>
### API - Erros 5xx

Crítico: mais de 5 respostas 5xx em 5 min, sustentado por 10 min (aviso: mais de 2).
1. No dashboard, veja **Erros por classe** e a transação. `InvalidOperationException` agora indica bug real (#293), não regra de negócio.
2. Se coincidir com um deploy, avalie o rollback (`rollback-production`).
3. 503 em volume aponta para o banco; siga o alerta de banco.
4. Não copie mensagens de erro, payloads ou identificadores de paciente para o incidente ou issue.

<a id="alerta-login-lento"></a>
### API - Login lento (p95)

Crítico: p95 acima de 3 s por 15 min em login, contexto de login e renovação de sessão.
1. Verifique se é retomada do SQL serverless (503 e latência alta só nos primeiros minutos) ou cold start de revisão nova (#201).
2. Latência persistente: confira o PBKDF2 (o custo é intencional; não reduza as iterações, AGENTS.md regra 4)
   e a CPU da revisão no Container Apps.
3. Confira o rate limit de autenticação (`AuthenticationRateLimiting`) antes de concluir que é lentidão.

<a id="alerta-banco-indisponivel"></a>
### Banco - Indisponível (503)

Crítico: mais de 20 respostas 503 em 5 min, sustentado por 10 min. Retomadas normais do auto-pause geram poucas.
1. Confira no portal do Azure o estado do Azure SQL (pausado, retomando, limite de vCore).
2. Confira firewall/identidade (`docs/issue-197-sql-runtime-network.md`) se houve mudança de rede.
3. **Não** crie monitor sintético para "manter o banco acordado": isso anula o auto-pause e gera custo (#209).

<a id="alerta-functions-filas"></a>
### Functions e filas - Falha de integração

Crítico: mais de 3 erros em 5 min, por 10 min, ao publicar em fila (`Azure.RequestFailedException`)
ou chamar Azure Functions (`HttpRequestException`/timeout).
1. Confira a Function App e a Storage Account no Azure (disponibilidade, chave, restrição de rede:
   `docs/functions-network-restriction.md`).
2. Mensagens que falharam dentro da Function vão para a fila `<nome>-poison`; confira no Storage.
3. Lacuna conhecida: os Workers não têm agente New Relic, então falhas **dentro** da Function não chegam aqui.

<a id="alerta-front-erros-js"></a>
### Front - Erros JavaScript

Crítico: mais de 20 erros JavaScript em 5 min, por 15 min (New Relic Browser do `hemodinks-front`, #262).
Só existe quando `NEW_RELIC_BROWSER_APP_NAME` está configurado.
1. No dashboard (página **Front**), veja a classe do erro e a versão publicada do front.
2. A telemetria do Browser só é ativada após o consentimento de análise; queda a zero não é incidente.
3. Não copie `errorMessage` para o issue sem revisar: pode conter texto digitado pelo usuário.

## Aplicação

Variáveis do workflow **New Relic Observability** (`.github/workflows/newrelic-observability.yml`):

| Nome | Tipo | Uso |
| --- | --- | --- |
| `NEW_RELIC_API_KEY` | secret | **User key** (`NRAK-...`). A license key do agente não serve. |
| `NEW_RELIC_ALERT_EMAIL` | secret | destino das notificações |
| `NEW_RELIC_ACCOUNT_ID` | variável | conta New Relic |
| `NEW_RELIC_REGION` | variável | `US` (padrão) ou `EU` |
| `NEW_RELIC_BROWSER_APP_NAME` | variável | nome do app Browser do front; sem ela, itens do front são ignorados |

1. Rode o workflow com `mode: plan` (somente leitura) e revise a lista criar/atualizar.
2. Rode com `mode: apply` (job com environment `production`).
3. Uma segunda aplicação sem mudança só atualiza; não cria duplicata (casamento por nome).

Localmente: `python scripts/newrelic_observability.py validate` (offline, também roda no CI);
`plan`/`apply` exigem as mesmas variáveis de ambiente.

## Teste de disparo controlado

Fazer após a primeira aplicação e registrar abaixo.
1. No New Relic, abra o workflow `HemoDinks API - Producao` e use **Test workflow**: confirma a entrega do e-mail
   sem abrir incidente.
2. Disparo real sem tocar em produção: na condição **API - Erros 5xx**, use **Preview** com o limiar
   temporariamente em `0` (não salve) para ver os pontos que abririam incidente.
3. Opcional, com aviso prévio: em horário de baixo uso, aplique localmente uma cópia de `alerts.json` com o limiar
   da condição de 5xx em `0`, gere uma requisição 5xx conhecida, confirme o e-mail e reaplique a configuração versionada.

| Data | Quem | Passo | Resultado |
| --- | --- | --- | --- |
| 2026-11-16 | Mantenedor | Primeira aplicação (`mode: apply`) e segunda rodada `mode: plan` | Políticas, 5 condições, 4 SLOs e dashboard criados; a segunda rodada só listou `ok`/`atualizar`, sem duplicatas. |
| 2026-11-16 | Mantenedor | **Test workflow** | Não disponível: o botão exige um incidente anterior na conta. Substituído pelo passo seguinte. |
| 2026-11-16/17 | Mantenedor | Condição NRQL temporária `TESTE disparo - apagar` na política `HemoDinks API - Producao` (`count(*)` de `Transaction` do app `Hemodinks API` acima de 0 por 1 min), seguida de tráfego real | O e-mail de incidente chegou em 2026-11-17 às 08:52 (BRT). A rota condição → workflow → destino de e-mail está confirmada. A condição temporária deve ser apagada após o teste. |
