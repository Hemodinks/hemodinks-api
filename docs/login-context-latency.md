# Espera no login-context

O login atual chama `POST /api/users/login-context` para validar a credencial e
descobrir suas clínicas. Depois chama `authenticate` com o contexto escolhido.
A seleção de clínica continua ocorrendo somente após validar a credencial; os
modos Individual e Equipe (Nenhuma, Seleção e PIN) mantêm os contratos existentes.

## Diagnóstico e limites

Uma requisição pendente no navegador não comprova cold start. O `Program.cs`
aguarda `InitializeDatabaseAsync()` antes de começar a atender; a inicialização
do processo e o acesso ao banco podem compor a espera da primeira requisição.
O runbook de produção registra `minReplicas=0`, mas isso não comprova a
configuração atual de homologação. A consulta à Azure nesta análise não pôde
ser concluída porque a sessão do CLI expirou. Não houve alteração de infraestrutura.

Para confirmar a causa, correlacionar o horário da requisição com a criação da
réplica, os logs de inicialização e o tempo de execução do handler. Comparar
primeiro acesso após inatividade com acessos subsequentes. O log de sucesso do
handler agora informa `ElapsedMs`, `UserLookupMs`, `MembershipLookupMs` e
`HashVerificationCount`. Esse cronômetro começa no handler: não mede a fila no
ingress, a inicialização do processo nem os middlewares anteriores.

Se a aplicação estiver escalando a zero, manter ao menos uma réplica evita essa
parcela de cold start, com impacto de custo. Referência:
https://learn.microsoft.com/en-us/azure/container-apps/cold-start

## Alterações

- Ao abrir a página de login, o frontend prepara o acesso consultando
  `GET /api/public/clinicas` antes de montar os campos de e-mail/senha. O loading
  explica a operação e, após 12 segundos, a espera prolongada.
- A consulta pública admite até cinco tentativas sequenciais de 60 segundos,
  com intervalos de 5/10/10/10 segundos (até aproximadamente 5min35s de espera
  ativa). Depois oferece nova tentativa manual. Falhas definitivas, como 403,
  encerram a preparação sem repetição automática. Navegar para fora cancela a chamada.
- O diretório público fica no cache de memória por até cinco minutos; ele contém
  apenas metadados públicos e pode ser limitado a 50 clínicas pela API. Não é uma
  lista de vínculos do usuário. A autenticação continua resolvendo suas clínicas
  na API, sem cruzar e-mails/senhas no navegador. A preparação antecipa o acesso
  ao serviço/banco, mas não comprova cold start nem garante que um endpoint de
  autenticação com um problema próprio responderá rapidamente.
- O formulário só é liberado com resposta válida e ao menos uma clínica. Não há
  persistência desse diretório em localStorage/sessionStorage nem coleta de senha
  durante a preparação. Sessões já autenticadas não passam por essa tela.

- Projeção apenas dos campos necessários de usuário/clínica.
- Verificação de cada hash e consulta de bloqueio da mesma conta uma vez por
  requisição, mesmo quando há múltiplas clínicas. Nenhum resultado de autenticação
  é reutilizado entre requisições; a força do PBKDF2 permanece igual.
- Busca das credenciais temporárias em lote, mantendo validade, uso, revogação,
  usuário e clínica como condições de aceitação.
- No frontend, apenas `login-context` passa de 60 para 120 segundos de timeout.
  Isso amplia a tolerância à espera; não reduz a latência nem supera limites do proxy.
- Mensagens evoluem após 12 e 35 segundos. Cancelar aborta a chamada no browser,
  limpa senha/PIN e ignora respostas tardias. A API pode já ter processado parte
  da chamada; cancelar não desfaz operações no servidor.
- Timeout e indisponibilidade recebem mensagens próprias, sem atribuir a falha
  à senha. Não há reenvio automático de credenciais.

## Validação reproduzível

API: testes `LoginContextEndpointTests` e `LoginContextWorkTests`. O segundo
confere uma verificação de hash para três clínicas, nova checagem de bloqueio na
requisição seguinte e isolamento/validade das credenciais temporárias.

Frontend: `useLoginPreparation.test.tsx`, `useLoginFlow.test.ts`,
`LoginLoadingOverlay.test.tsx`, `api.test.ts`, build e auditoria de arquitetura.
Playwright: filtros `login preparation:` e `login wait:` em
`e2e/hemodinks.spec.ts` usa respostas simuladas para validar demora, cancelamento,
indisponibilidade e nova tentativa manual no navegador. Esses testes não medem
o tempo real de resposta na Azure.

Validação adicional da preparação: os 11 cenários de `LoginBrowserTests`
passaram com navegador e API local real, usando banco isolado por cenário.
Cobrem Individual, Equipe sem identificação/Seleção/PIN, PIN inválido,
restrições de escrita sem operador, isolamento entre clínicas, troca de operador,
reutilização de desafio, cancelamento e layout/acessibilidade. Executar com
`HEMODINKS_E2E_FRONT_PATH` apontando para o checkout do frontend e filtro
`FullyQualifiedName~LoginBrowserTests` no `dotnet test`.
