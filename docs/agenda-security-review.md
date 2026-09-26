# Revisão de segurança da Agenda e Notificações

Data: 26/09/2026. Escopo: Agenda, destinatários, notificações e lembretes. Nenhuma alteração no frontend ou no layout.

## Implementação existente analisada

- `ClinicaResolutionService` obtém a clínica dos dados autenticados; headers não trocam a clínica de uma requisição autenticada.
- `ClinicaResolutionMiddleware` valida usuário, identidade global, vínculo `UsuarioClinica`, clínica e contexto de equipe ativos. `AuthenticationSessionMiddleware` verifica a sessão e atualiza os claims de perfil.
- `AppDbContext` aplica filtros a `IClinicaOwnedEntity`, valida a clínica nas gravações e rejeita relacionamentos entre clínicas. `PlatformDbContext` tem escopo global, usado pelo worker de lembretes.
- Os endpoints de eventos usam MediatR/CQRS e o pipeline existente de validação/tratamento de erros. Foram analisados GET da lista e por ID, POST, PUT, DELETE, conclusão, médicos, destinatários e marcação de notificações como lidas. Não há PATCH de eventos registrado.
- O dashboard consulta eventos/notificações com os filtros EF existentes. A idempotência também inclui a clínica no escopo.
- Foram analisados as consultas/autorização de usuários, os vínculos globais, os perfis e os serviços, formulário e controller da Agenda no frontend. `AppContent` remonta o conteúdo por clínica/usuário/token e limpa o cache nas mudanças de sessão. O frontend usa `notificationUserIds` e `notificationGroupIds`; não envia `ClinicaId` no contrato do evento.
- O projeto `HemodinksAPI.Workers` contém o fluxo de recuperação de senha; o job de Agenda é `EventNotificationHostedService` na Infrastructure.

## Riscos encontrados e correções

1. **Grupos permitiam contornar a seleção da tela.** A resolução de grupos só verificava o escopo do médico; outros perfis podiam informar grupos ocultos, e grupos inativos/inexistentes não eram rejeitados consistentemente. Agora a consulta de grupos autorizados é compartilhada entre opções e gravação, exige clínica/atividade e valida cada ID solicitado.
2. **PUT não validava os destinatários informados.** Ele não enviava novas notificações, mas aceitava IDs que o POST deveria recusar. Agora valida antes de alterar o evento, preservando o comportamento existente de não reenviar notificações na edição.
3. **Validação de destinatários não verificava o vínculo global ativo.** As consultas usavam principalmente `User.Ativo` e o filtro EF. Agora exigem clínica explícita, clínica ativa, usuário ativo, `UsuarioClinica` ativo e identidade global ativa; equipes ficam restritas aos membros ativos da própria equipe.
4. **Worker usava envio por perfil sem clínica.** O worker de plataforma chamava `SendNotificationToMedicalProfileAsync`, sem parâmetro de tenant, e não revalidava todos os destinatários. Agora resolve usuários individualmente na clínica do evento, revalida atividade/vínculo e restringe eventos de equipe aos membros autorizados. Elimina envios duplicados ao mesmo usuário. O serviço atual apenas registra o processamento em log: este era um risco de escopo para a implementação do transporte, não evidência de vazamento externo já ocorrido.
5. **Contrato tolerava campos desconhecidos e IDs malformados.** O DTO passa a rejeitar campos desconhecidos, inclusive `ClinicaId`; coleções nulas e IDs não positivos retornam erro de validação. FluentValidation foi incorporado à validação do payload, mantendo a tradução de erros do pipeline existente.
6. **Defesa adicional na aplicação.** CRUD compara a clínica do ator ao contexto resolvido e filtra o ID do evento pela clínica; leitura e marcação de notificações também explicitam o tenant. SuperAdministrador permanece na clínica selecionada. Pacientes não podem forjar seleção de médicos ou envio ao perfil médico, recurso que já era oculto a eles.

Não foi constatado bypass do isolamento dos GET/CRUD entre clínicas no fluxo HTTP existente: os filtros EF e o middleware já forneciam essa proteção. Os testes negativos agora fixam esse comportamento como contrato.

## Arquivos de produção alterados

- `HemodinksAPI.Application/Data/IModuleDbContexts.cs`
- `HemodinksAPI.Application/Features/Events/EventRecipientScope.cs` (novo)
- `HemodinksAPI.Application/Features/Events/EventFeatureRules.cs`
- `HemodinksAPI.Application/Features/Events/EventDtos.cs`
- `HemodinksAPI.Application/Features/Events/AgendaNotificationQueryHandlers.cs`
- `HemodinksAPI.Application/Features/Events/AgendaNotificationCommandHandlers.cs`
- `HemodinksAPI.Application/Features/Events/Queries/EventQueryHandlers.cs`
- `HemodinksAPI.Application/Features/Events/Commands/EventCommandHandlers.cs`
- `HemodinksAPI.Application/Features/Events/Commands/EventCommandQueries.cs`
- `HemodinksAPI.Application/Features/Events/Commands/EventCommandValidators.cs`
- `HemodinksAPI.Infrastructure/Services/EventReminderProcessor.cs`

## Testes adicionados/atualizados

- `AgendaSecurityTests.cs`: regras da aplicação, validações, grupos ocultos/inativos, vínculos revogados/ausentes, identidade global inativa, PUT sem mutação em caso de rejeição, contexto divergente e paciente tentando selecionar médicos.
- `AgendaReminderSecurityTests.cs`: worker com duas clínicas, destinatários ativos e escopo da equipe.
- `ApiEndpointAgendaSecurityTests.cs`: GET, PUT, DELETE e conclusão de evento externo; consulta de usuários/médicos/grupos externos; POST/PUT com destinatário externo; responsável/médico/grupo de outra clínica; injeção de `ClinicaId` e tentativa de troca por headers.
- `ApiEndpointAgendaNotificationSecurityTests.cs`: criação válida, deduplicação, leitura do dashboard e marcação de lidas sem acesso às notificações de outra clínica.
- `MedicalRecipientQueryTests.cs`: fixture relacional SQLite ampliada com os vínculos de identidade exigidos pela consulta real.
- `EventReminderProcessorConcurrencyTests.cs`: fixture de SQL Server com vínculo de identidade ativo, preservando as verificações de concorrência.

## Limitações e implantação

- O teste existente de concorrência em SQL Server falhou na preparação: `SQL Server Full-Text Search nao esta instalado nesta instancia.` É necessário executá-lo em uma instância com esse componente; não foi alterado para mascarar a falha.
- Usuários legados sem vínculo ativo ficam indisponíveis como destinatários. Não há fallback que amplie o escopo; não foram modificados dados de produção.
- Clientes que enviem propriedades fora do DTO de evento passam a receber HTTP 400. O frontend analisado utiliza o contrato válido.
- O transporte atual de lembretes é um serviço de log; entrega externa real não foi exercitada. Nenhum deploy ou auditoria de dados de produção foi realizado.

## Resultados de execução

| Verificação | Resultado |
| --- | --- |
| Suíte geral da API, excluindo categoria SqlServer e LoginBrowserTests | 452 aprovados, 1 ignorado, 0 falhas |
| Rodada final específica da Agenda e consulta relacional | 23 aprovados, 0 falhas (22 casos novos e 1 teste relacional existente) |
| E2E existentes do frontend selecionados por login/sessão/clínica/agenda/perfil/permissão | 22 aprovados; 11 casos de API real sem fixture e 2 gravações de tutorial ignorados nesta invocação |
| LoginBrowserTests, com frontend e API real isolada | 11 aprovados, nenhum ignorado; executa os 11 casos sem fixture da linha anterior |
| Worker concorrente em SQL Server | Falha na migração por ausência de Full-Text Search, antes da execução do worker |
| git diff --check | Sem erros |

Os totais da rodada específica se sobrepõem à suíte geral; não devem ser somados como testes distintos. O último teste de equipe foi acrescentado após a rodada geral e passou na rodada específica. O teste de SQL Server ignorado na suíte geral é `PatientUserTenantSqlServerTests.SqlServer_RejectsCrossClinicPatientAndUserRelationships`, por pré-requisito da própria suíte. Nenhum E2E foi modificado.

Comandos principais:

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter "Category!=SqlServer&FullyQualifiedName!~LoginBrowserTests"
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter "FullyQualifiedName~AgendaSecurityTests|FullyQualifiedName~AgendaReminderSecurityTests|FullyQualifiedName~AgendaSecurity_|FullyQualifiedName~MedicalRecipientQueryTests"
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~EventReminderProcessorConcurrencyTests"
# No checkout do frontend:
npx playwright test --grep "login|sessão|clínica|clinica|agenda|perfil|permiss" --workers=2 --reporter=line
# No checkout da API, com HEMODINKS_E2E_FRONT_PATH apontando para o frontend:
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter "FullyQualifiedName~LoginBrowserTests"
```

Relatórios TRX locais preservados em `logs/agenda-security-20260926/` (diretório ignorado pelo Git).
