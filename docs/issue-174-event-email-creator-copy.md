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
