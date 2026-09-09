# Gerenciamento de acesso por senha temporária

## 1. Diagnóstico inicial

O backend utiliza .NET 10, separação Domain/Application/Infrastructure/API, MediatR, interfaces de contexto por funcionalidade, EF Core e SQL Server. A validação passa pelo `ValidationBehavior` e por `IRequestValidator`; FluentValidation já era uma dependência. Endpoints mínimos usam `EndpointExecution` para traduzir exceções. A implementação integra o novo validator FluentValidation a esse pipeline.

O login resolve a clínica e o usuário local, associa `UsuarioClinica` a `UsuarioGlobal`, verifica a credencial global e aplica proteção contra tentativas repetidas. O hash existente é PBKDF2-SHA256, com salt aleatório, 210.000 iterações e comparação em tempo constante. A senha é compartilhada pela identidade global entre seus vínculos clínicos.

JWTs já carregavam `precisaTrocarSenha`. Há sessões persistidas com cookie de refresh, revogação, controle de inatividade e concorrência. O serviço contém renovação de sessão, embora os endpoints atuais de sessão exponham apenas listagem e seleção de clínicas. O frontend encerra a sessão ao expirar o JWT. O fluxo de equipes possui identificação de operador/PIN, preservada nesta alteração.

`AppDbContext` aplica filtros e validações de tenant; `PlatformDbContext` atende operações explicitamente globais. A recuperação precisa deste último para revogar sessões de todos os vínculos da mesma identidade. A autorização continua correlacionando solicitante, clínica e alvo antes de qualquer geração. Não há seleção de tenant pelo payload da operação.

O frontend é React/TypeScript/Vite, com services HTTP, hooks por funcionalidade, React Query, componentes compartilhados de modal, formulários, alertas e senha. Já existiam `PasswordRequiredScreen` e a barreira de credenciais no aplicativo. A senha gerada permanece exclusivamente no componente do dialog, fora de React Query e do armazenamento da sessão.

O reset administrativo anterior substituía a senha definitiva por uma temporária, sem prazo nem consumo único. Essa operação foi substituída mantendo sua rota. O primeiro acesso legado e a recuperação por e-mail continuam com seus contratos existentes.

## 2. Arquivos criados

| Arquivo | Finalidade |
| --- | --- |
| `HemodinksAPI.Domain/Models/TemporaryAccessCredential.cs` | Hash e ciclo de vida da credencial temporária. |
| `HemodinksAPI.Application/Data/ITemporaryAccessDbContext.cs` | Contrato de persistência da recuperação. |
| `HemodinksAPI.Application/Features/Users/Commands/TemporaryAccessService.cs` | Autorização, geração, consumo, conclusão, revogação e auditoria. |
| `HemodinksAPI.Application/Features/Users/Commands/ChangeTemporaryPasswordCommand.cs` | Command, handler e validator FluentValidation para definição da senha. |
| `HemodinksAPI.Infrastructure/Data/Configurations/TemporaryAccessCredentialConfiguration.cs` | Mapeamento e concorrência da credencial. |
| `HemodinksAPI.Api/PasswordRecoveryMiddleware.cs` | Verificação da versão de segurança e bloqueio das APIs durante recuperação. |
| `HemodinksAPI.Infrastructure/Data/Migrations/20260908224929_AddTemporaryAccessCredentials.cs` | Migration incremental. |
| `HemodinksAPI.Infrastructure/Data/Migrations/20260908224929_AddTemporaryAccessCredentials.Designer.cs` | Modelo da migration gerado pelo EF. |
| `HemodinksAPI.Tests/TemporaryAccessTests.cs` | Autorização, expiração, ciclo de vida, política de senha e isolamento. |
| `HemodinksAPI.Tests/TemporaryAccessConcurrencyTests.cs` | Concorrência e rollback com SQLite e mapeamentos de produção. |
| `HemodinksAPI.Tests/TemporaryAccessEndpointTests.cs` | Fluxo HTTP, tokens, rate limiting e preservação da identificação de equipes. |
| `hemodinks-front/src/features/users/TemporaryPasswordAction.tsx` | Ação com ícone de chave junto à exclusão na lista de usuários. |
| `hemodinks-front/src/features/users/TemporaryPasswordDialog.tsx` | Dialog de confirmação e resultado, com exibição única, cópia e expiração. |
| `hemodinks-front/src/features/users/temporary-password.css` | Layout responsivo dos dialogs, temas claro/escuro e painel opaco. |
| `hemodinks-front/src/features/users/TemporaryPasswordAction.test.tsx` | Testes de autorização visual e ciclo do dialog. |
| `hemodinks-front/src/features/auth/TemporaryPasswordForm.test.tsx` | Testes da barreira de credenciais e conclusão da troca. |
| `docs/temporary-access-implementation.md` | Este relatório. |

## 3. Arquivos alterados

Os caminhos abaixo são relativos ao respectivo repositório.

| Arquivos | Justificativa |
| --- | --- |
| `HemodinksAPI.Api/ApiServiceCollectionExtensions.Application.cs`, `ApiServiceCollectionExtensions.Database.cs` | Registro do serviço e contexto global de recuperação. |
| `HemodinksAPI.Api/ApiServiceCollectionExtensions.Platform.cs`, `Program.cs` | Política de rate limiting por identidade autenticada e middleware de recuperação. Autenticação precede o limitador. |
| `HemodinksAPI.Api/UserEndpointExtensions.cs`, `UserEndpointExtensions.Password.cs`, `UserEndpointExtensions.Crud.cs` | Integração das operações, contexto confiável do solicitante, resposta sem cache e preservação do desafio de identificação antes da sessão. |
| `HemodinksAPI.Api/ClinicaModuleAccessMiddleware.cs`, `ClinicaResolutionMiddleware.cs` | Permitir a definição da senha independentemente do módulo contratado e após identificação válida com PIN ainda pendente de troca. |
| `HemodinksAPI.Application/ApplicationServiceCollectionExtensions.cs` | Registro do novo validator no pipeline existente. |
| `HemodinksAPI.Application/Authentication/GlobalIdentityService.cs` | Impedir login com senha pessoal durante a recuperação administrativa. |
| `HemodinksAPI.Application/Features/Sessions/AuthenticationSessionService.cs` | Vincular sessão à versão da credencial e preservar restrições na renovação. |
| `HemodinksAPI.Application/Features/Teams/TeamAuthenticationUseCases.cs` | Vincular o desafio à versão de segurança e propagar troca obrigatória. |
| `HemodinksAPI.Application/Features/Users/Commands/AuthenticateUserCommandHandler.cs` | Integrar consumo temporário ao login e tratar conflitos com resposta genérica. |
| `HemodinksAPI.Application/Features/Users/Commands/ChangePasswordCommandHandler.cs`, `ConfirmPasswordResetCommandHandler.cs` | Impedir rotas alternativas de alteração durante recuperação temporária. |
| `HemodinksAPI.Application/Features/Users/Commands/ResetUserPasswordCommandHandler.cs`, `UserCommands.cs` | Delegar geração segura e ajustar contratos. |
| `HemodinksAPI.Domain/Models/UsuarioGlobal.cs`, `AuthenticationSession.cs`, `Equipe.cs` | Versão de segurança, estado global da recuperação e vínculo de sessões/desafios. |
| `HemodinksAPI.Domain/Utils/TemporaryPasswordGenerator.cs` | Caracteres obrigatórios aleatórios e embaralhamento criptográfico. |
| `HemodinksAPI.Infrastructure/Authentication/JwtTokenService.cs` | Claims de recuperação e versão de segurança. |
| `HemodinksAPI.Infrastructure/Data/AppDbContext.cs`, `Configurations/UsuarioGlobalConfiguration.cs`, `Migrations/AppDbContextModelSnapshot.cs` | Persistência e concorrência otimista. |
| `HemodinksAPI.Tests/UserCommandHandlerPasswordChangeTests.cs` | Atualizar expectativas do reset administrativo: preservação do hash pessoal e armazenamento separado. |
| `hemodinks-front/src/features/users/UsersPage.tsx`, `UserList.tsx` | Exibir a ação ao lado de Excluir na lista, respeitando o perfil do solicitante. |
| `hemodinks-front/src/services/usersService.ts` | Contratos tipados e chamadas das operações. |
| `hemodinks-front/src/shared/components/PasswordForm.tsx`, `features/auth/PasswordRequiredScreen.tsx` | Formulário de nova senha sem exigir reutilização da temporária já consumida. |
| `hemodinks-front/src/features/auth/useAuthSession.ts`, `useLoginFlow.ts` | Restaurar a restrição a partir do JWT e limpar a senha do formulário após autenticar. |
| `hemodinks-front/src/otel.ts` | Não registrar interações com componentes marcados como privados. |
| `hemodinks-front/src/App.test.tsx` | Atualizar o título esperado da tela de senha obrigatória. |

## 4. Banco de dados

- `TemporaryAccessCredentials`: chave primária `UsuarioGlobalId`, FK para `UsuariosGlobais`, identificador da emissão, usuário, clínica, hash, criação, expiração, consumo, revogação e solicitante.
- Uma única linha por identidade global impede credenciais simultâneas em vínculos diferentes. Uma reemissão substitui os metadados da emissão anterior; o histórico fica na auditoria.
- Índice composto em `ClinicaId, UserId`; hash limitado a 500 caracteres.
- `UsuariosGlobais`: `SecurityVersion` e `TemporaryPasswordRecovery`.
- `AuthenticationSessions` e `EquipeLoginDesafios`: `SecurityVersion`.
- Valores iniciais preservam usuários e sessões existentes. A versão global, o identificador da emissão e as datas de uso/revogação participam do controle de concorrência.

## 5. Backend e contratos HTTP

- `PUT /api/users/{id}/password/reset`: exige administrador, valida clínica e hierarquia, gera e retorna a senha uma única vez com `expiresAtUtc` e `Cache-Control: no-store, private`.
- `POST /api/users/authenticate`: mantém o login existente, adicionando validação e consumo da credencial temporária quando a identidade está em recuperação.
- `POST /api/users/password/temporary/complete`: aceita `novaSenha` e `confirmacao`. Identidade, clínica e versão de segurança vêm exclusivamente da autenticação do servidor.
- Não foi criado endpoint de consulta/recuperação da senha temporária.

## 6. Frontend e fluxo

Na lista de usuários, o administrador aciona o ícone de chave ao lado de Excluir e confirma a geração mediante solicitação do titular. O dialog informa validade e uso único, mostra a senha, permite copiar e informa a hora de expiração. Ao fechar ou expirar, o valor é removido do estado; uma nova abertura começa sem a credencial anterior.

O login temporário encaminha o usuário à tela **Definir nova senha**. Há somente nova senha, confirmação e saída. A conclusão revoga a sessão restrita e retorna ao login: o usuário entra com a senha pessoal e recebe um novo JWT normal. Não se remove uma flag local para reutilizar o token restrito.

## 7. Segurança

- **Hash e geração:** reutilização do PBKDF2 existente; 20 caracteres com maiúsculas, minúsculas, números e símbolos gerados e embaralhados com `RandomNumberGenerator`.
- **Prazo:** `TimeProvider` no backend, UTC, cinco minutos exatos; `agora >= expiração` recusa a credencial. O relógio visual não autoriza login.
- **Uso único e concorrência:** alterações de consumo e versão global são gravadas junto com a auditoria em `SaveChanges`. O EF usa transação no provedor relacional; versões concorrentes não podem confirmar dois consumos. O teste SQLite força duas leituras anteriores ao primeiro commit e verifica rejeição/rollback do segundo consumidor e de uma geração concorrente.
- **Autorização:** perfil persistido do solicitante, clínica e alvo são validados. Administrador comum não alcança Super Administrador, inclusive em outra clínica ou antes da migração do vínculo global. As consultas globais são limitadas à identidade envolvida e não retornam dados de outras clínicas.
- **APIs restritas:** `temporary_password`, `precisaTrocarSenha` e `security_version` são emitidos pelo servidor. O middleware compara a versão atual e recusa APIs de negócio e seleção de clínica até a conclusão. O tratamento legado de primeiro acesso permanece separado deste novo ciclo.
- **Sessões:** geração e conclusão rotacionam a versão e revogam sessões e tokens de recuperação por e-mail da identidade. Senha pessoal antiga não autentica durante a recuperação. Renovação não converte sessão restrita em sessão normal. Após concluir, todos os vínculos da identidade deixam de exigir troca de senha.
- **Identificação adicional:** equipes continuam exigindo seu desafio de operador/PIN; a API não emite sessão normal antes da identificação. Desafios antigos ficam inválidos quando a versão da credencial muda.
- **Abuso:** cinco operações de acesso temporário a cada cinco minutos por identidade autenticada; fallback por IP para chamadas anônimas. Reemissão para a mesma identidade também exige intervalo persistido de 30 segundos. Login mantém proteção por conta/IP e a recuperação por e-mail mantém seu limitador existente.
- **Auditoria:** geração, uso, reemissão/revogação, expiração reconhecida, conclusão e negação por hierarquia registram operação, resultado, solicitante/identidade, alvo, clínica e UTC. Não há senha ou hash no conteúdo da auditoria.
- **Telemetria:** nenhuma credencial é enviada a logs, filas ou eventos. Respostas de geração não passam por armazenamento de idempotência. O dialog e o formulário são privados para gravação de tela/interações; OpenTelemetry ignora interações nesses elementos.

## 8. Testes

- Backend: **344 testes passaram**, com o filtro `Category!=SqlServer`, incluindo **22 testes específicos de acesso temporário** e a matriz existente de autorização.
- Frontend: **254 testes passaram em 44 arquivos**, incluindo os novos testes do dialog e do formulário obrigatório. Permanecem avisos `act(...)` em testes antigos de configuração/privacidade, sem falhas.
- O fluxo HTTP verifica geração, expiração, revogação, sessão restrita, bloqueio de APIs, conclusão, login com a nova senha, não reutilização, rate limiting e preservação do desafio de equipe. Os testes de serviço complementam os casos de isolamento, hierarquia e reemissão.

A suíte completa foi tentada. Os testes preexistentes `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas` e `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals` falharam porque a instância local não tem SQL Server Full-Text Search instalado. Isso impede validar esses dois cenários SQL Server neste ambiente. A concorrência da nova credencial foi validada também em SQLite com os mapeamentos de produção.

Comandos reproduzíveis:

```powershell
dotnet test HemodinksAPI.Tests --filter "Category!=SqlServer"
dotnet test HemodinksAPI.Tests --filter "FullyQualifiedName~TemporaryAccess"
cd ../hemodinks-front
npm test
npm run audit:architecture
```

Os testes combinam integração HTTP real da API e testes React. Não foi executado um navegador conectado simultaneamente aos dois servidores.

## 9. Builds

`dotnet build HemodinksAPI.Api --no-restore` e `npm run build` passaram. O frontend mantém um aviso de importação estática/dinâmica de `observability.ts`, anterior a esta funcionalidade. A auditoria de arquitetura do frontend passou. `dotnet ef migrations has-pending-model-changes` confirmou que o modelo e a migration estão sincronizados.

## 10. Aplicação da migration

Migration: **`20260908224929_AddTemporaryAccessCredentials`**.

Na raiz de `hemodinks-api`, usando a conexão configurada para o ambiente desejado:

```powershell
dotnet ef database update 20260908224929_AddTemporaryAccessCredentials --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --context AppDbContext
```

Para gerar SQL revisável em vez de aplicar diretamente:

```powershell
dotnet ef migrations script --idempotent --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --context AppDbContext --output temporary-access.sql
```

A migration não foi aplicada ao banco da aplicação ou de produção. Os testes SQL Server utilizam bancos temporários próprios. Na implantação, aplicar o schema e atualizar todas as instâncias da API: versões antigas não conhecem a nova versão de segurança das credenciais.
