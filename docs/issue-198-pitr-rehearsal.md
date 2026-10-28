# API #198 - revalidacao do ensaio PITR em 28/10/2026

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
