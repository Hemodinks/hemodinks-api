# Warm-up de infraestrutura

Ao abrir o frontend, `main.tsx` solicita a renderização e chama
`void warmupApi()`. O serviço usa o cliente Axios existente diretamente para
`GET /api/warmup`, sem os helpers que adicionam tenant, token ou renovam sessão.
Não há dependência entre essa promessa e a renderização/login, nem mensagens de
erro, polling ou retry no frontend. Nenhum componente visual é modificado.

Com Azure Container Apps em `minReplicas = 0`, a requisição pode iniciar uma
réplica. O endpoint reutiliza `IDatabaseReadinessProbe.CheckAsync(false, token)`:
abre a conexão pelo `ISqlConnectionFactory` e executa apenas `SELECT 1`, provocando
o resume do Azure SQL Serverless se estiver pausado. Não resolve AppDbContext,
PlatformDbContext ou tenant, não valida schema e não lê/escreve entidades.

O endpoint está num ramo exato de infraestrutura antes dos middlewares de
autenticação/sessão/clínica. Mesmo que o cliente envie credenciais, ele não toca
sessões ou claims. CORS, encaminhamento de IP, HTTPS e logging existentes são
reutilizados. As demais rotas mantêm seu pipeline. O handler HTTP apenas coordena
o contrato de Application; o SQL permanece na implementação de Infrastructure.
Não há novo DbContext, CQRS de negócio ou política de retry paralela.

## Limites e respostas

- Sucesso: `204 No Content`, com `Cache-Control: no-store`.
- Falha ou timeout: `503` vazio, sem detalhes de banco/exceção.
- Rate limiter nativo: 5 requisições por minuto por IP, sem fila; excesso: `429`.
- Cancelamento da requisição e prazo total de 30 segundos enviados ao probe.
  O timeout de comando existente de 3 segundos permanece. O timeout de conexão
  permanece o da connection string. O cliente aguarda no máximo 90 segundos,
  incluindo o tempo anterior à inicialização do processo no Container App.
- A configuração `EnableRetryOnFailure` já existente do EF não muda. O probe
  usa ADO.NET e não executa uma operação EF; não recebe retries adicionais.
  Uma falha de conexão já pode iniciar o resume, mesmo sem concluir a consulta.
- `sessionStorage['hemodinks-warmed']` registra a **tentativa** antes do HTTP,
  evitando chamadas concorrentes, navegações e reloads na mesma sessão da aba.
  A flag permanece em falha para evitar rajadas. Com storage bloqueado, a
  proteção em memória dura até o próximo carregamento. Abas que herdam storage
  de uma aba de origem podem herdar a flag também.

## Operação e Azure

Não são necessárias migrations, novas credenciais ou mudanças de JWT, tenant,
réplicas, auto-pause, probes ou permissões SQL. Publicar o backend antes do
frontend; manter `VITE_API_URL` apontado para a API e o domínio do frontend em
`Cors:AllowedOrigins`. A funcionalidade não foi publicada por esta alteração.

O workflow atual `publish-container.yml` já configura `ForwardedHeaders__Enabled=true`,
`ForwardedHeaders__ForwardLimit=1` e `ForwardedHeaders__TrustAnyImmediateProxy=true`.
Nada disso foi alterado. A segurança dessa configuração pressupõe que o processo
só receba tráfego pelo ingress gerenciado, sem acesso direto à porta da aplicação.
O Azure acrescenta o IP à direita de `X-Forwarded-For`; somente esse último valor
é fornecido pelo ingress. A configuração de um salto é coerente com esse caminho,
mas a topologia real precisa ser confirmada em homologação.
[Referência Azure](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview#http-headers).

O limitador usa `RemoteIpAddress` depois do middleware existente. Fora desse
ambiente protegido, manter a confiança restrita a proxies/redes conhecidos.
Sem forwarding correto, usuários podem compartilhar a cota do IP do ingress;
com confiança inadequada, pode haver falsificação de IP. Não ampliar a cadeia
de confiança para viabilizar o warm-up.

O limitador é local ao processo, coerente com `maxReplicas = 1`, e reinicia com
a réplica. Ele protege o trabalho da API depois do startup; não impede que uma
requisição chegue ao ingress e acorde o container. Não equivale a proteção
distribuída contra DDoS. Usuários atrás do mesmo NAT compartilham a cota.

O aquecimento é best effort, não garante conclusão antes do clique em Entrar,
e pode antecipar tempo faturável de API/banco. Não mantém a infraestrutura
acordada por pings. Uma aba aberta por muito tempo não aquece novamente; o fluxo
normal de login continua responsável pela sua própria tentativa.

## Observação e desativação

Usar o logging estruturado/Serilog existente, categoria `InfrastructureWarmup`:
`WarmupCompleted` (Information), `WarmupFailed` (Warning), campo
`WarmupDurationMs` e motivo genérico `Unavailable` ou `CancelledOrTimedOut`.
`WarmupStarted` está em Debug para evitar volume desnecessário. O tempo medido
começa no handler, não inclui startup do container. Logs HTTP existentes
registram status/duração da requisição. Não são registrados texto de exceção,
connection string, usuário ou SQL adicional pelo warm-up.

Para desativar a chamada, definir `VITE_WARMUP_ENABLED=false` e gerar/publicar
um novo build frontend. Para desativar somente o toque ao banco, configurar
`Warmup__Enabled=false` no backend: o endpoint continua retornando `204` sem
executar o probe. A chamada ainda pode acordar o container; para impedir ambos,
desativar também no frontend. Para repetir um teste manual na mesma aba,
remover a chave `hemodinks-warmed` do sessionStorage e recarregar.

## Validação

- Backend: `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj`.
  `WarmupEndpointTests` cobre resposta, falha, cancelamento, idempotência,
  ausência de resolução de contextos, métodos HTTP e rate limit. O probe real
  também é exercitado sobre banco relacional vazio (SQLite).
- Frontend: `npm test`, `npm run build`, `npm run audit:architecture`.
- Browser: `npm run test:e2e -- --grep 'warmup:|login imediato'` e suíte geral.
- E2E com API real: configurar `HEMODINKS_E2E_FRONT_PATH` e executar
  `LoginBrowserTests`; os testes usam API e banco de teste isolados.
- O resume real exige validação em ambiente Azure de homologação com banco
  pausado e zero réplicas. Os testes locais não simulam a infraestrutura Azure.

Referências do framework: [rate limiting ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
e [resiliência EF Core](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).

## Evidência local (25/09/2026)

- Build da solução `HemodinksAPI.slnx`: aprovado, sem erros/avisos.
- Build frontend, auditoria de arquitetura (269 arquivos) e orçamento de bundle:
  aprovados. O Vite mantém um aviso de import estático/dinâmico de observability.
- Vitest final: 344/347 aprovados, incluindo os 8 testes novos do serviço. Três
  falhas no App.test.tsx: alternância de tema (elemento não encontrado), cadastro
  de pacientes (timeout de 15 s) e filtros de pacientes (timeout de 10 s).
  As execuções anteriores variaram entre 1 e 5 falhas nesse mesmo arquivo.
  As expectativas e limites desses testes não foram alterados; a suíte não
  está integralmente verde. Log: `artifacts/warmup-frontend-final.log`.
- Backend geral: 430 aprovados, 12 ignorados, 3 falhas. O teste de diretório SQL
  passou na repetição após o LocalDB iniciar; os outros dois requerem Full-Text
  Search, ausente na instância local. Nenhum desses testes foi alterado.
- Validação direcionada final: 15 aprovados (13 de warm-up e 2 de diretório SQL),
  incluindo o probe real em SQL Server, isolamento de contextos, IPs distintos e
  forwarding de um salto. Relatório: `logs/warmup/warmup-final.trx`.
- Playwright geral: 74 aprovados, 23 ignorados e 3 falhas iniciais. Após permitir
  somente o warm-up na expectativa antiga das páginas públicas, os três casos
  passaram em repetição direcionada; junto com os testes de warm-up, 6/6 passaram.
  Os dois timeouts de pacientes/tutoriais passaram sem alterações nesses testes.
- Login com API real: 11/11 aprovados, incluindo seleção, PIN, isolamento,
  permissões, cancelamento e layout. Relatório: `logs/warmup/warmup-login-browser.trx`.
- `git diff --exit-code` confirmou ausência de alterações nos endpoints de users,
  middlewares existentes, Application, Infrastructure, App.tsx e serviços de
  autenticação/sessão. `/api/users/login-context` permanece intacto.

Os logs de execução estão em `artifacts/warmup-*.log` (ignorados pelo Git).
Não houve deploy nem teste de cold start em Azure nesta validação.
