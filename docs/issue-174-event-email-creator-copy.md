# Issue #174 — cópia opcional ao criador do evento

Integração da API com [frontend #166](https://github.com/Hemodinks/hemodinks-front/issues/166), conforme [API #174](https://github.com/Hemodinks/hemodinks-api/issues/174).
Complexidade alta, risco crítico por autorização, isolamento, concorrência e migration.
Implementação pelo agente principal; investigação/revisão independente por `database` (GPT-6.1-sol, high).
`security` participou da #170; a revisão suplementar desse agente atingiu limite de uso. Nenhum commit, push, PR, merge ou deploy.

## Contrato HTTP

As rotas permanecem `/api/events/{id}/email-notification`, com a autenticação, acesso ao evento e gate de equipe existentes.

**GET** sem parâmetro mantém a lista e contagens dos participantes originais. `GET /api/events/{id}/email-notification?copyToCreator=true` inclui a cópia na prévia e contagens, deduplicada por email normalizado. A consulta não enfileira nem envia emails.

Ambas as formas acrescentam `creatorCopy` à resposta:

```json
{
  "creatorCopy": {
    "recipient": {
      "userId": 42,
      "nome": "Criador",
      "email": "criador@example.com",
      "status": "available"
    },
    "canSend": true,
    "unavailableReason": null
  }
}
```

`recipient` é `null` quando o criador não pode receber cópia. Estados de entrega seguem `available`, `pending`, `sent`, `failed`. Motivos estáveis de indisponibilidade:

| Motivo | Significado |
|---|---|
| `creator_unknown` | Evento legado sem criador comprovável |
| `creator_not_authorized` | Criador/vínculo/global/clínica inativos, ausentes, perfil não elegível ou perda de visibilidade do evento |
| `invalid_email` | Email ausente ou inválido |
| `event_completed` | Evento concluído |
| `already_requested` | O usuário ou o email já tem uma entrega pending/sent/failed; o POST não cria outra |

**POST** aceita corpo opcional:

```json
{ "copyToCreator": true }
```

Sem corpo, `{}` ou `copyToCreator: false`, o contrato antigo é preservado: somente participantes originais elegíveis. Não há parâmetro de endereço ou ID de criador; propriedades adicionais no POST de email não determinam o destino. O contrato de criação/edição de eventos continua rejeitando campos desconhecidos, incluindo tentativa de enviar `createdByUserId`.

Resposta permanece `202` com `{ "queuedCount": N }`. Isso confirma enfileiramento, não entrega SMTP. Repetições retornam zero para destinatários já solicitados. Solicitar sem cópia e depois com cópia acrescenta somente o criador ainda não solicitado. Criador indisponível é ignorado sem bloquear os participantes válidos. Um evento sem participantes pode enfileirar somente a cópia válida quando explicitamente solicitada.

Na UI #166: notificação por email permanece desligada por padrão. Depois de persistir o evento, consultar a prévia; habilitar/marcar a subopção somente se `creatorCopy.canSend` permitir. Enviar o POST exclusivamente na confirmação do usuário e consultar GET novamente para status. Explicar o motivo quando a cópia estiver indisponível. Não acrescentar criador em `notificationUserIds`/`notificationGroupIds` e não confundir 202 com entrega final.

## Autoria e persistência

`Event.UserId` é responsável mutável, não prova autoria: `EventCommandQueries.cs:25` permite escolher responsável, e o update o altera. Por isso foi adicionado `Event.CreatedByUserId` nullable. O handler de criação grava exclusivamente `CurrentUser.Id`; a edição preserva esse campo. Para equipe, esse ID é o usuário autenticado de login da equipe, conforme o modelo atual, não um endereço de operador arbitrário.

Migration `20261012210010_AddEventAuthenticatedCreator` adiciona somente coluna nullable, índice `(ClinicaId, CreatedByUserId)` e FK composta para `(Users.ClinicaId, Users.Id)` com Restrict. Sem backfill: não se inventa autoria de eventos existentes. Eventos antigos continuam enviando aos participantes; a cópia fica indisponível. FK restrita preserva a referência de autoria; exclusões físicas devem respeitar essa integridade.

Os dados `AgendaNotifications`, flags, horários, responsáveis e agendamentos de lembretes não são modificados pelo GET/POST de email. O campo novo não muda o escopo de acesso, que continua baseado no responsável atual e nas regras existentes.

## Segurança e concorrência

- Solicitante/evento revalidados antes de resolver o criador. Não existe bypass por SuperAdmin para outra clínica.
- Criador resolvido pela referência persistida, com clínica, usuário, vínculo/global e email atuais válidos; perfil Paciente não ganha permissão nova. `EventFeatureRules.ApplyScope` também deve permitir sua leitura atual: autoria não supera reatribuição ou perda de vínculo em equipe privada. Isso restringe somente a cópia; destinatários originais mantêm sua autorização de recebimento existente.
- União de participantes e cópia deduplicada antes de construir o lote, por email normalizado; a fila também verifica usuário/email já persistidos.
- Índices únicos por evento/clínica/usuário e evento/clínica/email e leases existentes preservam idempotência entre réplicas. Sem mudança na tabela de entregas.
- Worker preserva o solicitante original da entrega e revalida seu acesso. Cópia só usa fallback quando `RecipientUserId == Event.CreatedByUserId`, com vínculo/email do criador ainda válidos e iguais ao snapshot do email solicitado.
- Mensagens individuais em texto simples: sem lista de destinatários ou exposição dos outros endereços. Falha parcial mantém status/retry individual.
- Limitação já existente: crash após sucesso SMTP e antes da gravação de status pode duplicar entrega; lease/idempotência de enfileiramento não prometem exactly-once externo.

## Verificações e pendências

Testes: opt-in ausente/falso/verdadeiro, sem participantes, criador também participante/email compartilhado, legado sem autoria, criador diferente do responsável, troca de responsável, falsificação rejeitada, tenant cruzado, conclusão do evento, revogação de solicitante/criador/vínculo/global, email inválido/alterado, falha parcial, contagem de excluídos e integridade de notificações/lembretes. Concorrência e migration exercitadas em SQL Server LocalDB descartável.

Relatórios em `out/issue-validation/` (ignorados pelo Git). Não foi enviado email real nem acessada conta Azure/produção. Migration somente gerada/testada em banco descartável; aplicação no ambiente de implantação continua pendente. Frontend #166 e E2E Playwright não foram executados neste repositório (`HEMODINKS_E2E_FRONT_PATH` ausente).

| Verificação | Resultado |
|---|---|
| Pré-correção HTTP, `issue174-red.trx` | 2 falhas reproduzem ausência da funcionalidade |
| Revogação de visibilidade antes da proteção final, `issue174-visibility-red.trx` | 4 falhas comprovam cópia indevida após reatribuição/saída de equipe privada |
| Rodada final direcionada, `issue174-final-targeted.trx` | 71 aprovados, 0 falhas, 0 ignorados; inclui HTTP, cópia, regras originais, transporte, concorrência e migration SQL |
| SQL inicial isolado, `issue174-sqlserver.trx` | 4 aprovados, 0 falhas: concorrência sem/com cópia, FK da fila e nova migration |
| `dotnet ef migrations has-pending-model-changes --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Infrastructure --context AppDbContext --no-build` | Sem mudanças pendentes entre modelo e snapshot |

A rodada direcionada final foi executada com `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter 'FullyQualifiedName~EventEmailNotificationTests\|FullyQualifiedName~EventEmailCopy_\|FullyQualifiedName~EventEmailConcurrencyTests\|FullyQualifiedName~EventCreatorMigrationTests\|FullyQualifiedName~EventEmailTransportTests' --environment HEMODINKS_TEST_LOCALDB=1 --results-directory out/issue-validation --logger 'trx;LogFileName=issue174-final-targeted.trx'`.

A fixture inicial de autoria enviava campo proibido no `EventRequest` e recebeu corretamente 400. Foi corrigida para verificar explicitamente essa rejeição e, depois, a atribuição legítima sem esse campo; sem afrouxar o contrato. A concorrência com dois destinatários permite que o segundo worker processe o destinatário ainda não leased; as assertions continuam exigindo uma tentativa/entrega por destinatário, total exato e zero reenvio do item leased.

Suíte completa com a última proteção: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --results-directory out/issue-validation --logger 'trx;LogFileName=issues170-174-complete.trx'`: **890 aprovados, 2 falhas, 37 ignorados, 929 total**, em 9m06s.

Falhas: `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas` e `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`. Ambas falharam nas migrations com **SQL Server Full-Text Search não instalado na instância**, como na execução anterior à #174; suas assertions finais não foram executadas. Testes SQL diretamente relacionados à #174 passaram, incluindo FK, concorrência e migration. Não se declara suíte global verde. A falha anterior da fixture de rollback de pacientes permanece descrita no relatório #170.

Arquivos principais: `HemodinksAPI.Api/EventEndpointExtensions.Email.cs`; `HemodinksAPI.Application/Features/Events/EventEmailRequests.cs`, `EventEmailRequestHandlers.cs`, `EventEmailRules.cs` e `Commands/EventCommandHandlers.cs`; `HemodinksAPI.Domain/Models/Event.cs`; configuração, migration e snapshot em `HemodinksAPI.Infrastructure/Data/`; `HemodinksAPI.Infrastructure/Services/EventEmailDeliveryProcessor.cs`; testes `ApiEndpointEventEmailCopyTests`, `EventEmailCreatorCopyTests`, `EventEmailCreatorVisibilityTests`, `EventCreatorMigrationTests` e `EventEmailConcurrencyTests`.

Status: implementação local concluída, com contrato documentado e verificações negativas/regressão/SQL executadas. Pendências externas: migration ainda não aplicada em ambiente implantado, frontend/E2E não executado e Full-Text Search indisponível no LocalDB para os dois testes amplos. Eventos legados sem autoria comprovada continuam sem cópia; FK de autoria restrita impede exclusão física de um criador enquanto eventos ainda o referenciam. Não houve alteração em AGENTS.md/.codex, regras de destinatários originais, lembretes ou permissões de acesso ao evento.

## Reutilização do transporte de recuperação — 2026-10-13

Verificação posterior da homologação confirmou o deploy `6c64170` e a aplicação
de `20261012210010_AddEventAuthenticatedCreator` nos logs da publicação. O worker
estava ativo, mas três entregas falharam repetidamente com `SmtpException`.
Isso atualiza as pendências de publicação registradas acima para esse ambiente,
sem afirmar entrega real ou disponibilidade do transporte em produção Azure.

Remetente e SMTP já são compartilhados: os senders de recuperação/eventos usam
`EmailOptions`, tanto na API quanto no worker. Não foi criado email, senha ou
serviço novo. A recuperação também suporta Function HTTP e fila Azure; copiar
somente o SMTP local não reproduz necessariamente seu canal efetivo.

Corrigido `ApiServiceCollectionExtensions.Application.cs`: `EventEmailFunctions`
ausente, vazio ou whitespace agora reutiliza **o par** `PasswordResetFunctions`
(BaseUrl/FunctionKey). O operador não precisa duplicar secrets quando o canal de
recuperação é a Function HTTP. O evento utiliza `/api/event-email/send`, já
existente no mesmo worker, e seu SMTP/remetente compartilhados.

Uma configuração própria não vazia requer **ambos os campos**. Configuração
parcial falha antes da rede, sem misturar a chave da recuperação com outro host
e sem cair silenciosamente no SMTP. Instalações que antes informavam somente
um campo próprio precisam fornecer o par completo ou remover o override.
HTTPS, redaction do header, validação de destinatário e HTTP 200 após conclusão
do SMTP permanecem obrigatórios. Uma chave exclusiva da função de recuperação
pode não autorizar a função de eventos; deve-se verificar sua autorização.

Limite: recuperação que funciona exclusivamente por Azure Storage Queue não
passa a enviar eventos por essa fila. Seu payload é específico para recuperação;
não foi utilizado token fictício nem marcado `sent` após simples enfileiramento.
Após login manual no painel Render, a consulta confirmou
`AsyncQueues__PasswordResetEnabled=true`, `PasswordResetFunctions__BaseUrl`
vazio e SMTP na porta 587. Portanto, a recuperação usa a fila Azure e o fallback
HTTP desta correção, sozinho, não resolve o envio de eventos nesse ambiente.
O plano Free do Render bloqueia saída SMTP nas portas 25/465/587
([documentação oficial](https://render.com/docs/free)).

A alternativa existente é configurar o par `EventEmailFunctions` para a rota
HTTPS `/api/event-email/send` do mesmo worker Azure que processa a recuperação,
preservando seu remetente e SMTP. Uma consulta sem chave e com payload vazio
recebeu HTTP 401; isso não comprova autorização da chave nem entrega SMTP.
A sessão local da CLI Azure expirou; o Cloud Shell autenticado permitiu concluir
a consulta. A fila do Render é `password-reset-emails-confirmation`, igual à do
worker `hemodinks-workers-confirmation`, que está `Running` e usa
`smtp.gmail.com:587`. A lista de funções desse worker contém recuperação,
exportação e uploads, mas **não contém `SendEventEmail`**; a consulta específica
retornou `NotFound`. A função de eventos existe no worker de produção, cujo uso
para homologação cruzaria ambientes e não foi adotado.

É necessário publicar o pacote Workers na Function App de confirmação e,
após verificar a nova função, configurar seu URL e chave restrita no par
`EventEmailFunctions` do Render de homologação. O pacote inclui as outras
funções existentes; app settings e fila de recuperação devem ser preservados.
Nenhum secret foi revelado ou copiado, nenhuma configuração remota foi
modificada e nenhum email real foi enviado. Publicação e aplicação do par no
Render, que reiniciam os serviços envolvidos, dependem de autorização explícita.
Não houve commit, push, merge ou deploy desta correção.

Pacote local preparado com `dotnet publish HemodinksAPI.Workers/HemodinksAPI.Workers.csproj --configuration Release --no-restore --output out/issue-validation/event-email-worker-package --verbosity quiet`: sucesso, com avisos IDE0005 existentes.
O `functions.metadata` contém `SendEventEmail` como POST protegido por
`AuthorizationLevel.Function`, rota `event-email/send`, além das cinco funções
existentes. O pacote não contém `local.settings.json` nem o arquivo de exemplo.
Preparação local não publica nem altera configurações Azure/Render.

O usuário autorizou publicar o pacote no worker de confirmação e configurar o
par de eventos no Render de homologação. O bloqueio inicial de autenticação
local foi resolvido com `az login` na sessão da CLI usada pela execução.

### Publicação autorizada e validação do ambiente

- ZIP local `event-email-worker-confirmation.zip`, SHA-256
  `c380f8a655acbd7fbf1d21d6500b849a66d34d55feed15e3052636a6b7e77d96`,
  inclui `.azurefunctions/` e não inclui `local.settings`.
- Publicado somente em `hemodinks-workers-confirmation`, resource group
  `hemodinks-workers-confirmation_group`. Deploy Azure
  `6b6f5991-1101-4032-a72c-92ca27590aa4`: status 4, complete/active true,
  concluído em 2026-10-13T22:32:25Z. A comparação integral dos app settings,
  realizada em memória sem exibir valores, confirmou igualdade antes/depois.
- A lista remota contém as seis funções, incluindo `SendEventEmail`.
  Utilizada a chave existente restrita à função; nenhuma chave nova foi criada.
  POST `{}` sem chave retornou 401; com chave retornou 400, antes do SMTP.
  Esses probes não enviaram email e não comprovam entrega SMTP.
- Adicionados somente `EventEmailFunctions__BaseUrl` e
  `EventEmailFunctions__FunctionKey` no Render `hemodinks-api-1`, com merge das
  variáveis existentes. URL aponta ao worker de confirmação; recuperação
  continua pela fila `password-reset-emails-confirmation` e mesmo SMTP Azure.
- A atualização das variáveis pelo conector Render iniciou automaticamente o
  deploy `dep-db7b3ni5b2lc73f8evdg`, commit remoto existente `256cab8`, live em
  2026-10-13T22:35:58Z. O diff entre esse commit e o anterior `6c64170` contém
  somente dois arquivos de testes e documentação da #171, sem mudança de
  código executável. Não houve commit/push/merge nesta execução. A correção
  local de fallback/configuração descrita acima permanece sem publicação;
  o par completo configurado funciona com o código já publicado.
- Após o rollout, `/healthz` e `/livez` responderam HTTP 200,
  `{"status":"Healthy"}`. A primeira consulta durante a implantação expirou;
  a verificação posterior com o deploy live passou.

Status: infraestrutura publicada/configurada e probes negativos/saúde aprovados.
Entrega efetiva e cópia ao autor ainda exigem um evento/destinatário de teste
autorizados; não houve email real nesta validação nem reprocessamento de
entregas `failed`. Os HTTP 503 por `session_validation_busy` são uma ocorrência
distinta de validação de sessão e não foram corrigidos por esta configuração.

Complexidade moderada, risco alto de integração/configuração de credenciais.
Architect (`high`) investigou/revisou; implementação pelo principal. Testes:
`EventEmailRegistrationTests.cs` e classe parcial `StorageRegistrationTests`;
compartilhamento da Function, blanks, override completo/parcial e SMTP preservado.
Red: quatro falhas demonstraram o defeito. Regressão final: **106 aprovados,
zero falhas/ignorados**, em 44s, incluindo recuperação de senha, transportes,
cópia, permissões e clínica cruzada. Relatório ignorado pelo Git:
`out/issue-validation/event-email-shared-config-regression.trx`.

Comando: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter "FullyQualifiedName~StorageRegistrationTests|FullyQualifiedName~EventEmailTransportTests|FullyQualifiedName~PasswordResetNotificationFallbackSenderTests|FullyQualifiedName~EventEmailNotificationTests|FullyQualifiedName~EventEmailCopy_|FullyQualifiedName~PasswordResetSecurity" --logger "trx;LogFileName=event-email-shared-config-regression.trx" --results-directory out/issue-validation --verbosity quiet`.
Entregas anteriores em estado `failed` não são reiniciadas automaticamente por esta correção. Qualquer reprocessamento exige avaliação separada de destinatários, autorização e risco de duplicidade; não foi realizada alteração da fila no ambiente.

## Validação real autorizada — 2026-10-14

O usuário autorizou os destinatários Gmail/Outlook de teste. Em 13/10, duas
mensagens técnicas com identificador `HMD-EVENT-20261013-0b3a7132` foram enviadas
pela Function de confirmação: HTTP 200 em ambos os casos. A busca restrita ao
identificador confirmou a mensagem na caixa de entrada Gmail; a caixa Outlook
não foi inspecionada. Isso comprovou o SMTP compartilhado do worker, sem dados
de pacientes, mas ainda não comprovava o fluxo da agenda.

A interface de homologação inicialmente não apresentava o checkbox de cópia.
O PR frontend #170 foi posteriormente integrado pelo usuário, merge commit
`8351640041bf4e5338d8c56bd5e8b1022396d802`; todos os checks GitHub passaram.
QA (`medium`) revisou o HEAD `df79aa1` sem editar arquivos: 29 testes
direcionados aprovados, build TypeScript/Vite aprovado, diff check aprovado e
hook copy-only executado em memória com serviços simulados. O diff continuava
restrito aos dez arquivos da correção #169. Não foi realizada nova publicação
ou integração de frontend pelo executor nesta validação.

E2E real em 14/10 na clínica de homologação originalmente utilizada:

- Reutilizado o evento técnico `Teste técnico de e-mail HMD-EVENT-20261013-0b3a7132`,
  criado no dia anterior com conteúdo sintético, sem participantes adicionais,
  grupos selecionados ou lembretes. Entregas antigas com falha não foram alteradas.
- Modal atualizado identificou o autor autenticado e seu email autorizado como
  disponível. Seleção explícita de `Enviar cópia ao criador` mudou o preview de
  zero para um destinatário e liberou a confirmação.
- Uma confirmação colocou uma cópia na fila. Após atualizar somente o status,
  a interface mostrou **1 enviado, 0 na fila, 0 falhas**, cópia solicitada e
  confirmação desabilitada para evitar novo envio.
- A busca Gmail pelo título técnico confirmou uma mensagem da agenda na caixa
  de entrada, às 2026-10-14T12:23:55Z, distinta do teste direto do transporte.
  Portanto, foram validados navegador → API → fila persistente → worker da API
  → Function Azure → SMTP → caixa de entrada do autor.
- O evento técnico foi marcado como concluído após o teste; histórico preservado.
  A captura real está no diretório ignorado pelo Git:
  `out/issue-validation/event-creator-copy-live-20261014.jpg`.

Status: envio real de evento e cópia ao autor confirmados em homologação. A cópia
continua opcional, mediante seleção no modal e autoria/email/acesso válidos.
Limites restantes: não houve reenvio de falhas antigas nem teste da caixa Outlook;
os HTTP 503 por `session_validation_busy` continuam uma investigação separada.
Nenhum secret ou dado de paciente foi inserido no relatório. Sem commit/push/merge
ou nova publicação pelo executor durante o teste real.
