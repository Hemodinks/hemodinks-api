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
