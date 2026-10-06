# Acesso ao banco na validação de sessões

## Escopo e linha de base

O pipeline executa autenticação JWT, `AuthenticationSessionMiddleware`,
`PasswordRecoveryMiddleware`, `ClinicaResolutionMiddleware`, módulos/licenças,
autorização e auditoria de equipes, nessa ordem. Os últimos componentes e a
autorização por recurso não são substituídos pela validação da sessão.

Antes da otimização, uma sessão individual válida exige uma leitura do agregado
sessão/vínculo/identidade, uma gravação de atividade, uma leitura de recuperação de
senha, uma leitura da clínica e uma leitura de vínculo ativo. Equipes acrescentam
leituras de equipe e operador nos dois validadores. Conflitos de rowversion exigem
releitura. As contagens medidas estão na seção de resultados ao final.

Toda requisição autenticada validada pelo middleware registra atividade,
inclusive polling/background; `/atividade` usa esse mesmo middleware. A renovação
por cookie é excluída desse caminho: `Active=false` não registra atividade;
`Active=true` registra. Restauração usa o fluxo próprio de refresh. A renovação
legada de equipe atravessa o middleware e mantém as verificações adicionais do
caso de uso. Troca de clínica revalida o vínculo de destino e altera a sessão.

`PlatformDbContext` tem escopo global explícito para identidade. O contexto de
negócio continua tenant-scoped. Nenhum filtro é removido. `SaveChanges` valida
relacionamentos tenant; a entidade AuthenticationSession não dispara auditoria
de negócio automaticamente. Atualização do perfil do vínculo pode acrescentar
uma gravação. Auditoria de equipe permanece no pipeline.

Em particular, o middleware de auditoria registra POST/PUT/PATCH/DELETE de
equipes, inclusive POST `/atividade` e renovação legada autenticada de equipe;
isso pode acrescentar INSERT
de auditoria mesmo quando não há UPDATE de atividade. Não foi removido. Rotas
de módulos também podem consultar plano/módulos da clínica para perfis não
administradores. As tabelas abaixo medem os três middlewares de validação, sem
SQL do endpoint, auditoria ou licenças; não representam todo o custo de uma
operação de negócio. Os cenários `*-service` medem somente o serviço de sessão.

## Medição reproduzível

`SessionDatabaseMeasurementTests` usa SQL Server com rowversion nativa, banco
descartável e relógio controlado. O interceptor conta comandos sem registrar
SQL ou parâmetros. Cada requisição cria um contexto novo, simulando ausência de
memória compartilhada entre réplicas. Aquecimento fica fora das amostras.

```powershell
$env:HEMODINKS_TEST_LOCALDB='1'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --filter FullyQualifiedName~SessionDatabaseMeasurementTests --logger 'console;verbosity=detailed'
```

Alternativamente configure `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` para um
servidor de testes com permissão de criar/remover bancos descartáveis. Nunca use
produção. O teste escolhe um nome exclusivo e remove apenas esse banco.

As latências são locais, incluem EF e middlewares medidos, não representam rede
HTTP, carga de produção ou economia financeira. Contagem de comandos de UPDATE
não é contagem de linhas alteradas, especialmente em conflitos otimistas.

## Configuração e precisão temporal

`AuthenticationSession:ActivityPersistenceIntervalSeconds` aceita 0 a 60 segundos,
sempre menor que o timeout de inatividade. O padrão é **0**, preservando o prazo
anterior, sem tolerância adicional. Não foi modificada configuração de produção.
Um valor como 30 habilita o agrupamento e aceita no máximo 30 segundos adicionais
de inatividade. A redução de leituras independe dessa configuração.

Com intervalo I, uma leitura de segurança continua ocorrendo a cada requisição.
O timestamp só é atualizado quando `agora - LastActivityAt >= I`; chamadas dentro
do intervalo não enviam UPDATE de atividade. O prazo de inatividade é
`LastActivityAt + IdleTimeout + I`. A última atividade aceita não persistida está
estritamente antes de `LastActivityAt + I`; portanto o prazo nunca antecede
`última atividade real + IdleTimeout`, nem ultrapassa esse instante por mais de I.
Essa propriedade vale mesmo após reinício ou mudança de réplica, pois não depende
de estado local. O limite absoluto sempre prevalece quando alcançado primeiro.

O intervalo foi mantido opcional porque não é possível eliminar essas escritas
entre requisições e conservar simultaneamente um prazo exato sem armazenar a
atividade em algum lugar durável. Cache local ou atualização não aguardada
perderia essa garantia. Projeções menores e reutilização nas operações que
mudam credenciais foram deixadas de fora para preservar suas revalidações; a
redução comprovada está no pipeline e no agrupamento explicitamente configurado.

Todas as réplicas precisam usar a mesma configuração e relógios UTC sincronizados.
Não altere o intervalo durante sessões existentes em um rollout misto: uma
réplica com tolerância menor pode antecipar o prazo das atividades agrupadas pela
outra. Uma redução do intervalo requer drenagem das sessões antigas (ou
reautenticação planejada). Isso é uma configuração estática de implantação,
não uma opção por usuário ou requisição.

Refresh passivo continua sem registrar atividade. Refresh ativo já precisa
persistir rotação; ele registra a atividade no mesmo SaveChanges. Troca de
clínica também mantém sua gravação. Essas operações e a validação nunca fazem
LastActivityAt regredir. Não há escrita em background, Redis ou novo serviço.

## Snapshot e concorrência

O resultado da validação contém valores imutáveis, sem entidades EF. A API o
guarda apenas em HttpContext.Items e confere sessão, usuário, vínculo, clínica,
perfil, SecurityVersion, equipe, operador, versões e identificação confiável
antes de reutilizá-lo. Alterações de contexto levam ao caminho original de
consulta. Operações de troca de vínculo, PIN e renovação continuam revalidando
suas próprias condições; não recebem esse snapshot como substituto.

A decisão usa o estado lido na validação da requisição, sem garantia de uma
transação aberta até a resposta. Uma alteração posterior será observada pela
próxima validação; não há janela de cache entre requisições. Policies, licenças,
filtros tenant e autorização por recurso continuam independentes.

As atualizações continuam usando o WHERE de rowversion gerado por EF/SQL Server.
Um conflito limpa o tracking e recomeça a validação, até três tentativas; não
autoriza usando entidades antigas. Se as três tentativas sofrerem conflito,
recusa somente a requisição com HTTP 503, código `session_validation_busy`,
`Retry-After: 1` e `Cache-Control: no-store`. Não revoga a sessão nem emite
401 por contenção: isso provocava logout de sessões válidas em rajadas de
requisições, como no carregamento do dashboard. Expiração, revogação e contexto
inválido continuam recusados com 401.
Falhas de persistência propagam erro, sem avançar para o endpoint. O retry também
preserva atividade quando uma rotação passiva concorre com o touch. Nenhuma
transação fica aberta durante toda a requisição.

## Resultados locais

SQL Server LocalDB; 20 requisições por cenário; contexto novo por requisição;
relógio controlado; aquecimento fora da medição. Linhas confirmadas são o resultado
de SaveChanges concluído (nestas amostras somente a entidade sessão foi alterada).

| Cenário | Versão / intervalo | Leituras | Comandos UPDATE | Linhas confirmadas | Tempo do lote (ms) |
|---|---|---:|---:|---:|---:|
| Individual, sequencial | Antes | 80 | 20 | 20 | 467,67 |
| Individual, sequencial | Depois / 0 | 20 | 20 | 20 | 151,10 |
| Individual, sequencial | Depois / 30 | 20 | 1 | 1 | 315,45 |
| Concorrente, mesma sessão | Antes | 85 | 6 | 1 | 82,14 |
| Concorrente, mesma sessão | Depois / 0 | 21 | 2 | 1 | 41,61 |
| Concorrente, mesma sessão | Depois / 30 | 21 | 2 | 1 | 63,77 |
| Concorrente, sessões distintas | Antes | 80 | 20 | 20 | 73,83 |
| Concorrente, sessões distintas | Depois / 0 | 20 | 20 | 20 | 44,94 |
| Concorrente, sessões distintas | Depois / 30 | 20 | 20 | 20 | 59,11 |
| Refresh passivo (serviço) | Antes | 20 | 20 | 20 | 178,36 |
| Refresh passivo (serviço) | Depois / 0 | 20 | 20 | 20 | 137,03 |
| Refresh passivo (serviço) | Depois / 30 | 20 | 20 | 20 | 190,94 |
| Refresh ativo (serviço) | Antes | 20 | 20 | 20 | 187,42 |
| Refresh ativo (serviço) | Depois / 0 | 20 | 20 | 20 | 139,81 |
| Refresh ativo (serviço) | Depois / 30 | 20 | 20 | 20 | 173,76 |
| Troca de vínculo (serviço) | Antes | 40 | 20 | 20 | 235,87 |
| Troca de vínculo (serviço) | Depois / 0 | 40 | 20 | 20 | 196,58 |
| Troca de vínculo (serviço) | Depois / 30 | 40 | 20 | 20 | 241,47 |
| Equipe com operador, sequencial | Antes | 160 | 20 | 20 | 413,00 |
| Equipe com operador, sequencial | Depois / 0 | 60 | 20 | 20 | 233,23 |
| Equipe com operador, sequencial | Depois / 30 | 60 | 1 | 1 | 218,17 |

A amostra de concorrência usa o mesmo instante lógico: algumas consultas podem
ver a primeira gravação já concluída e o EF não envia outra para um timestamp
igual. Conflitos e latências variam com o escalonamento; não se afirma ganho de
latência em todos os casos. Os testes de interleaving forçam conflitos separados
para comprovar segurança, independentemente desse escalonamento.

A redução estável do lote sequencial foi 75% nas leituras; o intervalo opcional
reduziu 95% dos UPDATEs desse lote. Sessões distintas que precisam persistir
atividade continuam exigindo uma gravação por sessão. Não há estimativa de
economia financeira nem extrapolação para produção.


A linha de base foi executada no código da revisão `136ce35`, extraído em uma
cópia isolada. O mesmo instrumento de medição foi copiado para ela, removendo
apenas a configuração nova e ajustando as expectativas de contagem do teste.
As implementações de produção daquela revisão não foram alteradas. Os lotes
antes/depois finais foram executados separadamente. Logs locais:
`logs/session-measurement-final-before.log` e `logs/session-measurement-final-after.log`.

## Arquivos e cobertura

- `Application/Features/Sessions/AuthenticationSessionService.cs`: opção de
  persistência, timestamps monotônicos, retry com revalidação e snapshot.
- `Application/Features/Sessions/SessionLifetimePolicy.cs` e
  `AuthenticationSessionIdentity.cs`: prazo de inatividade coerente com a
  tolerância, validação da configuração e informação devolvida na emissão.
- `Application/Features/Sessions/SessionValidationSnapshot.cs`: valores imutáveis
  necessários ao reaproveitamento na requisição.
- `Api/ValidatedSessionRequest.cs`, `AuthenticationSessionMiddleware.cs`,
  `PasswordRecoveryMiddleware.cs`, `ClinicaResolutionService.cs` e
  `ClinicaResolutionMiddleware.cs`: transporte interno e conferência do contexto,
  preservando o caminho original quando não houver snapshot compatível.
- `Tests/SessionActivityPersistenceTests.cs`: relógio controlado, fronteira do
  intervalo, atividade contínua, inatividade, limite absoluto, mudanças de
  segurança/perfil e contextos novos.
- `Tests/SessionActivitySqlServerTests.cs`: interleaving real de gravação recente,
  revogação, troca de vínculo, refresh passivo e falha de persistência.
- `Tests/ValidatedSessionRequestTests.cs`: alteração de cada identificador/versão
  impede reaproveitamento; outra requisição nunca herda o snapshot.
- `Tests/SessionActivityAuthorizationTests.cs`: remoção de perfil privilegiado
  altera a autorização imediatamente, mesmo dentro do intervalo de atividade.
- `Tests/SessionDatabaseMeasurementTests.cs`: contagens e latências comparáveis
  em SQL Server para sessão individual, equipe com operador, concorrência,
  atividade, renovação ativa/passiva e troca de vínculo.

Os caminhos acima estão abreviados: os projetos se chamam
`HemodinksAPI.Application`, `HemodinksAPI.Api` e `HemodinksAPI.Tests`.
A implementação de Infrastructure, os filtros tenant, as migrations e os índices
permanecem existentes; não foi necessário alterá-los.

Os testes existentes de `SessionLifetimeConcurrencyTests` comprovam refresh
concorrente com logout e limite absoluto. As suítes de integração de autorização,
equipes, recuperação de senha e isolamento de clínicas também foram executadas.
A execução ampla aprovou 666 testes; dois testes de migrations não concluíram
porque LocalDB não possui Full-Text Search: `EventReminderProcessorConcurrencyTests`
e `LegacyFinancialBackfillMigrationTests`. Precisam de uma instância SQL Server
de teste com esse recurso. A restrição das migrations não foi removida.

Validação final: compilação concluída; 101 testes específicos de sessões,
snapshots e concorrência aprovados, mais o teste HTTP adicional de remoção de
perfil com agrupamento habilitado (102 casos em execuções complementares).
Os 12 E2Es de `LoginBrowserTests`, com API real isolada, também passaram.
As medições finais passaram tanto na cópia anterior (1 caso) quanto na atual
(2 configurações). Essas execuções se sobrepõem à suíte ampla; não devem ser
somadas como quantidade de testes únicos.

Não houve deploy, migração, mudança de configuração ou acesso a dados de
produção. Agrupamento de atividade continua desativado por padrão. Para avaliar
um intervalo positivo em implantação futura, considerar a tolerância e a
consistência de configuração entre réplicas descritas acima.
