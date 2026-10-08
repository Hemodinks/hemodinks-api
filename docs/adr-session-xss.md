# Decisão: credenciais web em memória e refresh HttpOnly

Decisão registrada antes da implementação em 02/10/2026.

Implementação, configuração e validação: [proteção das sessões](session-xss-security.md).

| Alternativa | Avaliação |
| --- | --- |
| A — access token em memória + refresh HttpOnly | Escolhida. Remove a cópia persistente exposta a scripts, mantém transporte Bearer explícito e reutiliza sessões, rotação, autorização e clientes existentes. Exige bootstrap pelo cookie e persistência do contexto de equipes para restaurar sem JWT no storage. |
| B — autenticação web por cookie HttpOnly | Reduz também a leitura direta do access token, sem exigir BFF. Exigiria separar contratos e esquemas web/Bearer, alterar o frontend que usa claims em memória e aplicar CSRF em toda operação autenticada por cookie. Não é necessária para remover a persistência atual. |
| C — BFF | Manteria tokens no servidor, mas acrescentaria transporte, operação e estado intermediário. Não será criado outro serviço. Um proxy HTTP no host existente serve apenas para mesma origem; não é um BFF e não mantém tokens. |

A API é usada pelo frontend e por ferramentas de documentação, testes, scripts e integrações Bearer. O esquema Bearer continua explícito; cookies não autenticam endpoints de negócio. Restauração, refresh e logout usam cookie opaco com header CSRF obrigatório e origem permitida; identificadores do cliente nunca autorizam uma associação. A restauração obtém sessão e clínica no servidor. Equipes precisam conservar identificador e versões de equipe/operador, identificação confiável e instante original; isso justifica ampliar a sessão persistida, sem armazenar access tokens no banco.

O repositório documenta frontend em domínios `gestao-saude.tec.br`, Vercel e Render, e API em Azure Container Apps/Render. São topologias potencialmente entre sites: `SameSite=None` sozinho não garante cookies aceitos. A implantação suportada deve usar proxy `/api` no host existente ou API em subdomínio do mesmo site. Os arquivos dos hosts serão preparados; DNS/configuração remota e verificação publicada ficam pendentes, sem deploy neste trabalho. Azure Static Web Apps exige backend vinculado/proxy suportado ou domínio próprio da API. Não haverá fallback para tokens em storage se cookies forem bloqueados.

Dados de perfil ficam em memória e nunca autorizam operações. A transição remove `hemodinks.session` de sessionStorage/localStorage. Respostas atrasadas são descartadas por versão da sessão; mensagens entre abas não carregam credenciais. Logout limpa dados privados e revoga a sessão no servidor.

React mantém escaping. URLs de imagem devem aceitar apenas protocolos apropriados; arquivos ativos permanecem downloads, sem preview HTML. CSP será aplicada pelos hosts, com scripts da própria origem, sem `unsafe-eval` ou scripts inline. Estilos inline necessários à interface serão tratados separadamente de scripts. Telemetria deve usar destinos explícitos e não capturar credenciais/corpos privados.

Risco residual: tokens em memória não são imunes a XSS; um script executando na origem pode agir como o usuário e observar requisições. HttpOnly e CSP também não eliminam XSS. Extensões, comprometimento de dependências/host e navegadores precisam de controles adicionais.

Referências: [cookies de terceiros — MDN](https://developer.mozilla.org/en-US/docs/Web/Privacy/Guides/Third-party_cookies), [proxy por rewrites — Vercel](https://vercel.com/docs/routing/rewrites).
