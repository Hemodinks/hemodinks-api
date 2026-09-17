# Inicialização sem aquecimento automático

O login do frontend deve renderizar seus campos sem consultar o diretório de
clínicas, `/healthz`, `/readyz` ou `/livez`. A descoberta dos vínculos continua no
fluxo autenticado por credenciais, iniciado pelo botão Entrar. A recuperação de
senha continua podendo consultar clínicas quando solicitada pelo usuário.

Durante uma tentativa, a interface informa a espera e permite cancelamento.
Timeouts e indisponibilidade não provocam reenvio automático de credenciais.
Uma nova tentativa exige ação do usuário; a senha é limpa após erro ou
cancelamento, e respostas tardias de uma tentativa cancelada são descartadas.

No backend, o inicializador resolve o contexto de schema somente quando precisa
validá-lo/aplicar migrations, e o contexto de plataforma somente quando seeds ou
manutenção estão habilitados. As políticas de migrations, validação de schema,
seeds e manutenção permanecem as mesmas. Nenhuma configuração de hospedagem,
pausa do banco ou quantidade de réplicas é alterada.

## Verificação local

- Backend: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj`.
- Testes relacionais: configurar `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING`
  para um SQL Server local com Full-Text Search. Os testes criam bancos isolados.
- O teste específico de vínculos entre paciente e usuário em LocalDB requer
  também `HEMODINKS_TEST_LOCALDB=1` e a instância `MSSQLLocalDB` disponível.
- E2E com API real: definir `HEMODINKS_E2E_FRONT_PATH` para o checkout do frontend
  e executar os testes `LoginBrowserTests`. Cada cenário cria sua API e banco de
  teste; não usar endpoints ou credenciais de produção.
- Frontend: executar auditoria de arquitetura, Vitest, build, orçamento de bundle
  e Playwright. Os cenários de login verificam ausência de aquecimento, espera,
  cancelamento, timeout e recuperação manual, além dos fluxos de autenticação.

## Medição

Validação da alteração em 17/09/2026, incluindo correções e reexecuções
direcionadas: 324 testes de frontend, 423 de backend, 55 cenários Playwright com
API simulada e 11 cenários de navegador com API real isolada aprovados. Auditoria
de arquitetura, build de produção e orçamento de bundle também passaram.
Os cenários de gravação de tutoriais não fazem parte dessa validação.

Usar os logs existentes `configuration_and_services`, `host_build`,
`database_initialization` e `http_started` para separar o trabalho da aplicação
da espera de infraestrutura. O tempo anterior ao início do processo não aparece
nesses cronômetros.

Para LCP, gravar a navegação anônima no build de produção com o painel Performance
do navegador. Registrar cache, dispositivo e rede usados. Comparar também o
JavaScript carregado antes do login: a interface autenticada é carregada sob
demanda. Medições locais não representam o LCP da hospedagem.

Na validação local de 17/09/2026, três navegações no build de produção, em
Chromium sem limitação artificial de rede/CPU e com contexto novo a cada rodada,
registraram LCP de 276, 268 e 252 ms. O elemento foi o título "Acesso ao sistema".
As chamadas à API estavam bloqueadas, os cookies opcionais estavam rejeitados e
nenhuma chamada à API ou health check foi emitida. O módulo da interface
autenticada não foi baixado nessas navegações. Esses números verificam o caminho
local; não são uma comparação antes/depois nem uma previsão para Azure/Render.

Não adicionar pings periódicos nem aquecimento ao abrir o login. A suspensão da
hospedagem ou do banco ainda pode afetar a primeira tentativa solicitada pelo
usuário.
