# API #198 - ensaio PITR: preflight e impedimento operacional

## Status em 28/10/2026

**Restore nao iniciado: pre-requisito de isolamento nao atendido.**
Complexidade alta, risco critico (copia de dados de producao). Escopo executado:
leituras de configuracao Azure/backup, revisao Security e testes sinteticos de
integridade/tenant. Dependencias: #197 operacional, Azure SQL, identidade de
validacao, quarentena pre-Online e runbook [#184](issue-184-backup-recovery.md).
Nenhuma alteracao em API, regra de negocio, producao ou recurso pago.

## Evidencia atual, sem dados clinicos

Origem: `HemodinksDB`, servidor `gm-tech-solution`, RG `rg-hemodinks-prod`.

| Verificacao | Resultado observado |
| --- | --- |
| Identidade runtime | Em 2026-10-28T13:20:02Z (10:20:02 BRT), comparacao em memoria do usuario da conexao com administratorLogin retornou true. Identidade gerenciada da API: None. |
| Banco e backup | Paused; redundancia Local; STR 7 dias, diferencial 12 horas. |
| Limite inferior publicado | earliestRestoreDate=2026-10-21T13:19:54.000604+00:00. Fotografia movel, nao comprova limite superior nem ultimo commit recuperavel. |
| Rede | publicNetworkAccess=Enabled; 14 regras de firewall, uma AzureServices 0.0.0.0/0.0.0.0; zero private endpoint connections. |
| Destino | Nenhum criado ou aprovado tecnicamente neste ensaio. HemodinksDBConfirmation existente nao foi usado nem considerado descartavel. |
| Dependencia #197 | Issue ainda aberta. Este checkout developer estava limpo e nao continha os artefatos locais descritos na etapa anterior da conversa; a configuracao Azure comprova que o runtime segue administrador. |

Consultas usadas: `az sql db show`, `az sql db str-policy show`,
`az sql server show`, `az sql server firewall-rule list`, ARM GET de
privateEndpointConnections, `az sql db list` e configuracao do Container App.
A referencia de conexao foi resolvida exclusivamente em memoria para produzir
um booleano, sem imprimir conexao, usuario, senha ou secrets. Nao se abriu sessao
SQL no Azure nem se consultaram registros clinicos. O banco origem permaneceu
pausado nas consultas; nao foi submetido restore ou descarte.

## Por que o ensaio nao pode prosseguir agora

O PITR de Azure SQL Database so e suportado no mesmo servidor logico; nao ha
PITR direto para outro servidor/subscription. Uma copia com nome novo nesse
servidor nao estabelece quarentena contra o administrador usado pelo runtime.
Revogar usuarios depois do primeiro Online deixa uma janela de acesso e nao
atende o runbook #184. Um Private Endpoint tambem nao separa, por si, bancos
acessiveis pela mesma identidade nesse servidor.

Fonte oficial revalidada:
[recuperacao Azure SQL, consideracoes PITR](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql#point-in-time-restore-considerations).
Nao substituir PITR por copy/export nem declarar outro metodo equivalente ao
ensaio solicitado. A autorizacao de criar/excluir destino temporario registrada
no runbook #184 nao elimina o requisito de isolamento previo.

## Retomada objetiva

1. Concluir a #197 operacionalmente, com identidades de runtime e migration
   separadas e rede aprovada, sem manter administrador nos consumidores.
2. Comprovar que as identidades atuais e historicas (API, revisoes, workers,
   integracoes, operadores e runner) nao conseguem autenticar/autorizar na copia
   desde o primeiro Online. Usuario contido e grants podem acompanhar o backup;
   menor privilegio no banco origem, isoladamente, nao basta.
3. Confirmar destino novo `restore184-<UTC>-<id>`, ponto restauravel, identidade
   temporaria de validacao, SKU, tratamento de dados, custo e prazo de descarte.
   Nao reutilizar banco de homologacao nem alterar controles produtivos sob #198.
4. Executar os oito passos do runbook #184: restore cronometrado, validacao de
   schema/constraints/ClinicaId, CHECKDB quando aplicavel, negativos de acesso,
   host privado sem integracoes externas e E2E. SchemaGuard e verificador SQL
   nao substituem os testes de isolamento e nao autorizam migrations na copia.
5. Registrar resultados minimizados e resource ID exato do destino; excluir
   somente esse destino apos revisao, confirmar ausencia e revogar acesso
   temporario. Nao encerrar #198 antes da evidencia de restore e descarte reais.

## Medicao a preencher somente no ensaio real

| Medida | Estado atual e definicao |
| --- | --- |
| Amostras de restore | n=0; nenhuma requisicao submetida. |
| Restore puro | Nao medido. Instante Online menos submissao do restore. |
| RTO tecnico do ensaio | Nao medido. Fim da validacao/host isolado pronto menos inicio do incidente simulado; registrar tambem tempo de decisao. Nao equivale a cutover produtivo. |
| RPO efetivo | Nao medido. Instante do incidente menos ultimo commit valido comprovadamente recuperado, com evidencia de transacao minimizada e autorizada. Ponto solicitado ou earliestRestoreDate nao provam esse commit. |
| Descarte e custo real | Nao executado/nao medido; nenhum destino faturavel criado. |

Se a evidencia necessaria ao RPO exigir novo marcador/transacao na origem,
isso exige aprovacao especifica fora da restricao de nao alterar producao.
Sem essa evidencia, reportar apenas o ponto solicitado e limites observados,
sem apresenta-los como RPO real. Metas de 15 min/4 h da #184 sao propostas,
nao resultados medidos ou SLA aprovado.

## Verificacao desta rodada

- `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --configuration
  Release --filter 'FullyQualifiedName~RestoreDatabaseIntegrityTests|FullyQualifiedName~PatientUserTenantSqlServerTests|FullyQualifiedName~AppDbContextTenancyTests|FullyQualifiedName~PlatformDbContextIsolationTests'
  --verbosity minimal --logger 'trx;LogFileName=pitr198-readiness.trx'
  --results-directory out/issue198`: **14 aprovados, zero falhas e ignorados**,
  testes em 15 s. Processo com `HEMODINKS_TEST_LOCALDB=1` e conexao SQL externa
  removida; somente bancos locais sinteticos descartaveis. TRX no diretorio
  indicado, ignorado pelo Git. Build emitiu dois avisos IDE0005 existentes em
  testes de identidade/senha, sem erros. Nao se repetiu a suite ampla da #197.
- `git diff --check`: sem erros. Somente este arquivo foi criado; nao foram
  adicionados secrets, registros clinicos ou comandos de mutacao de producao.
- Esses testes comprovam o verificador local e regressoes de isolamento, nao
  um restore Azure, quarentena real ou medicao de recuperacao.

Security (high; modelo efetivo nao exposto) revisou os requisitos de quarentena
em modo somente leitura. Nenhum commit, PR, merge ou deploy foi executado.
