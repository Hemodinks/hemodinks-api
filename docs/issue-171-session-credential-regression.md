# Issue #171 — regressão de sessão, senha e SecurityVersion

Auditoria na `developer`, em 2026-10-13. Complexidade alta, risco crítico:
autenticação, credenciais globais, cookies, concorrência e troca de clínica.
Escopo: evidências e testes. Nenhuma alteração de produção, contrato ou migration.

## Dependência #152

A [#152](https://github.com/Hemodinks/hemodinks-api/issues/152) foi consultada no
GitHub e está fechada como concluída. Sua correção permanece em
`ConfirmPasswordResetCommandHandler.cs:58-65`: rotação de `SecurityVersion`,
revogação compartilhada, consumo do token e persistência conjunta.
Não foi reimplementada. Evidências anteriores e limites estão em
[password-reset-session-revocation.md](password-reset-session-revocation.md).
A #170, ordem anterior desta auditoria, já foi implementada e documentada.

## Evidências por fluxo

Caminhos de produção abaixo são relativos à raiz do repositório.

| Fluxo | Evidência | Testes pertinentes |
| --- | --- | --- |
| Recuperação por token | `HemodinksAPI.Application/Features/Users/Commands/ConfirmPasswordResetCommandHandler.cs:58`; `PasswordCommandMutations.cs:59` | `PasswordResetSecurityEndpointTests` (HTTP real, JWT com/sem sessão, refresh, replay, equipes e múltiplas clínicas); `PasswordResetSecurityPersistenceTests` (rollback e concorrência). Cobertura da #152 apenas reexecutada. |
| Troca autenticada de senha | `HemodinksAPI.Application/Features/Users/Commands/SensitiveIdentityService.cs:64`, `:150` | Novo `PasswordChange_RevokesBothClinicSessions_WithoutRevokingOtherIdentity`: access, refresh e seleção negados para duas sessões antigas; outra identidade permanece ativa; senha antiga rejeitada e novos logins nas duas clínicas válidos. |
| Senha temporária | `HemodinksAPI.Application/Features/Users/Commands/TemporaryAccessService.cs:74`, `:108`, `:126`, `:138` | `TemporaryAccessTests`, `TemporaryAccessEndpointTests`, `TemporaryAccessConcurrencyTests`: geração autorizada, hierarquia global, expiração, consumo único, versão, restrições e clínica cruzada. |
| Refresh e cookies | `HemodinksAPI.Api/AuthenticationSessionCookie.cs:43`; `HemodinksAPI.Api/SessionEndpointExtensions.cs:63`; `HemodinksAPI.Application/Features/Sessions/AuthenticationSessionService.cs:127` | `SessionCookieAttributesTests` e `SessionRenewalEndpointTests`: HttpOnly, Secure por ambiente, SameSite, Path, origem/header, rotação/replay, revogação, versão e conflito sem remoção indevida do cookie. |
| Troca de clínica | `HemodinksAPI.Application/Features/Sessions/SessionUseCases.cs:56`; `AuthenticationSessionService.cs:253`; `HemodinksAPI.Api/AuthenticationSessionMiddleware.cs:60` | Novos `ClinicSwitch_*`: vínculo ativo da própria identidade, JWT/contexto refresh anterior negados, clínica obtida do contexto apesar do header, sessão atual preservada; seis negativos para outra identidade, clínica inexistente, vínculo/usuário/clínica inativos e perfil equipe. |
| Operações concorrentes | `HemodinksAPI.Infrastructure/Data/EfAuthenticationSessionStore.cs:81`; `AuthenticationSessionService.cs:175` | Novo caso `credential-rotation` em `SessionLifetimeConcurrencyTests`: geração real de senha temporária disputa refresh carregado antes; rowversion rejeita a gravação antiga, revogação/versão persistem e outro SaveChanges não ressuscita sessão. Casos existentes de logout/prazo/atividade, `SessionActivitySqlServerTests` e `SensitiveIdentitySqlServerTests` também reexecutados. |
| Prova sensível e troca de contexto | `HemodinksAPI.Application/Features/Users/Commands/SensitiveIdentityService.cs:137` | `SensitiveIdentity_ClinicRoundTripInvalidatesPendingEmailProof` e concorrência SQL: prova vinculada a sessão/clínica/ContextVersion, sem reutilização após ida e volta. |

Novos testes estão em `HemodinksAPI.Tests/SessionCredentialRegressionTests.cs` e
no cenário adicional de `HemodinksAPI.Tests/SessionLifetimeConcurrencyTests.cs`.
O SQL Server usa bancos descartáveis exclusivos e `EnsureDeleted` no `finally`;
nenhum banco da aplicação ou serviço de email externo é utilizado.

## Execução e resultados

- Novos cenários e classe de concorrência relacionada: **13 aprovados, zero
  falhas/ignorados**, em 31s. Inclui oito casos HTTP novos, um SQL novo e quatro
  casos SQL existentes. Relatório: `out/issue-validation/issue171-new.trx`.
- A primeira execução dos casos de clínica falhou no preparo de seis logins:
  ao usar perfil Administrador em duas clínicas, era necessário selecionar
  explicitamente a clínica no login. A fixture foi corrigida para enviar o
  seletor existente; as expectativas de autorização não foram afrouxadas.
- Regressão ampliada: **258 aprovados, zero falhas, 12 ignorados**, em 3min03s.
  Os 12 ignorados são exclusivamente `LoginBrowserTests`, executados separadamente
  com o checkout do frontend configurado. Relatório:
  `out/issue-validation/issue171-security-regression.trx`.
- Navegador: **12 aprovados, zero falhas/ignorados**, em 3min38s, usando o
  checkout local `hemodinks-front`, Playwright e API real em Kestrel. Inclui
  login individual, seleção/PIN/anonimato, PIN inválido, escrita anônima negada,
  clínica cruzada, troca de operador, replay, cancelamento, layout e bootstrap.
  Relatório: `out/issue-validation/issue171-browser.trx`.
- As duas execuções cobrem **270 cenários distintos aprovados**. Os 13 casos da
  rodada inicial estão contidos na regressão ampliada; não são somados novamente.
  São nove casos novos: oito HTTP e um de concorrência nativa em SQL Server.
- `git diff --check` aprovado e revisão dos três arquivos sem secrets novos.
  O checkout do frontend permaneceu sem alterações de fonte. A suíte inteira
  de negócio não foi repetida; a regressão ampliada foi restrita aos fluxos
  relacionados à issue.

Comando da regressão ampliada (LocalDB instalado, bancos descartáveis):

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Session|FullyQualifiedName~PasswordResetSecurity|FullyQualifiedName~TemporaryAccess|FullyQualifiedName~SensitiveIdentity|FullyQualifiedName~TeamLogin|FullyQualifiedName~LoginContext|FullyQualifiedName~PasswordHashUpgrade|FullyQualifiedName~PasswordChange|FullyQualifiedName~SessionCookie|FullyQualifiedName~LoginBrowser" --environment HEMODINKS_TEST_LOCALDB=1 --logger "trx;LogFileName=issue171-security-regression.trx" --results-directory out/issue-validation --verbosity quiet
```

A execução separada usa o mesmo projeto com `--filter FullyQualifiedName~LoginBrowserTests`,
`--environment "HEMODINKS_E2E_FRONT_PATH=<caminho absoluto do checkout hemodinks-front>"`
e `--logger "trx;LogFileName=issue171-browser.trx"`. O checkout precisa conter
`playwright.login.config.ts` e as dependências Playwright já instaladas.

## Limites e pendências

- `SessionUseCases.cs:108` reativa vínculos e usuários locais de SuperAdmin.
  É comportamento preexistente, já registrado na #170; uma política de revogação
  local de SuperAdmin precisa ser definida antes de alterar essa regra.
  Os novos negativos usam Administrador para não confundir esse provisionamento
  com autorização de identidades comuns.
- Troca de clínica não é rotação de credencial global: o JWT anterior é rejeitado
  enquanto seu vínculo difere do vínculo atual da sessão. A prova de alteração de
  email usa `ContextVersion` para invalidar também a ida e volta.
- Requisições autorizadas antes de uma revogação persistir não são canceladas
  retroativamente. Os testes verificam validações posteriores e conflitos reais.
- Security (`gpt-6.1-sol`, `high`) e QA (`medium`, modelo não identificado no
  retorno do runtime) foram acionados conforme configuração local,
  mas não concluíram investigação independente por limite de uso da plataforma.
  Security também teve uma leitura rejeitada pela revisão automática por esse
  limite, sem julgamento de ação insegura. A inspeção e implementação foram
  concluídas pelo agente principal; não se declara revisão independente completa.
  Nenhum sucesso de subagente ou alteração do esforço do agente principal é
  presumido pela existência dos arquivos de configuração.
- Sem commit, push, PR, merge ou deploy; instruções e configurações de agentes
  preservadas. Não se declara CI verde a partir de testes locais direcionados.
