# Homologacao: preparacao do login e profiler

Em 30/09/2026, CORECLR_ENABLE_PROFILING foi definido como 0 somente no servico Render srv-d8hmtje47okc738ldllg. Deploy dep-daun9dc9v7es73a7jvag ficou live. Producao e schema nao foram alterados. render.confirmation.yaml registra a configuracao para futuras sincronizacoes.

Comparacao observada: HTTP passou de 67,5 s na inicializacao anterior para 7,8 s apos desativar profiler. A primeira confirmacao de banco disponivel ocorreu aos 14,9 s. Sao amostras distintas, nao um benchmark controlado de retomada apos inatividade. Nao ha garantia de eliminar cold start do Render Free. Logs locais permanecem; instrumentacao automatica do profiler New Relic fica desabilitada. Para reverter o experimento, restaurar CORECLR_ENABLE_PROFILING=1 somente em homologacao.

Frontend: preparacao ativa por padrao apenas em hemodinks-homologacao.gestao-saude.tec.br e hemodinks-homologacao.vercel.app. VITE_LOGIN_PREPARATION_ENABLED=true/false permite override explicito no build; nao ativar em producao. VITE_WARMUP_ENABLED=false continua desativando pre-aquecimento automatico, mas nao desativa a preparacao de login quando esta estiver habilitada.

Ao abrir a pagina, o pre-aquecimento e best effort. Ao enviar login, a preparacao compartilha a requisicao pendente, aguarda sucesso HTTP 204 e somente entao envia credenciais uma vez. Sao no maximo 3 tentativas de warmup de 60 s, com intervalos de 5 s (ate aproximadamente 190 s), sem polling permanente. Falhas definitivas 4xx (exceto 408) encerram a preparacao. Cancelar aborta a requisicao e impede autenticacao tardia. Depois de falha, Entrar permite nova tentativa; senha e limpa seguindo politica existente.

Somente sucesso fica em cache por 5 minutos na aba; falha nao fica gravada. Apos expiracao, proxima acao de acesso pode aquecer novamente. Nao ha reenvio automatico de credenciais, mudanca de tenant ou autorizacao. O caminho de producao preserva o warmup anterior.

Validacao: 41 testes direcionados aprovados, build e auditoria arquitetural aprovados; 2 E2E dedicados passaram (espera/cancelamento sem enviar credenciais e falha/nova tentativa/login). Executar npm run test:e2e:homologation sequencialmente aos outros E2E, pois compartilham ferramentas Vite e diretorios de resultado.

Mudancas frontend ainda precisam ser publicadas para aparecer no dominio real. Nenhum ping permanente ou recurso pago foi criado.

Regressao do fluxo existente: os 6 E2E de login/warmup passaram na execucao sequencial (2,3 min). A execucao inicial paralela sofreu indisponibilidade do servidor de testes; nenhum seletor ou expectativa existente foi relaxado. Total: 8 E2E aprovados nas execucoes finais.

## Timeout SQL no Publish Homologation

Na execucao 36780286457, os artefatos foram preparados com sucesso e o deploy foi bloqueado em OpeningSqlConnection apos 30,8 s (SqlTimeout, -2). Essa conexao parte do runner GitHub, nao da API Render. O log nao determina se o banco estava retomando, sobrecarregado ou inacessivel pela rede.

O verificador read-only agora realiza ate 3 tentativas, com 10 s entre falhas transitorias e prazo total de 4 minutos. Cada tentativa utiliza novo DbContext; timeout de comando permanece 30 s. Nao ha repeticao de migrations nem de deploy. Divergencia de historico, credenciais invalidas, firewall explicito e permissoes continuam bloqueando sem retry. Se as tentativas se esgotarem, o processo continua retornando erro e nao publica.

Publicar o codigo na developer, aguardar CI e iniciar uma NOVA execucao do Publish Homologation em deploy-no-schema-changes. Re-run da execucao antiga usa o commit antigo. Se o SQL continuar inacessivel, investigar disponibilidade e acesso de rede do banco; nao trocar para migrate-and-deploy para contornar timeout.
