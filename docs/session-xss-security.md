# Sessões web e proteção contra XSS

Implementação iniciada em 02/10/2026; validação retomada em 05/10/2026. Decisão: [ADR](adr-session-xss.md).

## Contrato e isolamento

- Access token somente na memória do frontend. O carregamento remove `hemodinks.session` dos dois storages; o perfil local não concede autorização.
- `POST /api/session/restaurar` usa exclusivamente o refresh opaco HttpOnly para localizar a sessão e o vínculo. O servidor valida atividade, revogação, SecurityVersion, clínica, usuário, equipe e operador e devolve a identidade canônica com novo Bearer.
- Endpoints de negócio continuam Bearer. O cookie não autentica requisições de negócio; clientes externos podem continuar usando Authorization.
- Login web, identificação, restauração, renovação e logout usam `X-Session-Refresh: 1`. Origens declaradas devem constar exatamente em `Cors:AllowedOrigins`. Clientes externos sem Origin continuam compatíveis. CORS mantém origens específicas e credenciais.
- Equipes passam a ter sessão persistida, com versões da equipe/operador e identificação confiável. PIN, auditoria, restrição de escrita de equipe sem operador e validação de recursos permanecem no servidor. A exceção de operações de sessão permite logout sem liberar escrita de negócio.
- O prazo absoluto de 12 horas, a inatividade e o fortalecimento anterior de senhas permanecem. Renovação e troca de PIN não reiniciam o instante original.
- Web Locks serializa mudanças no cookie entre abas. Sem essa API, há fila por aba e conflitos tratados com tentativas limitadas. A restauração tem prazo total de 15 segundos, incluindo espera pelo lock; expirar não permite ignorar um lock ocupado.
- Logout e troca de contexto limpam caches e notificam abas por identificadores de contexto, nunca por credenciais. Uma geração local impede entregar respostas privadas de uma sessão anterior. Logout durante bootstrap também invalida sua resposta pendente. Uma notificação antiga não encerra outra sessão já ativa.

## Configuração de implantação (não aplicada remotamente)

1. Aplicar, pelo processo normal de implantação, a migração `20261004173546_PersistTeamSessionIdentity`. Ela acrescenta cinco colunas a AuthenticationSessions. Não apaga dados nem muda tabelas de negócio. Sessões individuais existentes permanecem compatíveis; equipes antigas sem cookie precisam fazer login para usar restauração.
2. Preferir **mesma origem**, com frontend compilado com `VITE_API_URL=/`, ou API em domínio HTTPS do mesmo site. Cookies entre sites podem ser bloqueados mesmo com `SameSite=None`; não existe fallback para JWT persistido.
3. API: configurar `AuthenticationSession__RefreshCookieSameSite=Lax` para mesma origem/site; `None` somente quando a topologia exigir, com validação nos navegadores suportados. `Strict` também é aceito. Em ambientes publicados o cookie é Secure, HttpOnly, sem Domain e com Path `/api/session`; Development/Testing usa Lax sem Secure. Expiração limitada ao prazo absoluto. Não publicar com ambiente Development/Testing.
4. Nginx/Docker existente: `API_UPSTREAM=https://host-da-api` **sem barra final nem caminho**, com `VITE_API_URL=/` no build. O entrypoint seleciona o template de proxy e preserva `/api/...`; TLS do upstream é verificado. Sem API_UPSTREAM permanece o modo estático. Isso é proxy, não BFF.
5. Vercel: o rewrite `/api/:path*` aponta para a API de homologação já identificada no repositório. Antes de usar em outro ambiente, escolher explicitamente o destino correspondente. Não encaminhar produção para homologação. O arquivo não foi publicado neste trabalho.
6. Azure Static Web Apps e Render estático: configurar backend vinculado/proxy suportado ou domínio próprio da API do mesmo site. Somente mudar SameSite não resolve bloqueio de cookies de terceiros.
7. Manter em `Cors:AllowedOrigins` as origens exatas dos frontends de cada ambiente, sem wildcard com credenciais.

## Hosts, conteúdo e telemetria

Headers estão nos arquivos que entregam o frontend: `nginx.conf`, `nginx.proxy.conf.template`, `vercel.json`, `public/staticwebapp.config.json` e `render.confirmation.yaml`. A CSP restringe scripts à própria origem, bloqueia scripts inline, atributos de script, eval, objetos e base URL arbitrária. Estilos inline permanecem necessários para React e os tutoriais. Fontes são locais. Imagens HTTPS externas ainda são permitidas para fotos/logotipos existentes.

O resolver de imagens rejeita SVG em data URL, protocolos ativos, credenciais em URL, blob de outra origem e HTTP em página HTTPS. O teste renderiza o componente real com nome malicioso e SVG ativo. React mantém escaping. Não foi introduzido HTML rico; arquivos continuam downloads, sem preview HTML executável. Conteúdo estático dos tutoriais não deve receber HTML fornecido por usuários.

Referrer-Policy é `no-referrer`; não foi configurado coletor de relatórios CSP que receba URLs privadas. O formato de access log do Nginx exclui query strings, headers, IP, referrer e user-agent. Nos provedores gerenciados, revisar também a retenção/redação dos logs de acesso, especialmente a query dos links de recuperação.

Sentry envia um evento mínimo sem usuário, request, breadcrumbs ou mensagens privadas. OpenTelemetry mantém duração e status numérico, removendo URLs, eventos, atributos privados e nomes de operações dinâmicos antes da exportação. New Relic mantém page views/performance, com URLs ofuscadas e captura de replay, ações, logs, erros e requisições desativada. Essa redução intencional de detalhe limita diagnóstico com dados clínicos.

Destinos padrão de New Relic e APIs conhecidas estão explícitos em `connect-src`. Se Sentry, OTLP ou outro beacon estiver habilitado, acrescentar **a origem HTTPS exata do coletor** às políticas dos hosts usados, sem liberar `https:` ou curingas em connect-src. Validar recebimento e ausência de dados privados antes de publicar. O JSON público do OTLP não exporta headers de autenticação: o coletor web deve aceitar ingestão pública controlada ou usar proxy que injete credenciais no servidor. Nunca colocar credenciais de servidor em VITE_* ou runtime-config público.

## Verificação e limites

A suíte completa anterior executou 658 testes de backend: os 647 testes fora do navegador passaram, incluindo SQL Server descartável, concorrência, isolamento e migrações; 10 dos 11 E2Es passaram, e o cenário de equipe sem operador excedeu o prazo durante bootstrap. A repetição dos 11 E2Es existentes passou integralmente. Foram acrescentadas regressões para lock ocupado e logout entre abas durante bootstrap, além dos testes de cookie, CSRF real, equipes, expiração e armazenamento.

Validação de 05/10/2026:

| Verificação | Resultado |
| --- | --- |
| Frontend, `npm test -- --maxWorkers=1` | 428 testes aprovados em 73 arquivos |
| E2E com API real isolada, execução final | 12 de 12 aprovados, incluindo lock ocupado, reload, logout entre abas e equipes |
| `npm run build` e `npm run budget` | Aprovados; entrada JS 75,24 kB gzip, limite 120 kB |
| Auditoria de arquitetura do frontend | 289 arquivos; nenhuma violação; aviso preexistente sobre types.ts |
| Compilação .NET usada nos E2Es | Sem erros; avisos IDE0005 de imports desnecessários |
| EF `has-pending-model-changes` | Nenhuma diferença pendente após a migração |
| Nginx + Chromium no build final | Headers verificados e scripts inline/eval/origem externa bloqueados; query de recuperação ausente dos access logs |
| Template de proxy, envsubst e `nginx -t` | Aprovados |

Logs locais: `logs/session-xss/front-final.log`, `e2e-final.log`, `build-final-front.log`, `budget-final.log`, `architecture-resume.log`, `model-check.log`, `host-final.log` e `proxy-check.log`. O runner E2E usa `HEMODINKS_E2E_FRONT_PATH` apontando para o checkout do frontend e `dotnet test HemodinksAPI.Tests --filter FullyQualifiedName~LoginBrowserTests`; a API e os dados são isolados por cenário.

O host Nginx local foi exercitado por HTTP e Chromium: CSP e headers presentes na SPA, assets e 404; scripts inline, eval e scripts de origem não autorizada bloqueados. Repetir com `SECURITY_HOST_URL` apontando para um host local e `node scripts/verify-security-host.mjs` no frontend. Isso não comprova headers dos serviços publicados.

Pendências exclusivamente de implantação: aplicar migração pelo fluxo do ambiente, confirmar domínio/proxy, cookies em Chrome/Firefox/Safari com bloqueio de terceiros, headers publicados em rotas/assets/erros e destinos de telemetria. Nenhum deploy, configuração remota ou dado de produção foi alterado.

Riscos residuais: JavaScript malicioso executando na origem ainda pode observar tokens em memória e agir como o usuário. HttpOnly e CSP não eliminam XSS. Logout sem rede limpa o cliente, mas a revogação remota depende de a API receber a requisição; uma sessão ainda válida pode voltar em recarregamento se essa requisição não chegou. A sessão continua sujeita aos prazos e às validações do servidor. Navegadores sem Web Locks não têm serialização global entre abas, embora o servidor impeça salvar rotações concorrentes como se ambas tivessem sido válidas.

Referências: [OWASP — armazenamento de sessão no navegador](https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html), [New Relic — ofuscação no agente do navegador](https://docs.newrelic.com/docs/browser/new-relic-browser/configuration/obfuscate-browser-agent-data/).

## Arquivos principais

Backend: SessionBootstrapEndpoints, SessionRequestSecurity, SessionLoginIssuer, AuthenticationSessionCookie, endpoints User/Equipe/Session, AnonymousTeamReadOnlyMiddleware, ClinicaResolutionMiddleware; AuthenticationSessionService/Identity/Store; JWT/CurrentUserContext; EfAuthenticationSessionStore; modelo AuthenticationSession e migração/snapshot. Testes: SessionBootstrapEndpointTests, SessionCookieAttributesTests, SessionRenewalEndpointTests, TeamSessionAbsoluteLifetimeTests, SessionLifetimeConcurrencyTests e LoginBrowserTests.

Frontend: useAuthSession, AppContent, sessionService, sessionLock, sessionEpoch, authService, api; resolver de fotos; telemetryPrivacy, observability, otel, newRelic e gerador de runtime-config; configurações dos hosts/Docker e entrypoint do proxy. Testes: App, hooks/serviços, ImageSafety, telemetryPrivacy, login-real-api e script verify-security-host. Mudanças anteriores de prazo absoluto foram preservadas.
