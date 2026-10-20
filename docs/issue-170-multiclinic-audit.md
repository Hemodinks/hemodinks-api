# Issue #170 — autorização e isolamento multiclínica

Auditoria local em 12/10/2026, branch `developer`. Complexidade alta, risco crítico.
Issue: https://github.com/Hemodinks/hemodinks-api/issues/170. Ordem 01/08, sem predecessora.
Execução autorizada pelo usuário, apesar do status de backlog na descrição.
Investigação e revisão independentes: `security` e `database`, GPT-6.1-sol, esforço high.
Implementação e execução dos testes: agente principal. Sem commit, push, PR, merge ou deploy.

## Propriedade e autoridade

- A clínica autenticada prevalece sobre headers: `HemodinksAPI.Api/ClinicaResolutionService.cs:28` e `:60`; vínculo, usuário e clínica são validados em `ClinicaResolutionMiddleware.cs:135`.
- `Paciente.ClinicaId` é a propriedade do registro; `User.ClinicaId` pertence ao usuário associado. Não foi encontrado símbolo `PacienteClinicaId` no código de produção atual fora das migrations. Não se usa o ID do usuário para inferir a clínica do paciente.
- Chaves compostas de referências a pacientes/usuários são configuradas em `HemodinksAPI.Infrastructure/Data/Configurations/PatientUserTenantKeys.cs:10` e `:30`.
- Todas as entidades `IClinicaOwnedEntity` recebem filtro em `AppDbContext.cs:305`; gravações validam clínica e relacionamentos em `:166` e `:214`. `IgnoreQueryFilters` na validação de FK exige a clínica da entidade em `:262`.
- `PlatformDbContext` é separado do escopo da requisição. Usos globais de identidade/password/session são deliberados e correlacionados à identidade; não significam acesso global às entidades clínicas.

## Matriz auditável

Em todas as linhas, A é proprietária; B é a clínica autenticada atacante. Acesso permitido em A continua sujeito a perfil, vínculo médico/equipe e políticas existentes. `—` indica operação ausente nesse contrato, não operação auditada e aprovada. A matriz é por família de recursos; não constitui prova exaustiva de cada combinação de endpoint/perfil.

| Recurso | Leitura/listagem B→A | Criação em B com IDs/clinica de A | Edição B→A | Exclusão B→A | Exportação/download | Evidência e cenários automatizados |
|---|---|---|---|---|---|---|
| Usuários | GET por ID 404, listagem sem registros de A | Clínica vem do contexto; FK validada no save | Consulta filtrada + autorização por usuário | 404, usuário preservado | Arquivos privados com autorização | `UserEndpointExtensions.Crud.cs:110`; `ApiEndpointTenantAuditTests.cs:17`; `ApiEndpointClinicTests.cs:1125`; `PrivateFileDownloadQueryHandlerTests` |
| Pacientes | GET por ID 404, listagem filtrada também por médico/equipe/paciente | Referências clínicas filtradas, FK composta | Clínica e escopo médico existentes | 404 entre clínicas; médico sem vínculo recebe 404 mesmo dentro da clínica | Sem leitura de blob quando paciente fora do escopo | `GetPacienteByIdQueryHandler.cs:51`; `PacienteAccess.cs:21`; `DeletePacienteCommandHandler.cs:44`; novos testes HTTP e `PacienteDeletionSecurityTests` |
| Grupos médicos | Listagem filtrada por clínica | ID de médico de A indistinguível de ID inexistente (400); ClinicaId injetado não troca propriedade | Grupo de A e ID inexistente retornam mesmo 404 | Mesmo 404, grupo de A preservado | — | `GrupoMedicoRules.cs:27`; `UpdateGrupoMedicoCommandHandler.cs:26`; `ApiEndpointTenantAuditTests.cs:113` |
| Agenda/destinatários/notificações | Clínica + visibilidade por autor/destinatário | Header não troca tenant; IDs externos negados | Clínica e acesso ao evento | Clínica e acesso ao evento | Email/reminder correlacionados à clínica | `ApiEndpointAgendaSecurityTests.cs:14` e `:89`; `ApiEndpointAgendaNotificationSecurityTests.cs:13`; `EventRecipientScope.cs:12`; `EventEmailRules.cs:14` |
| Atendimento/faturamento/conta/glosa/recebimento | Filtro EF e policies; médico também sujeito ao vínculo no atendimento | Clínica no backend e validação de referências | Consulta filtrada e policy; médico alheio negado | Consulta filtrada, policy e restrições financeiras | Anexos privados, auditoria filtrada | `FinanceiroEndpointExtensions.cs:123`; `AtendimentoManagementUseCases.cs:14`; `FinanceiroEndpointIntegrationTests.cs:351`, `:566`, `:610` |
| Catálogos hospital/convênio/OPME | Listagem de B não contém marcador de A | Criação indireta usa clínica atual | — no catálogo público do endpoint | — | — | `ApiEndpointClinicTests.cs:1125`; `FinanceiroEndpointIntegrationTests.cs:272`; filtro central EF |
| Licenças | Licença/usuário carregados com filtro EF; outra clínica não encontrada | Trial criado só para médico da clínica resolvida | Policy Admin e usuário filtrado | — | — | `LicencaEndpointExtensions.cs:20`; `LicencaService.cs:117`; `LicencaServiceTests`; matriz de perfis |
| Configuração/identidade visual | Nome/foto públicos da clínica resolvida, contrato anônimo explícito | Registro criado com clínica resolvida | — | — | Foto pública de empresa | `ConfiguracaoSistemaEndpointExtensions.cs:17`; `ConfiguracaoSistemaRepository.cs:27`; `ApiEndpointDashboardAndConfigTests` |
| Monitoramento/logs | Admin recebe só própria clínica; SuperAdmin global explicitamente documentado | — | — | — | — | `MonitoringEndpointExtensions.cs:18`, `:50`; `MonitoringLogReaderTests.cs:9`; `SecurityObservationEndpointTests.cs:84` |
| Plataforma/clínicas/equipes | Admin só própria clínica; outra retorna 403 | SuperAdmin explicitamente autorizado na plataforma | Admin não altera outra clínica (403) | Admin não desativa clínica (403) | Auditoria de plataforma reservada | `ApiEndpointClinicTests.cs:1090`; `ClinicaPlatformRules.cs`; `ApiAuthorizationMatrixTests` |
| Descoberta de clínicas | Vínculo revogado excluído; identidade global inativa retorna 401 genérico | — | — | — | Nenhum ID/slug no erro; nenhuma sessão criada | `ResolveLoginClinicsCommandHandler.cs:94`; `LoginContextWorkTests.cs:32`; `ApiEndpointTenantAuditTests.cs:162` |
| Exportações PDF/XLSX | Worker atual não consulta registros clínicos | Job usa clínica autenticada mesmo com header/body/filtro de A | — | — | Novos containers privados, chave com `clinica-{id}` | `FileExportCommandHandlers.cs:65`; `FileExportFunction.cs:50`; `ApiEndpointTenantAuditTests.cs:89`; `FileExportPrivacyTests.cs:18` |
| CBHPM/perfis/diretório público | Dados de referência compartilhados; diretório só clínicas ativas e campos públicos | Policies existentes para importação | — | — | Exportação CBHPM segue contrato atual | `SqlPublicClinicDirectory.cs:21`; `ApiAuthorizationMatrixTests`; `AppDbContextTenancyTests` distingue entidades tenant de referências globais |

## Violações reproduzidas e correções

1. **IDOR destrutivo de paciente dentro da clínica.** Um médico sem vínculo recebia 404 na leitura, mas 204 na exclusão. Endpoint agora transmite ID autenticado ao comando e handler reutiliza `PacienteAccess` antes de qualquer deleção. Médico titular/auxiliar/grupo e administradores mantêm o escopo existente. Testes verificam preservação de paciente, usuário, foto/arquivo e zero chamadas de limpeza nos caminhos negados.
2. **Inferência de clínica por vínculo revogado.** Vínculo/global inativo era convertido em ausência de vínculo e aceitava senha local pelo fallback legado. Testes reproduziram retorno de `[101,102,103]` em vez de `[101,103]` e ausência do 401 para global desativado. Agora somente ausência verdadeira permite fallback. Compatibilidade legado e lock de vínculo ativo têm regressão automatizada.
3. **Container de exportação público.** SDK emitia `x-ms-blob-public-access: blob` para PDF e XLSX. Criação usa `PublicAccessType.None`. Teste exercita o worker e SDK reais contra servidor HTTP local, sem conexão Azure, e verifica ausência do header público e prefixo da clínica no upload.

Sem migrations, dependências de produção novas ou alterações de políticas por perfil. A referência ao projeto Workers no projeto de testes usa alias para evitar colisão entre classes `Program`.

## Pendências e limites reais

- **Containers já públicos:** `CreateIfNotExistsAsync(None)` não altera ACL de container existente. Verificar/restringir ACL em ambiente implantado e revisar exposição histórica de metadados/filtros. Nenhuma chamada à conta Azure foi realizada.
- **SuperAdmin e revogação local:** `SessionUseCases.cs:18`, `:54`, `:108` reativa/provisiona vínculos ao listar/selecionar clínicas. É política global explícita no código, mas a expectativa de revogação local precisa ser definida. Sem mudança automática dessa regra.
- **Exportação por recurso:** contrato atual e `ApiAuthorizationMatrixTests.cs:158` autorizam todos os perfis. Worker só exporta metadados/filtros. Antes de exportar dados clínicos reais, definir/revalidar autorização por recurso, estado da sessão e filtros; não confiar em `Filters[ClinicaId]` como autoridade.
- **Histórico financeiro por médico/equipe:** escopo atual é por clínica; confirmar se anexos de histórico devem ter granularidade médico/equipe. Sem violação comprovada de tenant e sem mudança de negócio.
- Testes de dados/API usam EF InMemory; o teste do worker usa HTTP local. `PacienteDeletionSqlServerTests` também executou o schema atual em SQL Server LocalDB descartável, verificando consulta por grupo, negativas entre clínicas e cascata de usuário/paciente/arquivos. Sem conta Azure ou frontend E2E (`HEMODINKS_E2E_FRONT_PATH` não configurado).
- Não se afirma ausência universal de bypass; os três achados comprovados foram tratados e os comportamentos pendentes foram registrados.

## Execuções

SDK local: .NET 10.0.401. Resultados originalmente registrados em `HemodinksAPI.Tests/TestResults/` e organizados em `out/issue-validation/` (artefatos locais, ignorados pelo Git).

| Execução | Resultado |
|---|---|
| Direcionada final: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter 'FullyQualifiedName~TenantAudit\|FullyQualifiedName~FileExportPrivacyTests\|FullyQualifiedName~LoginContextWorkTests\|FullyQualifiedName~DeletePaciente_' --logger 'trx;LogFileName=issue170-targeted.trx'` | 26 aprovados, 0 falhas, 0 ignorados; inclui 20 novos cenários |
| Suíte completa: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=issue170-full.trx'` | 862 aprovados, 2 falhas, 34 ignorados, 898 total; 10m06s |
| SQL da correção: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter FullyQualifiedName~DeletePaciente_SqlServer --environment HEMODINKS_TEST_LOCALDB=1 --logger 'trx;LogFileName=issue170-deletion-sqlserver.trx'` | 1 aprovado, 0 falhas, 0 ignorados; consulta SQL/grupo e cascata real |
| SQL legado: `HEMODINKS_TEST_LOCALDB=1`, filtro `PatientUserTenantSqlServerTests`, TRX `issue170-sqlserver.trx` | 1 falha no rollback parcial da fixture antiga: FK de `EventEmailDeliveries` impede remover `AK_Users_ClinicaId_Id` |
| Revisão de diff / `git diff --check` | Sem erro de whitespace; avisos de normalização CRLF existentes; nenhuma mudança em AGENTS/.codex/migrations |

As duas falhas da suíte completa são `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas` e `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`: ambas interrompidas nas migrations com **SQL Server Full-Text Search não instalado na instância**. Não foram executadas suas assertions finais. A falha do SQL legado é incompatibilidade da fixture, que aplica Down de migration antiga sobre schema posterior; não se alteraram migrations/FKs para esconder essa falha. Nenhuma dessas falhas está em código de produção modificado pela #170.

Reproduções pré-correção: 1 falha HTTP (DELETE 204), 2 falhas discovery e 2 falhas de ACL PDF/XLSX. Falha intermediária de tracking nos positivos foi corrigida com `AsTracking`, sem enfraquecer assertions. A tentativa inicial de compilar o novo SQL enquanto a suíte mantinha o apphost aberto falhou por lock; a execução sequencial acima passou.

Status: auditoria e três correções implementadas e verificadas nos testes direcionados/SQL; validação global com pendências explícitas, sem alegação de suíte totalmente verde. A revisão `security` e `database` foi concluída nos fixes; uma revisão suplementar final de documentação pelo agente security não ocorreu por limite de uso do runtime.

Após a #174, a regressão completa das duas issues na versão final registrou `out/issue-validation/issues170-174-complete.trx`: **890 aprovados, 2 falhas por falta de Full-Text Search, 37 ignorados, 929 total**. As mudanças de domínio/migration posteriores pertencem exclusivamente à #174 e estão detalhadas em `docs/issue-174-event-email-creator-copy.md`.
