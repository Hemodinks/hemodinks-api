# API #198 - revalidacao do ensaio PITR em 28/10/2026

## Restricao financeira obrigatoria - escopo vigente

**Orcamento autorizado para novos recursos Azure: R$ 0,00. Sem excecoes.**
Esta instrucao expressa do usuario substitui qualquer autorizacao historica de
restore, criacao/descarte de destino ou mudanca operacional registrada abaixo
ou no runbook #184. A issue #198 deve permanecer **aberta**.

Proibido criar outro banco Azure, executar PITR/restore real, provisionar
infraestrutura, ativar servicos pagos, aumentar capacidade ou executar operacao
que gere despesa adicional. Mesmo isolamento tecnicamente comprovado nao
autoriza restore. Nao promover trafego, reiniciar revisoes, alterar secrets,
rotacionar credenciais ou modificar a infraestrutura sob esta issue.

Permitido somente: consultas sem mutacao ou despesa adicional, analise dos
backups existentes, testes sinteticos locais e documentacao/planejamento.
Nao abrir novas conexoes SQL Azure nem chamar endpoints que possam acordar
compute/replicas. Nenhuma nova verificacao Azure foi executada nesta rodada;
as fotografias anteriores abaixo sao evidencias historicas, nao estado atual.

| Pendencia | Status obrigatorio |
| --- | --- |
| Restore PITR real em destino temporario | BLOQUEADO POR RESTRICAO FINANCEIRA; nao iniciado. |
| Medicao real de RPO/RTO | BLOQUEADA POR RESTRICAO FINANCEIRA; nao medida, n=0. |
| Criacao, validacao Azure e descarte de destino | Nao executados; nenhum destino criado por esta issue. |
| Isolamento operacional | Planejamento apenas; planos anteriores nao autorizam execucao. |

Uma duracao de teste local, meta de 15 min/4 h ou timestamp solicitado nao sera
apresentado como medicao real de RPO/RTO. O plano tecnico e as consultas ja
registradas sao preservados para rastreabilidade, sem autorizar sua execucao.

Validacao local desta restricao: `dotnet test` com `--configuration Release
--no-build --no-restore` e filtro de AppDbContextTenancyTests,
PlatformDbContextIsolationTests e
RestoreDatabaseIntegrityTests.CheckerManifest_CoversCurrentForeignKeysAndChecks:
**10 aprovados, zero falhas/ignorados**, 2 s. Testes de tenant em memoria e
inspecao local do modelo/manifesto, sem SQL Azure ou novo banco persistente.
Variaveis de conexao SQL externa/LocalDB foram removidas do processo.
TRX: out/issue198/pitr198-zero-budget.trx (ignorado pelo Git).
`git diff --check` sem erros. Escopo desta alteracao e documental, risco baixo;
nenhum novo subagente foi acionado e nenhuma regra de negocio foi alterada.

## Atualizacao operacional - consultas de 20:27 a 20:31 UTC

### Esclarecimento e proposta delimitada para aprovacao

O usuario identificou o outro sistema como ESCOLA, informando que esta pausado
no Render e na Azure. O inventario nominal Azure encontrou somente o recurso
SQL gm-tech-solution/ESCOLA_API, em estado Paused; nao encontrou Web App ou
Container App com nome ESCOLA. Isso nao prova ausencia de compute com outro
nome. Pausa SQL nao e suspensao da aplicacao: Azure SQL serverless pode retomar
ao receber conexao. Nao foi aberta sessao no banco ESCOLA.

A suspensao Render ainda precisa ser confirmada. O conector lista somente o
workspace GMARCONE TECH SOLUTIONS, mas nao tem workspace selecionado; aguardando
confirmacao explicita do usuario antes de ler o servico, conforme seu contrato.
Nao foram lidos env vars ou logs do ESCOLA nem alterado seu estado.

Plano historico, **nao autorizado sob o escopo financeiro vigente**, limitado a ACA hemodinks-api-prod,
RG rg-hemodinks-prod, mantendo a imagem atual:

1. Copiar somente em memoria o valor restrito ja validado de
   connectionstrings-runtime-prod-v2 para o secret local legado
   connectionstrings-defaultconnection. Isso impede que as revisoes antigas
   voltem a receber administrador ao iniciar; nenhum login/senha SQL e alterado.
2. Promover hemodinks-api-prod--p197sqlruntimev2 para 100% do trafego e mover a
   label green para ela, apos revalidar imagem, flags, health e auditoria SQL.
3. Desativar hemodinks-api-prod--sha-96d25dd53c; aguardar estado inativo e zero
   replicas/processos. Confirmar que as outras revisoes administrativas seguem
   inativas e que todas as referencias SQL do app resolvem para runtime restrito.
4. Validar /livez e /healthz no endpoint publico e da candidata; conferir trafego,
   labels e identidade efetiva. Login/CRUD produtivo nao integra esta autorizacao.

Rollback de trafego somente para 96d25dd53c apos o secret legado estar restrito,
reinicializacao/ativacao controlada e health aprovados; nao restaurar conexao
administrativa automaticamente. Se os checks falharem, interromper e registrar
o estado para decisao do operador. Os smokes atuais e auditoria SQL nao substituem
CRUD/E2E de negocio; testes com escrita exigem autorizacao especifica.

Fora desse escopo: administrador SQL compartilhado, ESCOLA, firewall, rede,
rotacao de senhas, migrations, novo deploy de codigo e restore PITR. A proposta
deve receber autorizacao explicita por ser mudanca de producao, mesmo sem merge
na main. A execucao da #198 permanece condicionada a comprovar a suspensao de
outros consumidores durante todo o ensaio e sua impossibilidade de retomar
acesso administrativo antes de o destino ser excluido.

Esta secao substitui a conclusao anterior de que a configuracao desejada da API
representava necessariamente o runtime em uso. A verificacao foi ampliada para
as referencias SQL de **todas as revisoes e da rota com 100% do trafego**.

| Revisao | Trafego/estado | Referencia SQL | Auditoria |
| --- | --- | --- | --- |
| hemodinks-api-prod--sha-96d25dd53c | Ativa, 100% | connectionstrings-defaultconnection | Usuario coincide com administrador; auditoria SQL efetiva recusou com 51972. |
| hemodinks-api-prod--p197sqlruntimev2 | Ativa, sem peso de trafego | connectionstrings-runtime-prod-v2 | Usuario nao administrador; auditoria efetiva aprovada em 20:30:40Z. |
| Tres revisoes anteriores | Inativas; uma ainda com label blue/peso zero | connectionstrings-defaultconnection | Referencia ainda resolve para administrador. |

A revisao candidata esta Healthy no ARM e usa a mesma referencia de imagem da
revisao com 100% do trafego; isso nao e uma verificacao independente de digest.
Flags de migrations, manutencao, seeds e processador de lembretes no startup
estao false; SchemaManagedByDeployment=true. Nenhum trafego, label, secret ou
estado de revisao foi alterado nesta rodada.
Smoke somente leitura da revisao candidata: /healthz=200 em 20:31:45Z e
/livez=200 em 20:32:00Z. A primeira tentativa de /livez expirou; uma nova
tentativa apos /healthz responder concluiu com 200, sem atribuir causa ao timeout.

SQL de metadata, usando exclusivamente as conexoes existentes e sem ler dados
clinicos, confirmou para o candidato authentication_type=DATABASE,
create_date=2026-10-28T18:36:23.363 e modify_date=2026-10-28T18:44:45.960.
O verificador embarcado AuditSqlRuntimePermissions passou para o candidato e
falhou para a conexao realmente usada pelo trafego. Uma ferramenta transitoria
em out/issue198/readiness foi compilada com sucesso (avisos IDE0005 existentes)
para executar essas leituras e emitir apenas booleanos, datas e codigo SQL.
Nao ha conexoes ou credenciais gravadas nesse diretorio.

O usuario confirmou nesta conversa que os usuarios/senhas produtivos sao novos,
criados hoje sem reutilizacao. Tambem confirmou que **outra aplicacao ainda usa
o administrador desse servidor**. Nome e hospedagem foram solicitados; nao se
presumiu ESCOLA nem se alterou outro sistema. Essa confirmacao mantem o bloqueio
pre-Online mesmo depois de uma eventual troca de trafego do HemoDinks.

Os secrets GitHub production separados foram cadastrados em 19:32:12Z e
19:33:21Z. O secret legado permanece cadastrado; sua mera existencia operacional
nao equivale a uma aplicacao executando com administrador. O impedimento atual
e concreto: a rota de 100% ainda o usa, assim como outro consumidor confirmado.

Proxima mudanca a preparar: retirar o administrador de todas as revisoes
executaveis do HemoDinks, validar a revisao restrita antes da troca e inventariar
a outra aplicacao. Sem autorizacao especifica, nao promover trafego, substituir
secrets legados, desativar revisoes, rotacionar o administrador compartilhado ou
alterar o outro sistema. Os testes de login/CRUD produtivos tambem escrevem e
nao foram executados nesta auditoria de leitura.

Restore permanece n=0; RPO/RTO reais nao medidos. Nao se criou destino nem se
executou descarte. A revisao Security (high) confirmou esses dois bloqueios.

## Registro anterior - 17:34 UTC

**Status: homologacao validada; PITR de producao bloqueado antes do restore.**
Complexidade alta, risco critico. Origem prevista pela referencia #184:
`HemodinksDB`, servidor `gm-tech-solution`, RG `rg-hemodinks-prod`.
Nao foi trocada a origem do ensaio por homologacao. Nenhuma mutacao Azure,
novo banco, acesso a registros clinicos, deploy ou despesa foi executado aqui.

## Progresso comprovado na homologacao

Os secrets HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING e
HOMOLOGATION_SQL_MIGRATION_CONNECTION_STRING foram cadastrados em 28/10,
respectivamente 14:49:55Z e 14:50:42Z; valores nao foram consultados no GitHub.

| Execucao existente | Evidencia consultada |
| --- | --- |
| [diagnose](https://github.com/Hemodinks/hemodinks-api/actions/runs/40710892639) | Configuracao Render validada; sem etapa SQL/deploy. |
| [diagnose-sql](https://github.com/Hemodinks/hemodinks-api/actions/runs/40711414923) | Configuracao e verificacao SQL concluidas com sucesso. |
| [publish](https://github.com/Hemodinks/hemodinks-api/actions/runs/40711916228) | Preflight de identidades, auditoria efetiva de privilegios e historico SQL aprovados; deploy Render concluido. Sem migrations pendentes, bundle ignorado. |

Esses runs usaram `fbb5142`, que inclui a #197. Logs do job 122900441348
confirmam preflight aprovado e historico compativel entre 15:04:55Z e 15:04:58Z.
As execucoes foram feitas anteriormente pelo usuario; apenas seus resultados
foram consultados nesta rodada. Nao comprovam alteracoes operacionais na producao.

## Preflight atual da producao

Consulta sanitizada concluida em **2026-10-28T17:34:54Z (14:34:54 BRT)**:

| Item | Resultado |
| --- | --- |
| Conexao da API | Configurada, User ID preenchido, servidor e banco correspondem a origem prevista. Usuario SQL coincide com administratorLogin do servidor. Saida restrita a booleanos. |
| Identidade Azure | None. |
| Rede | publicNetworkAccess=Enabled; 15 regras, uma AzureServices; zero private endpoint connections. |
| Banco | Paused, redundancia Local, SKU GP_S_Gen5. |
| Retencao | 7 dias; diferencial 12 horas. |
| Limite inferior PITR | earliestRestoreDate=2026-10-21T17:35:55.883895+00:00. Fotografia movel, nao e limite superior nem ultimo commit recuperavel. |
| Revisao ativa | hemodinks-api-prod--sha-96d25dd53c; label green, peso 100. Tres revisoes anteriores inativas; label blue com peso zero. |
| Secret da API | Referencia connectionstrings-defaultconnection. Comparacao foi feita em memoria, sem registrar valor, usuario ou senha. |
| GitHub production | Listagem de nomes mostra somente AZURE_SQL_CONNECTION_STRING como secret desse environment. Novos secrets separados nao aparecem nessa listagem; escopos repository/organization nao foram auditados. |

Inventario adicional do RG: um Container App (a API), zero Container App Jobs,
zero Web Apps e zero Function Apps retornados. Isso nao prova ausencia de
consumidores em outros RGs, Render, integracoes externas ou sessoes existentes.
Nao se concluiu o inventario de compartilhamento do administrador, inclusive
aplicacoes de outros bancos do servidor. Nao ha plano aprovado de rotacao desse
administrador compartilhado nem de interrupcao de seus consumidores.

## Impedimento e proxima mudanca necessaria

A [documentacao oficial do Azure](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql#point-in-time-restore-considerations)
restringe PITR ao mesmo servidor. Com o runtime produtivo portando a credencial
administrativa, novo nome de banco, Private Endpoint ou revogacao apos Online
nao comprovam quarentena pre-Online. Usuarios contidos tambem podem acompanhar
o snapshot; usuario restrito na origem, sozinho, nao resolve esse requisito.

Para retomar: concluir inventario de consumidores/credenciais e preparar mudanca
operacional da #197 com lista fechada de recursos, usuarios, revisoes, impacto,
testes e rollback. Trocar consumidores para identidade restrita e retirar seu
acesso ao administrador exige autorizacao especifica de producao. Rotacao do
administrador compartilhado/rede so pode ser aprovada depois de conhecer todos
os consumidores afetados. Nao solicitar nem executar autorizacao ampla para
alterar ESCOLA ou outros sistemas por inferencia.

Depois, provar que nenhuma credencial de consumidor produtivo funciona no
snapshot escolhido desde o primeiro Online; confirmar destino, ponto, SKU,
identidade temporaria e descarte. Seguir o [ensaio da #184](issue-184-backup-recovery.md)
para restore, integridade, negativos A/B, host privado e descarte. Nenhuma etapa
de restore/cutover foi executada. O pedido atual preserva a restricao da #198
de nao fazer mudancas diretas na producao.

## Medidas e verificacao

- Restore: n=0. RPO/RTO reais, tempo de restore, descarte e custo real:
  **nao medidos**, sem preencher zero como duracao ou estimativa como evidencia.
- RPO requer ultimo commit valido recuperado comprovado; o ponto solicitado,
  earliestRestoreDate e o intervalo diferencial nao substituem essa evidencia.
- RTO tecnico inclui integridade e aplicacao isolada pronta, alem de Online.
- Validacoes atuais: tres runs reais de homologacao consultados, leituras ARM
  e configuracao sanitizada da API; nenhum teste HTTP/SQL na copia inexistente.
- Nao se repetiram suites locais: somente este registro foi criado; os testes
  sinteticos anteriores nao seriam evidencia adicional de PITR real.
- Security (high; modelo efetivo nao exposto) reavaliou em modo somente leitura
  e confirmou o bloqueio de isolamento, sem editar ou executar testes.
- `git diff --check` e verificacao de formato do arquivo: sem erros.
  Nenhum commit, merge, PR ou deploy nesta rodada.
