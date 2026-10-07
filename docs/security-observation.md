# Observabilidade de segurança — issue #148

## Infraestrutura e fluxo

Reutiliza ILogger/Serilog, arquivos rotativos em logs/, OpenTelemetry/OTLP de ServiceDefaults e as políticas do monitoramento existente. Auditorias de plataforma e de acesso temporário permanecem no banco; eventos anônimos não são associados a identidades fictícias nessas tabelas.

A API classifica o resultado HTTP e captura somente email para pseudonimização de entrada tipada. Application define catálogo, contrato, opções e regras. Infrastructure usa Channel limitado e consumidor em segundo plano para persistência e detecção. Login não consulta histórico nem faz I/O adicional de banco, arquivo ou rede.

O endpoint GET /api/monitoramento/seguranca reutiliza a política Administrador: clínica do claim validado no servidor; SuperAdministrador tem visão global explícita. Parâmetros de clínica enviados pelo cliente não determinam o escopo. Eventos pré-login e alertas de correlação global só aparecem para a plataforma. Respostas usam Cache-Control: no-store.

## Catálogo

| Evento | Significado |
| --- | --- |
| AuthenticationSucceeded | Sessão emitida após autenticação completa, inclusive identificação de equipe |
| CredentialValidated | Descoberta de clínicas ou desafio intermediário, ainda sem login completo |
| AuthenticationRefused | Credencial recusada (invalid_credential) ou entrada inválida (invalid_request) |
| AuthorizationDenied | Acesso negado, distinto de credencial inválida |
| Throttled | HTTP 429 dos controles existentes; não cria novo bloqueio |
| RecoveryRequested / RecoveryCompleted / RecoveryRefused | Solicitação genérica, confirmação ou recusa de recuperação; não revela existência da conta |
| CredentialChanged / CredentialRevoked | Troca de senha ou emissão/conclusão de acesso temporário com invalidação das credenciais anteriores |
| SessionRevoked | Revogação confirmada pelo armazenamento, mesmo quando o cookie de outra sessão deve ser preservado |
| TemporaryConflict | session_validation_busy ou session_refresh_conflict |
| SessionExpired / SessionRejected | Expiração real ou sessão inválida/contexto rejeitado |
| InfrastructureFailure | Falha de infraestrutura; não equivale a senha errada |
| SuspiciousPattern | Indício observacional; nunca autoriza, bloqueia ou envia notificação |

Respostas 401 de endpoints protegidos com bearer ausente/inválido são registradas no estágio authorization, sem inferir expiração da sessão. SessionValidated é uma amostra interna para o denominador da detecção; não é gravada em arquivo. Restauração sem cookie antes do login é separada como bootstrap e não infla erros de sessões autenticadas. Uma requisição emite um resultado; descoberta e autenticação têm estágios distintos.

## Regras e configuração

Seção SecurityObservation (ou variáveis SecurityObservation__Nome). Defaults centralizados em SecurityObservationOptions, validados no startup:

- WindowMinutes=10; MaximumWindowEvents=10000; QueueCapacity=4096.
- account_failures: AccountFailureThreshold=10 recusas da mesma chave na janela.
- multiple_accounts: MultipleAccountFailuresThreshold=50 recusas, MultipleAccountsThreshold=20 contas distintas e AuthenticationFailureRatio=0.9. Correlação global da instância, sem atribuição a IP nem inferência de atacante individual.
- session_error_growth: SessionMinimumSamples=100, SessionErrorThreshold=20 e SessionErrorRatio=0.2.
- AlertCooldownMinutes=10 por regra/conta; janela e estado têm limites de memória.

Somente invalid_credential participa das regras de conta. Sucessos, entrada malformada, falha de infraestrutura, autorização e throttling não contam como senha errada. Volume/diversidade/razão reduzem alertas em redes compartilhadas. Limiares não são prova de ataque e precisam ser ajustados com observação operacional, especialmente no início de expediente e após incidentes.

## Privacidade, retenção e indisponibilidade

Eventos contêm somente timestamp, enum de evento, operação/motivo de lista permitida, correlação de requisição do servidor, clínica validada quando disponível e pseudônimo opcional. Nenhum IP, User-Agent, email, usuário, senha, PIN, corpo, cookie, Authorization, token ou identificador de sessão é copiado para eles. Textos são limitados/sanitizados e metadados extras não são apresentados.

A correlação de conta usa HMAC-SHA256 com chave aleatória própria de 32 bytes por processo e namespace diário. Não usa hash simples nem segredo de autenticação. A chave não é persistida; mudança de dia ou reinício rompe correlação. A representação é pseudonimizada, não anonimizada.

Arquivo hemodinks-security-*.json: rotação diária/10 MiB, no máximo 30 arquivos e retenção configurada de 30 dias. A limpeza do sink ocorre na rotação; a consulta exclui eventos com mais de 30 dias e limita a leitura aos últimos 10000 eventos do escopo. Permissões do diretório/deploy e acesso aos sinks externos continuam os existentes. Retenção de console/OTLP/New Relic depende do destino já configurado e deve ser administrada nele; esta alteração não muda essa configuração.

Fila cheia descarta o evento sem esperar; erro do emissor/consumidor não altera o resultado da autenticação. Métricas hemodinks.security.observation.events (somente tag event de catálogo fixo) e hemodinks.security.observation.dropped não incluem identificadores. Persistência é best effort, não trilha transacional. Desligamento/reinício, disco indisponível ou fila cheia podem perder eventos; erros internos dos sinks Serilog também seguem a política já existente desses sinks.

Detecção e arquivos são locais a cada réplica. Não há correlação histórica ou entre réplicas; no Render sem disco persistente, arquivos podem desaparecer em redeploy. Console/OTLP existentes podem preservar a emissão conforme sua configuração. Nenhuma infraestrutura paga, notificação externa ou mudança nos limites de autenticação foi introduzida.

## Validação de custo

SecurityObservationTests mede HMAC + sanitização + emissão, inclusive fila saturada, com 20000 amostras e referência do hash de armazenamento existente. SecurityObservationEndpointTests compara 4 logins HTTP alternados com emissão ativa/desativada, após aquecimento, em banco de testes em memória. A amostra HTTP é exploratória e não representa latência do Render/SQL Server nem permite prometer um percentual de overhead de produção. Resultados medidos e testes finais são registrados na issue.
