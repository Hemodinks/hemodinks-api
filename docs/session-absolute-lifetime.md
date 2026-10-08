# Duração absoluta das sessões

O backend exige nova autenticação após **12 horas**, mesmo com atividade e renovação contínuas. A decisão temporal fica em `Application/Features/Sessions/SessionLifetimePolicy.cs`, usa `TimeProvider` em UTC e considera a sessão expirada quando `agora >= início + duração`.

## Configuração

`AuthenticationSession:AbsoluteLifetimeHours` (variável de ambiente `AuthenticationSession__AbsoluteLifetimeHours`) tem padrão `12` e aceita inteiros entre `1` e `8760`. Valores inválidos impedem a inicialização. A mesma validação exige nome de cookie preenchido, inatividade entre 1 e 525600 minutos e validade normal do cookie entre 1 e 365 dias.

- A validade do JWT continua sendo configurada separadamente em `JwtSettings`.
- Nas sessões persistidas, a inatividade continua usando `LastActivityAt`; prevalece o primeiro prazo atingido entre inatividade e duração absoluta.
- O refresh cookie expira no menor prazo entre sua validade normal e o limite absoluto. Rotacionar o refresh token não amplia esse limite.
- A configuração é carregada na inicialização. Reduzir a duração pode invalidar sessões existentes na próxima requisição após reiniciar a aplicação.

## Origem confiável e compatibilidade

Sessões com `sid` usam exclusivamente o `AuthenticationSession.CreatedAt` persistido. Renovação, atividade, atualização de perfil e troca de clínica não alteram esse campo. Um JWT antigo com `sid`, mesmo sem `auth_time`, continua funcionando apenas enquanto a sessão correspondente estiver válida; o middleware recupera sua origem do banco.

Tokens sem sessão persistida, incluindo equipes, recebem o claim assinado `auth_time` com a autenticação original. Renovação, troca de contexto e troca de PIN preservam esse valor. Na identificação de operador, a origem é `EquipeLoginDesafio.DataCadastro`, definido pelo servidor na etapa de senha. Os modos PIN, seleção e sem identificação preservam suas regras de autorização e versões de segurança. O timestamp não é aceito de um corpo de requisição, e o `iat` de um JWT renovado não inicia outra janela.

Tokens antigos sem `sid` e sem `auth_time` confiável exigem novo login. Valores ausentes, malformados ou futuros também são rejeitados. Não existe concessão automática de uma nova janela. O claim tem precisão de segundos, podendo encerrar um fluxo sem sessão persistida uma fração de segundo antes do prazo de precisão integral. A implementação não adiciona armazenamento de atividade às equipes: mantém seus mecanismos existentes de inatividade e impõe o novo limite absoluto no servidor.

Somente uma nova autenticação completa inicia nova janela. Não há alteração de schema nem migração. A implantação exige avisar sobre o novo login dos usuários com tokens legados sem origem confiável.

## Respostas, concorrência e frontend

Requisições protegidas e refresh de sessão expirada retornam HTTP 401. Quando o primeiro limite atingido é o absoluto, o contrato é:

```json
{"code":"session_absolute_expired","message":"Sua sessão atingiu o tempo máximo. Entre novamente."}
```

A validação protegida também pode informar `session_idle_expired` ou `session_reauthentication_required`; respostas genéricas existentes continuam possíveis, por exemplo quando o navegador já não envia o cookie expirado. Falhas de infraestrutura não são convertidas em expiração. Login, recuperação e logout continuam acessíveis conforme suas regras existentes.

O frontend em `hemodinks-front` usa `sessionExpiration.ts`, `api.ts`, `sessionService.ts` e `useSessionLifecycle.ts` para reconhecer o código, interromper novas tentativas de renovação e chamar a limpeza existente em `AppContent.tsx`, incluindo cache privado. A mensagem absoluta é propagada por `BroadcastChannel` apenas às abas da mesma janela de autenticação, inclusive após trocar de clínica. O relógio do navegador não determina a autorização absoluta. Respostas referentes a tokens substituídos são ignoradas pela limpeza de sessão.

O controle otimista existente por `rowversion` protege a rotação e a atividade. Após conflito, o store descarta alterações pendentes e a validação recarrega o estado; revogação concorrente não é tratada como simples atividade. Refresh revalida o prazo após a gravação e revoga caso o limite tenha sido alcançado durante a operação. Respostas de expiração não apagam cookies. Logout vincula a exclusão ao cookie da sessão solicitada, para que um bearer antigo não apague o cookie de outro login.

As associações ativas `UsuarioGlobal`/`UsuarioClinica`, filtros de clínica, policies, licenças, versões de equipe/operador, `SecurityVersion`, CSRF e CORS permanecem aplicáveis.

## Cobertura de testes

- `SessionLifetimePolicyTests`: fronteiras temporais, primeiro prazo, configuração e cookie.
- `SessionAbsoluteLifetimeEndpointTests`: JWT válido antes/no/depois do prazo, refresh, troca de clínica, nova autenticação, logout antigo e tokens legados com/sem sessão persistida.
- `TeamSessionAbsoluteLifetimeTests`: modos de equipe, identificação do operador, PIN e renovação com origem preservada.
- `SessionLifetimeConfigurationTests`: rejeição da configuração inválida no registro da autenticação.
- `SessionLifetimeConcurrencyTests`: contextos independentes e `rowversion` real no SQL Server; disputa com logout/atividade e prazo atingido durante gravação. Usa banco descartável próprio; configure `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` ou `HEMODINKS_TEST_LOCALDB=1` para executá-lo.
- Frontend: resposta absoluta sem retry, limpeza única, erro de infraestrutura sem expiração falsa e comunicação entre abas sem afetar nova sessão.

Os testes temporais avançam relógios controlados, sem sleeps para atravessar os prazos.

## Arquivos alterados

| Área | Arquivos |
| --- | --- |
| API | `ApiServiceCollectionExtensions.Auth.cs`, `AuthenticationSessionMiddleware.cs`, `SessionEndpointExtensions.cs`, `appsettings.json` |
| Application — autenticação | `Authentication/AuthenticationSessionClaimTypes.cs`, `Authentication/IJwtTokenService.cs`, `CurrentUserContext.cs`, `Features/Users/Commands/AuthenticateUserCommandHandler.cs` |
| Application — sessões | `Features/Sessions/SessionLifetimePolicy.cs` (novo), `AuthenticationSessionService.cs`, `IAuthenticationSessionStore.cs`, `SessionUseCases.cs` |
| Application — equipes | `Features/Teams/TeamUseCases.cs`, `TeamAuthenticationUseCases.cs`, `TeamSessionRenewal.cs` |
| Infrastructure | `Authentication/JwtTokenService.cs`, `Authorization/CurrentUserClaimsExtensions.cs`, `Data/EfAuthenticationSessionStore.cs` |
| Testes backend | Os cinco arquivos novos de testes acima; ajustes em `SessionRenewalEndpointTests.cs` e `UserCommandHandlerTestDoubles.cs` |
| Frontend | `src/services/sessionExpiration.ts` (novo), `src/services/api.ts`, `src/services/sessionService.ts`, `src/features/auth/useSessionLifecycle.ts`, `src/app/AppContent.tsx`, `src/services/sessionService.test.ts`, `src/features/auth/useSessionLifecycle.test.tsx` |
| Documentação | Este arquivo |

## Validação executada em 02/10/2026

- `dotnet build HemodinksAPI.slnx --no-restore`: sucesso, zero erros, 16 avisos IDE0005 de diretivas desnecessárias já existentes.
- Suíte completa com SQL Server Full-Text descartável, teste de isolamento LocalDB e 11 cenários de navegador: 636 aprovados e três falhas de preparação dos novos testes relacionais (a preparação inicial tentava migrar pelo contexto de plataforma sem criar as tabelas).
- Após corrigir a preparação para criar o schema pelo modelo, execução dirigida de concorrência e sessões persistidas legadas: seis aprovados, zero falhas. São os três casos corrigidos e três casos adicionais, totalizando **642 casos distintos aprovados** entre as execuções. Nenhum teste foi ignorado nessas execuções do backend.
- `dotnet ef migrations has-pending-model-changes --context AppDbContext --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --no-build`: nenhuma alteração de modelo pendente.
- `git diff --check`: sucesso nos dois repositórios.
- Frontend: auditoria de arquitetura aprovada (286 arquivos), `npm test -- --maxWorkers=1` com **416 testes aprovados em 71 arquivos**, sem falhas, e `npm run build` concluído. A execução inicial concorrente apresentou timeout em um teste existente de cadastro de pacientes; ele passou isoladamente e na suíte final com um worker, mantendo o limite original de 15 segundos. O build mantém o aviso de importação estática/dinâmica de `observability.ts`, e a auditoria mantém o aviso de tamanho de `types.ts`; ambos fora das alterações de sessão.

Os bancos e containers dessa validação são descartáveis e pertencem aos testes. Não houve deploy nem alteração de dados de produção.
