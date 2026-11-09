# Issue #209 — consumo excedente do Azure SQL (diagnóstico)

Diagnóstico de 03/11/2026. **Nenhuma alteração operacional foi feita** em PROD ou
HOMOL: sem mudança de auto-pause, escala, runbooks, secrets ou firewall. As
mudanças de código e workflow aprovadas estão em PR e só valem após deploy.
Coleta feita somente com leituras de plano de controle (Azure Resource Manager,
Activity Log, Azure Monitor metrics), que não abrem conexão SQL nem retomam
bancos pausados. Nenhum dado clínico, credencial ou connection string foi lido
ou registrado. Economia **não** foi alcançada; este documento é diagnóstico e
plano.

## Linha de base

### Configuração atual (lida em 03/11/2026)

| Banco | Uso | SKU | vCores mín/máx | Auto-pause | Franquia | Ao esgotar |
| --- | --- | --- | --- | --- | --- | --- |
| `HemodinksDB` | PROD | GP_S_Gen5 | 0,5 / 2 | 30 min | gratuita | `BillOverUsage` |
| `HemodinksDBConfirmation` | HOMOL | GP_S_Gen5 | 0,5 / 1 | API reporta `-1`; observado ~30 min | gratuita | `BillOverUsage` |

`hemodinks-api-prod` (Container Apps): `minReplicas = 0`, `maxReplicas = 1`.
HOMOL API: Render Free (`render.yaml`), `healthCheckPath: /healthz`.

### Custos de outubro (exportação citada na issue)

| Item | R$ |
| --- | --- |
| Computação Azure SQL acima da franquia | 144,21 |
| — PROD | 118,22 |
| — HOMOL | 25,99 |
| `HemodinksDB` (com armazenamento) | 128,71 |
| `HemodinksDBConfirmation` (com armazenamento) | 36,48 |
| Aspire Dashboard (desabilitado em 30/10) | 12,88 |
| **Total da conta** | **192,08** |

Consulta ao Cost Management (`ActualCost`, 01/10–02/11/2026, por recurso e
medidor; inclui 01–02/11):

| Recurso | Medidor | Quantidade | R$ |
| --- | --- | --- | --- |
| `hemodinksdb` | vCore (excedente) | 26,05 vCore-h (~93.800 vCore-s) | 134,79 |
| `hemodinksdbconfirmation` | vCore (excedente) | 5,36 vCore-h (~19.300 vCore-s) | 27,74 |
| `hemodinksdb` | General Purpose Data Stored | 10,24 | 11,70 |
| `hemodinksdbconfirmation` | General Purpose Data Stored | 10,24 | 11,70 |
| `hemodinks-api-prod` | vCPU + memória ativas | — | 12,70 |
| `aspire-dashboard` | vCPU + memória (ociosa e ativa) | — | 13,12 |
| Storage | operações | — | 2,40 |

Preço efetivo do excedente: **~R$ 5,17 por vCore-h (~R$ 0,00144 por vCore-s)**.
Armazenamento é custo fixo (~R$ 11,70 por banco) e não explica o excedente.
A quebra **diária** não foi coletada (a primeira tentativa retornou `429`).

### Tempo Online medido (Activity Log, outubro)

Janelas reconstruídas a partir dos eventos `resume`/`pause` bem-sucedidos.
vCore-s estimados no **piso** de 0,5 vCore; o faturado pode ser maior quando a
CPU excede o mínimo.

| Banco | Janelas Online | Horas Online | vCore-s (piso) | Mediana da janela |
| --- | --- | --- | --- | --- |
| PROD | 138 | 86,5 h | ~155.700 | 26 min |
| HOMOL | 85 | 55,9 h | ~100.600 | 30 min |

O faturado (~93.800 vCore-s excedentes em PROD, ou ~193.800 consumidos) é
maior que o piso (~155.700): em média a CPU ficou acima de 0,5 vCore durante as
janelas. A proporção entre PROD e HOMOL é coerente com o tempo Online.

**O custo é dominado pela quantidade de despertares**, não por armazenamento:
cada despertar mantém o banco Online até o auto-pause, independentemente de
quanto trabalho foi feito.

## Causas verificadas

### 1. PROD: despertar diário às 07h sem usuário (confirmado)

`automation-hemodinks-prod` executa `Start-HemodinksApi`
(`Start-AzContainerApp`) às 07:00 `America/Sao_Paulo`, de segunda a sexta
(`Start-SegSex-07h`) e aos sábados (`Start-Sabado-07h`). `Stop-HemodinksApi`
roda às 23:59 (seg–sex) e às 13:00 (sáb).

Evidência: em outubro, **25 retomadas entre 10:02 e 10:04 UTC**, uma por dia
de agendamento (ausentes aos domingos e em 12/10). Todas isoladas, com média
de **~20 min Online** (8,4 h no mês, ~15.200 vCore-s no piso, **~16% do
excedente PROD faturado**, ~R$ 22/mês). Métricas do Container App em 20/10, 22/10 e 03/11:
**zero requisições HTTP** entre 09:58 e 10:30 UTC, com réplica ativa por ~5
min (ex.: 10:02–10:07).

Mecanismo: ao iniciar, a revisão sobe uma réplica; enquanto ela vive, o banco
é contatado por:

- probe de **Readiness** `/healthz` a cada **5 s** (`publish-container.yml`),
  que executa `DatabaseHealthCheck` → `IDatabaseReadinessProbe` (`SELECT 1`);
- `EventEmailDeliveryHostedService`, a cada **30 s**: na PROD a variável
  `EventEmails__RunHostedProcessor` não está definida e o padrão é `true`.

O horário de funcionamento (seg–sex 07:00–23:59, sáb 07:00–13:00) é
**intencional**, confirmado pelo responsável em 03/11/2026, e permanece. O
problema não é o agendamento, e sim a réplica que sobe sem usuários e toca o
SQL. As mudanças implementadas abaixo atacam esse ponto.

### 2. PROD e HOMOL: o banco fica acordado enquanto houver réplica (confirmado no código)

Qualquer réplica viva contata o SQL a cada 5 s (probe) e a cada 30 s (worker
de e-mail). As rotas `/`, `/healthz` e `/readyz` executam o mesmo health check
com banco; qualquer requisição anônima à raiz da API também consulta o SQL.
Em HOMOL, o Render usa `/healthz` como health check.

Efeito: a janela Online é, no mínimo, a vida da réplica (tráfego + cooldown
do Container Apps) mais o atraso do auto-pause. O ganho de reduzir esses
contatos fica limitado aos períodos sem usuário. A medição atual não separa
esse efeito do uso real.

### 3. HOMOL: configuração divergente (a confirmar)

A API reporta `autoPauseDelay = -1` para `HemodinksDBConfirmation`, mas o
Activity Log mostra pausas automáticas regulares depois da última alteração
humana registrada (14/10/2026 19:43 UTC): janelas de exatamente 30 min em
01/11 e 03/11. Verificação passiva em 03/11: retomada às 14:44:01 UTC e pausa
automática às 15:13:42 UTC (~30 min), com a API ainda reportando `-1`. O
auto-pause está **efetivamente ativo (~30 min)**; o valor reportado pela API
não reflete o comportamento. **Confirmar no portal** antes de qualquer mudança.
Não alterei a configuração.

Desde 01/11 (inclusive no domingo) há despertares em HOMOL às ~08:35 e ~14:40
UTC. Eles não coincidem com workflows agendados do repositório (só limpeza do
GHCR), e o E2E de login usa host isolado no runner. **Origem provável,
segundo o responsável: Uptime Kuma.** Pelo plano de monitoramento, o monitor
HTTP da API roda a cada 3 h numa máquina Windows local, o que explica poucas
verificações por dia. A rota prevista no plano (`/health/live`) **não existe**:
`/health` e `/alive` só são mapeados em Development. Um monitor que responda
200 está, portanto, em `/`, `/healthz` ou `/readyz`, e todas executam
`SELECT 1`. Não confirmado: a configuração do Kuma não está no repositório.

### Abrir as URLs da aplicação acorda o banco (por desenho)

Ao carregar, o frontend chama `GET /api/warmup` uma vez por aba
(`hemodinks-front/src/main.tsx` → `warmupService.ts`). Esse endpoint executa
`SELECT 1` de propósito, para adiantar a retomada do SQL antes do login
(`docs/infrastructure-warmup.md`). Na homologação, a preparação do login faz o
mesmo. Consequência: qualquer visita à tela, mesmo sem login, mantém o banco
Online por pelo menos o atraso do auto-pause. Desativar (`VITE_WARMUP_ENABLED=false`)
reduz custo, mas o primeiro login após uma pausa espera a retomada do banco.
É uma decisão de produto. **Não alterado.**

A raiz da API (`/`) também executava o health check com banco, e qualquer
acesso anônimo à URL da API, inclusive de bots, acordava o SQL. **Alterado
neste PR** (aprovado em 03/11/2026): `/` passa a responder como `/livez`, sem
banco, com o mesmo contrato `{"status":"Healthy"}`. `/healthz` e `/readyz`
continuam validando o banco.

### 4. Deploys e CI

Em outubro, 15 das 138 retomadas PROD ocorreram durante `Publish Production` e
29 das 85 retomadas HOMOL durante `Publish Homologation` (41 execuções). São
despertares esperados por verificação de migrations e por health checks.

### 5. Itens sem evidência de impacto

- Armazenamento (~55 MB dados, ~16 MB log) e backup PITR/LTR: sem cobrança
  positiva relevante na exportação citada.
- Aspire Dashboard: desabilitado em 30/10; a queda dos encargos ainda precisa
  ser verificada em novembro.
- `EventNotificationHostedService` (lembretes, 1 min): desligado em PROD
  (`EventReminders__RunHostedProcessor=false` aplicado pelo workflow) e em
  HOMOL (`render.yaml`).

## Mudanças implementadas (aprovadas em 03/11/2026; PR, sem deploy)

Com as duas mudanças, uma réplica sem usuário autenticado não abre conexão
SQL. Isso vale para o Start das 07h, para tráfego só de probes e para
requisições anônimas ou de bots.

1. **Readiness probe em `/livez`** (`publish-container.yml`). `/livez` executa
   apenas checks `live` (sem banco). Startup e Liveness já usavam `/livez`.
   O passo de smoke da revisão candidata continua exigindo `/healthz` = 200
   (banco conectado) antes da promoção, então a validação de banco no deploy
   não muda. Vale a partir do próximo deploy em produção.
2. **Worker de e-mails da agenda com gate de atividade**
   (`EventEmailDeliveryHostedService`). Mantém o ciclo de 30 s, mas só
   chama o processador (e o banco) se a réplica atendeu uma requisição
   autenticada com sessão válida nos últimos 15 min. A fila só recebe itens
   por `POST /api/events/{id}/email-notification` (autenticado), e as
   retentativas vencem em 2 a 6 min, dentro da janela. O sinal é só um
   horário em memória, sem usuário ou tenant. Configuração:
   `EventEmails:PollOnlyAfterAuthenticatedRequest` (padrão `true`; `false`
   restaura o comportamento anterior) e `EventEmails:ActivityWindowMinutes`
   (padrão 15).

Efeito esperado: eliminar os ~25 despertares mensais das 07h (~15.200 vCore-s
no piso, ~R$ 22/mês), e as probes deixam de tocar o SQL durante o cooldown das
réplicas. **Não é economia comprovada**: medir em novembro, comparando com
outubro, as retomadas por volta de 10:03 UTC e o total de horas Online.

Rollback: reverter o commit. Ou, sem deploy de código, definir
`EventEmails__PollOnlyAfterAuthenticatedRequest=false`. A probe volta a
`/healthz` revertendo o workflow e publicando.

## Recomendações pendentes (não executadas)

| # | Mudança | Ganho estimado | Risco | Pré-requisito | Rollback |
| --- | --- | --- | --- | --- | --- |
| R5 | Revisar o auto-pause PROD | Incerto | Mais retomadas a frio | Ver abaixo | Restaurar valor |
| R6 | Uptime Kuma: apontar o monitor da API para `/livez` | Remove despertares por monitoramento (provável origem dos de 08:35/14:40 UTC em HOMOL) | Baixo: o monitor deixa de indicar falha de banco | Alteração no Kuma (máquina local do responsável) | Voltar a URL anterior |
| R7 | HOMOL (Render): `healthCheckPath` para `/livez` | Probes do Render deixam de tocar o SQL | Deploy no Render deixa de validar o banco; o `Publish Homologation` já valida antes | `render.confirmation.yaml` alterado; **aplicar também no painel do Render** (Settings → Health Check Path), pois o blueprint não é sincronizado automaticamente | Voltar para `/healthz` |

**R5, auto-pause PROD.** O responsável informou ter configurado 1 h em
produção e 30 min em homologação. A API do Azure reporta **30 min** para
`HemodinksDB` (e `-1` para HOMOL), e as janelas isoladas observadas pausaram
em ~20 min. Conferir no portal qual valor está efetivamente aplicado antes de
decidir. Reduzir o atraso diminui o custo por despertar, mas aumenta as
retomadas a frio para quem volta depois de uma pausa curta.

Despertares por uso real e por deploy permanecem e são esperados.

Controles de orçamento: orçamentos nativos do Cost Management são **avisos,
não bloqueiam** consumo. Não foram criados alertas de métrica (potencialmente
cobrados).

## Evidências e reprodução

- Configuração: `az sql db list -s <servidor-sql-producao> -g rg-hemodinks-prod`;
  `az containerapp list`; schedules/jobSchedules/runbooks via
  `az rest` em `Microsoft.Automation/automationAccounts/automation-hemodinks-prod`.
- Janelas Online: `az monitor activity-log list -g rg-hemodinks-prod` dia a
  dia, com `--max-events 1000` (com teto de 200 houve perda de evento em
  26–27/10; o maior dia teve 208 eventos), filtrando `pause`/`resume`
  `Succeeded`.
- Tráfego: `az monitor metrics list` (`Requests`, `Replicas`, PT1M) no
  Container App.
- Correlação com deploys: `gh run list` em outubro.

## Limitações

- vCore-s por janela calculados no piso de 0,5 vCore; o faturado real é maior
  (CPU acima do mínimo), então os ganhos estimados por janela são conservadores.
- Cost Management consultado sem quebra diária; o período inclui 01–02/11.
- Activity Log retém 90 dias; não há como reconstruir antes de agosto/2026.
- Requisições ao ingress fora do horário (bots/scanners) não foram
  classificadas por origem.
- Atribuição dos ~110 despertares PROD restantes a usuários reais é
  inferência por horário comercial, não comprovação.
