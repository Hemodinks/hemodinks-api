# Recuperação de senha e revogação de sessões

## Causa e correção

O caminho `POST /api/users/password/reset/confirm` validava e consumia o token,
atualizava a senha local e sincronizava a senha global, mas não alterava
`UsuarioGlobal.SecurityVersion` nem revogava sessões. Por isso, um JWT ou refresh
emitido anteriormente continuava funcionando após a recuperação.

`ConfirmPasswordResetCommandHandler` agora rotaciona a versão com `Guid.NewGuid()`
e reutiliza a revogação antes existente em `TemporaryAccessService`. Essa rotina
foi movida para `PasswordCommandMutations.RevokeSessionsAndResetTokensAsync` e
recebe o contrato restrito `ICredentialRevocationDbContext`.

O mesmo `SaveChangesAsync` persiste a senha local, a senha global, a nova versão,
o consumo do token e a revogação das sessões/tokens de recuperação da identidade.
A transação automática do EF Core garante o rollback conjunto em banco relacional.
A preparação preexistente da identidade ocorre antes dessas mutações de credenciais.
Não foi introduzida transação manual, blacklist ou serviço paralelo de autenticação.

## Concorrência

- A configuração existente de `UsuarioGlobal.SecurityVersion` como concurrency
  token faz confirmações concorrentes disputarem a mesma identidade.
- `PasswordResetToken.UsedAt` também é concurrency token. Isso impede consumo
  duplicado mesmo quando o token foi lido antes de outra confirmação e a
  identidade global foi carregada depois dela.
- Conflitos na confirmação mantêm erro genérico de recuperação inválida e não
  deixam mudanças parciais no banco.
- Uma solicitação de reset que disputa um token com uma confirmação preserva a
  resposta genérica. O token novo não é enviado se sua transação perdeu a disputa.
  As alterações rastreadas dessa tentativa são descartadas/recarregadas para não
  vazarem para um `SaveChangesAsync` posterior da camada de idempotência.

## JWT, refresh e isolamento

O JWT mantém a claim existente `security_version`. Nenhuma claim ou campo público
foi acrescentado. `PasswordRecoveryMiddleware` rejeita a versão antiga, inclusive
em JWT sem `sid`. `AuthenticationSessionService` já compara a versão da sessão com
a global e verifica `RevokedAt`, tanto no acesso autenticado como na renovação.

Os refresh tokens permanecem armazenados como hash nas sessões revogadas. Não
podem gerar novos access tokens. Novos logins usam a versão atualizada.

A revogação é limitada ao `UsuarioGlobalId` obtido do vínculo do usuário do token.
Ela alcança as sessões dessa mesma identidade em todas as clínicas. Não altera
IDs, vínculos, perfis, permissões nem credenciais de outras identidades.
Os filtros e a resolução de tenant existentes não foram alterados.

Os logs novos contêm apenas o identificador do usuário e o resultado da operação.
Não registram senha, token de recuperação, JWT, refresh ou versão de segurança.

## Arquivos

Alterados:

- `HemodinksAPI.Application/Data/IModuleDbContexts.cs`
- `HemodinksAPI.Application/Data/ITemporaryAccessDbContext.cs`
- `HemodinksAPI.Application/Features/Users/Commands/ConfirmPasswordResetCommandHandler.cs`
- `HemodinksAPI.Application/Features/Users/Commands/PasswordCommandMutations.cs`
- `HemodinksAPI.Application/Features/Users/Commands/ResetUserPasswordByEmailCommandHandler.cs`
- `HemodinksAPI.Application/Features/Users/Commands/TemporaryAccessService.cs`
- `HemodinksAPI.Infrastructure/Data/Configurations/PasswordResetTokenConfiguration.cs`
- `HemodinksAPI.Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs`

Criados:

- `HemodinksAPI.Tests/PasswordResetSecurityEndpointTests.cs`
- `HemodinksAPI.Tests/PasswordResetSecurityEndpointTests.Tenancy.cs`
- `HemodinksAPI.Tests/PasswordResetSecurityPersistenceTests.cs`
- `HemodinksAPI.Tests/PasswordResetSecurityTestDatabase.cs`
- Este documento.

## Validação

Os testes novos exercitam o pipeline HTTP real com `HemodinksApiFactory` e usam
SQLite relacional com os mapeamentos de credenciais de produção para rollback e
concorrência. O envio de email é capturado pela infraestrutura de testes existente.

Cobertura: alteração de senha e versão; JWT anterior com e sem sessão; refresh
anterior; novo login e refresh; token inválido, expirado ou reutilizado; senha
inválida; falha em cada uma das quatro gravações; confirmações concorrentes;
solicitação concorrente; múltiplas clínicas e outras identidades; preservação dos
vínculos e perfis. Os testes HTTP incluem a sequência login → recuperação →
rejeição das credenciais antigas → novo login → acesso e renovação válidos.

Comando dos testes específicos:

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --filter "FullyQualifiedName~PasswordResetSecurity"
```

Resultado da execução completa em 2026-10-01, revisado em 2026-10-02:

- **552 aprovados, zero falhas e zero ignorados**, em 6 minutos e 46 segundos.
- Inclui os 15 casos novos de recuperação, os 11 cenários existentes de navegador
  e o teste de integridade dos vínculos entre clínicas no SQL Server LocalDB.
- Os testes novos reproduziram antes da correção a aceitação de JWT/refresh
  anteriores e o consumo concorrente do token; passaram após a correção.
- Uma primeira execução completa encontrou duas falhas por ausência de Full-Text
  Search no LocalDB (`LegacyFinancialBackfillMigrationTests` e
  `EventReminderProcessorConcurrencyTests`). A execução final usou a imagem
  `hemodinks-sqlserver:2022-fts-ci` em container descartável e ambos passaram.
- O container de teste foi removido ao final. Nenhum banco da aplicação foi usado.

A execução final usou `dotnet test HemodinksAPI.slnx --no-build --no-restore`, após
compilar a correção final, com `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` apontando
para o banco descartável, `HEMODINKS_TEST_LOCALDB=1` e `HEMODINKS_E2E_FRONT_PATH`
apontando para o checkout local do frontend. Essas são configurações de teste,
não de produção. O relatório local está em
`logs/password-reset/complete/password-reset-complete.trx` (diretório ignorado pelo Git).

## Implantação e limites

Não há nova coluna, tabela, migration de schema ou configuração de produção.
O snapshot apenas acompanha a configuração de concorrência de `UsedAt`;
`dotnet ef migrations has-pending-model-changes` confirmou ausência de alterações
pendentes.

A implantação deve atualizar todas as instâncias que atendem o reset. Requisições
já autorizadas antes do commit não são canceladas retroativamente. Em caso de
disputa com outra operação, a recuperação pode precisar ser solicitada novamente.

Login, seleção de clínica, autorização, pacientes, financeiro, frontend e criação
de usuários não foram modificados. O fluxo de senha temporária apenas passou a
chamar a rotina compartilhada; sua regra permanece a mesma. A troca comum de
senha autenticada permanece fora do escopo desta correção.

## Revisão da issue #152 em 2026-10-08

A revisão da branch `developer` encontrou a correção funcional já implementada
no histórico (`ed65363`). Foram preservados o handler, a revogação compartilhada,
os concurrency tokens e os fluxos de senha temporária e troca autenticada.
Nenhuma alteração de produção ou migration foi necessária nesta revisão.

Complementos de validação:

- `PasswordResetSecurityEndpointTests.cs`: execução sobre Kestrel em porta
  efêmera, com clientes sem cookies automáticos e endereço real do servidor.
- `PasswordResetSecurityEndpointTests.Teams.cs`: recuperação no login unificado
  nos modos Seleção, PIN e Nenhuma; rejeição de access/refresh anteriores,
  renovação de equipe e desafio pendente; novo login, versão e renovação válidos;
  preservação da identidade, vínculo, clínica, perfil, equipe e operador.

Os cenários HTTP usam banco InMemory isolado e captura de email; os testes de
persistência usam SQLite com transações e mapeamentos de produção. O E2E da
recuperação valida a API por HTTP real; não automatiza a entrega de email nem a
interface de recuperação no navegador. Os E2E existentes de navegador são
executados separadamente como regressão do login individual/unificado.

Validação específica desta revisão:

- Antes dos complementos: 16 aprovados, zero falhas/ignorados (recuperação,
  persistência e teste unitário existente de confirmação).
- Após os complementos: **18 aprovados, zero falhas/ignorados**, em 1min36s;
  inclui sete cenários E2E da API por HTTP real e onze casos relacionais.
- Relatórios locais ignorados pelo Git: `logs/issue-152/issue-152-targeted.trx`
  e `logs/issue-152/issue-152-security.trx`.
- Comando final específico: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --output logs/issue-152/diagnostic --filter "FullyQualifiedName~PasswordResetSecurity" --logger "trx;LogFileName=issue-152-security.trx" --results-directory logs/issue-152`.

A saída isolada evita disputar o executável com a suíte geral. No transporte
Kestrel, o teste controla os cookies com `SocketsHttpHandler.UseCookies=false`;
isso impede que um cookie automático interfira na credencial escolhida por cada
cenário. Os rate limits de produção permanecem ativos.

Reexecução isolada das falhas de concorrência sensível e seleção de equipe:
**6 aprovados, zero falhas/ignorados**, em 54s. Inclui o E2E de navegador
`LoginBrowserTests.Browser_UsesRealApi("selection")` e os cinco casos de
`SensitiveIdentitySqlServerTests.ConfirmationRace_CannotCommitAcrossConsumedProofOrChangedSession`.
Relatório: `logs/issue-152/issue-152-failure-review.trx`. Ambos haviam falhado
na execução completa; a aprovação isolada não apaga esse resultado nem comprova
CI verde.

Suíte geral executada com LocalDB descartável e frontend local:
`dotnet test HemodinksAPI.slnx --no-build --no-restore --logger "trx;LogFileName=issue-152-regression.trx" --results-directory logs/issue-152 --verbosity quiet`.
Configuração: `HEMODINKS_TEST_LOCALDB=1` e `HEMODINKS_E2E_FRONT_PATH` para o
checkout local. Resultado: **791 aprovados, 5 falhas, zero ignorados (796 casos)**,
em 17min03s. Relatório: `logs/issue-152/issue-152-regression.trx`.

Falhas dessa execução e revisão:

- `PasswordResetSecurityEndpointTests.TokenRecovery_RevokesSameIdentityAcrossClinics_WithoutChangingOtherUsersOrMemberships`: refresh recebeu 401 com o cliente inicial do Kestrel; passou após controle explícito de cookies, junto aos 18 casos finais de segurança.
- `LoginBrowserTests.Browser_UsesRealApi("selection")`: timeout de 60s ao carregar a página; passou isoladamente.
- `SensitiveIdentitySqlServerTests.ConfirmationRace_CannotCommitAcrossConsumedProofOrChangedSession("password")`: falha de transporte/conexão física do SQL Server; passou isoladamente, junto aos outros quatro casos.
- `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`: Full-Text Search ausente no LocalDB.
- `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`: Full-Text Search ausente no LocalDB.

Na execução geral, 11 dos 12 E2E de navegador passaram; o caso restante passou na
reexecução isolada. Não houve alteração no frontend. A suíte geral não foi
repetida após o ajuste final do cliente de testes; não se declara CI verde com
base em reexecuções parciais.

Revalidação dos dois testes dependentes de Full-Text Search: **2 aprovados,
zero falhas/ignorados**, em 23s, usando a imagem local
`hemodinks-sqlserver:2022-fts-ci` em container exclusivo, porta efêmera e bancos
descartáveis. Comando: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --output logs/issue-152/diagnostic --filter "FullyQualifiedName~EventReminderProcessorConcurrencyTests|FullyQualifiedName~LegacyFinancialBackfillMigrationTests" --logger "trx;LogFileName=issue-152-fts.trx" --results-directory logs/issue-152`.
A conexão de teste foi configurada apenas no processo e o container foi removido
no `finally`. Nenhum banco da aplicação foi usado. Relatório:
`logs/issue-152/issue-152-fts.trx`.

As cinco falhas da execução geral possuem revalidações aprovadas. Permanecem
como limitações a ausência de uma nova execução completa com o ajuste final e a
necessidade de SQL Server com FTS para reproduzir integralmente o ambiente do CI.
Não houve merge, push, deploy nem atualização remota da issue.
