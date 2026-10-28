# API #197 - SQL runtime, rede e rollback

Complexidade alta, risco alto. Escopo: identidade SQL, SchemaGuard, workflows,
scripts de privilegios e procedimento de rede. Dependencias: Azure SQL, Render
de homologacao, Container Apps, runner de migrations e recuperacao #184.
Frontend e regras de ClinicaId nao mudam. Revisao independente: Security (high;
modelo efetivo nao exposto). Nenhum recurso foi provisionado ou modificado.

## Antes: evidencia somente leitura em 28/10/2026

Servidor `gm-tech-solution`, RG `rg-hemodinks-prod`, banco `HemodinksDB`:

| Verificacao | Resultado |
| --- | --- |
| Runtime API | Comparacao em memoria da conexao referenciada com `administratorLogin`: **igual**; senha configurada; identidade Azure da API `None`. Confirmado as 12:39:32 UTC, sem abrir sessao SQL. |
| Rede | `publicNetworkAccess=Enabled`, TLS minimo `1.2`, 14 regras de firewall, uma `0.0.0.0`/`0.0.0.0` (Azure Services), nenhuma regra integral `0.0.0.0`-`255.255.255.255`. |
| Rotas privadas | Zero VNet rules e zero private endpoint connections (consulta ARM). |
| Banco | `Paused`; nao se abriu conexao para acorda-lo. |
| Workflows anteriores | Mesmo secret para runtime/SchemaGuard e bundle em cada ambiente. |

Consultas: `az account show`, `az containerapp show`, resolucao da referencia
de conexao com `az containerapp secret list --show-values` exclusivamente em
memoria, `az sql server show`, `az sql server firewall-rule list`,
`az sql server vnet-rule list`, ARM GET `privateEndpointConnections` e
`az sql db show`. Apenas booleanos/metadados minimizados foram registrados.
Nao foram lidas tabelas clinicas. Membership/grants SQL, identidade real do
runner e RBAC das identidades de deploy nao foram comprovados por essa leitura
ARM; verificar separadamente antes de uma mudanca operacional.

## Depois: controles implementados no repositorio

| Ambiente | Runtime e verificacao de schema | Bundle de migrations |
| --- | --- | --- |
| homologation | `HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING` | `HOMOLOGATION_SQL_MIGRATION_CONNECTION_STRING` |
| production | `AZURE_SQL_RUNTIME_CONNECTION_STRING` | `AZURE_SQL_MIGRATION_CONNECTION_STRING` |

Nao ha fallback aos secrets antigos. O preflight `SchemaGuard
--validate-connections` compara servidor e banco e exige identidades explicitas
distintas, sem conectar nem imprimir valores. Use o mesmo formato de servidor
nas duas conexoes (incluindo `tcp:` e porta). Conexoes integradas/identidades
implicitas sao recusadas; uma futura identidade gerenciada sem User ID explicito
exige adaptar esse preflight com identificacao verificavel do principal.

O SchemaGuard passa a auditar os privilegios efetivos do runtime antes de ler o
historico. Runtime administrador, role extra, DDL, impersonacao, ownership,
grant option e grants de escrita no historico (inclusive colunas) bloqueiam o
deploy. Os codigos existentes 0/10/2/3 continuam: schema atual, pendencias
conhecidas, falha de verificacao e historico inconsistente. O fluxo Production
tambem recebe o verificador empacotado e recusa falhas antes do bundle.

`ConfigureSqlRuntimeRole.sql` exige banco confirmado e usuario dedicado ja
criado; concede SELECT/INSERT/UPDATE/DELETE apenas em `dbo`, com escrita negada
em `__EFMigrationsHistory`. Nao cria senha nem remove grants de usuarios antigos.
Recusa usuario privilegiado, login mapeado ao servidor e role preexistente
contaminada. O auditor usa a mesma SQL embarcada no SchemaGuard.
Um usuario WITHOUT LOGIN serve ao ensaio sintetico; o runtime real deve ser
usuario contido (`DATABASE` ou `EXTERNAL`), sem acesso ao servidor/master.

A role permite DML nas tabelas atuais e futuras de dbo. Esse schema deve conter
somente dados da aplicacao; ela nao substitui filtros/validacoes de ClinicaId.
Se uma migration recriar a tabela de historico, reaplicar o script como DBA e
revalidar os DENY antes de liberar o runtime.

## Homologacao: sequencia operacional pendente

1. Confirmar banco/servidor de homologacao distinto da producao, backup e janela;
   inventariar todos os escritores (API, workers, manutencao, integracoes e
   runners). Guardar configuracao anterior em cofre, sem incluir valores em logs.
2. DBA cria usuario contido exclusivo para runtime, preferindo Entra quando o
   host suportar. Nao reutilizar administrador, migrator ou usuario de producao.
   Criar identidade de migration separada, sem Owner/Contributor de subscription
   por conveniencia. Azure RBAC e permissoes SQL sao planos diferentes.
3. Para migrator, conceder somente DDL/DML exigidos pelo SQL revisado no banco
   destino e durante a janela. Revisar especialmente full-text catalog/index,
   constraints e historico EF; nao presumir que db_ddladmin sozinho basta nem
   conceder db_owner automaticamente. Nenhuma dessas permissoes vai ao runtime.
   Inventariar grants/roles/ownership e expirar acesso elevado apos a execucao.
4. Aplicar migrations de bootstrap com a identidade de migration se o banco for
   novo. Como DBA, em sessao conectada ao banco correto, executar:

   ```sql
   EXEC sys.sp_set_session_context @key=N'Runtime197ExpectedDatabase', @value=N'<banco-homologacao>';
   EXEC sys.sp_set_session_context @key=N'Runtime197User', @value=N'<usuario-runtime>';
   -- Executar scripts/ConfigureSqlRuntimeRole.sql nesta mesma sessao.
   ```

5. Abrir nova sessao como runtime e executar `AuditSqlRuntimePermissions.sql`;
   exigir `RuntimePrivilegeAuditPassed=1`. Validar tambem que nao conecta a master,
   producao ou outros bancos. O ensaio LocalDB por EXECUTE AS nao prova esse login.
6. Cadastrar os novos secrets do environment homologation e instalar apenas a
   conexao runtime no Render (`ConnectionStrings__DefaultConnection`). Usar
   Encrypt=True e TrustServerCertificate=False. Manter migrations, seeds e
   manutencao no startup desligados. Nao executar workflow publish nesta tarefa.
7. Na janela aprovada, rodar diagnose-sql e depois publish, confirmar SchemaGuard,
   migration e login/CRUD com dados sinteticos. Criar recurso da Clinica A e
   provar que Clinica B nao o le/altera/exclui; verificar readiness e workers.
8. Repetir negativos SQL (DDL, roles, history), origem de rede nao autorizada e
   rollback. Registrar UTC, resultado e duracao, sem PII/credenciais. Somente
   depois propor a mesma sequencia para producao em mudanca aprovada.

## Rede: escolha e plano de mudanca

Recomendacao: Private Endpoint com DNS privado e conectividade da API/worker e
runner na VNet. Antes de desabilitar rede publica, validar DNS do hostname SQL
para IP privado, conexao TLS de cada origem aprovada e rota do runner. Render
publico nao ganha acesso a Private Endpoint Azure por configurar uma variavel;
exige conectividade privada apropriada ou permanecer em caminho publico restrito.

Alternativa inicial: endpoint publico com IPs de saida fixos e allowlist minima.
Inventariar egress do Container Apps, Render e runner. Runners GitHub hospedados
tem saida variavel; preferir runner com rota privada/egress fixo. Nao liberar toda
a Azure nem grandes ranges de GitHub para contornar essa limitacao.

Na janela: adicionar somente origens aprovadas; testar cada escritor; remover
a excecao Azure Services e regras obsoletas uma a uma. Com caminho privado
validado, desabilitar publicNetworkAccess. Exigir negativo de conexao de origem
nao autorizada e positivo de todas as origens autorizadas antes/depois. Guardar
inventario anterior em cofre e registrar somente contagens/resultado no relatorio.
Nenhum comando de alteracao de firewall/PE foi executado nesta tarefa.

Private Endpoint, DNS, VNet/runner, NAT para egress fixo e trafego podem ter custo
recorrente. FinOps deve cotar regiao/SKU/topologia e aprovar antes de criar recursos;
nao ha estimativa numerica validada ou autorizacao de despesa nesta execucao.

## Rollback e #184

Se readiness, login, workers ou negativos falharem, parar cutover e restaurar a
ultima conexao **restrita e validada**. Manter credencial anterior em cofre apenas
durante a janela; retornar ao administrador observado exige decisao explicita do
responsavel de seguranca, prazo e registro do risco. Nunca elevar a role runtime
para db_owner como rollback automatico. Reverter a revisao/trafego conforme o
runbook Blue/Green nao reverte grants, secrets, firewall ou migrations.

Falha de rede: restaurar somente as regras/rotas anteriores necessarias ao
caminho aprovado, verificar DNS/TTL/pools e todos escritores; nao religar Azure
Services nem rede publica ampla automaticamente. Remover acesso de migration e
credenciais substituidas somente apos janela estavel, verificando consumidores.
Schema alterado exige forward fix ou recuperacao revisada, nao Down automatico.

Usuario contido e permissoes podem acompanhar PITR. PE/firewall sao do servidor:
menor privilegio nao garante quarentena de uma copia no mesmo servidor antes de
Online. O impedimento da #184 continua ate comprovar isolamento pre-Online.

## Validacao e status

- `dotnet test ... -c Release --filter` selecionando SqlRuntimePermissions,
  SqlDeploymentPolicy, AppDbContextTenancy, PlatformDbContextIsolation,
  DatabaseStartupPolicy, PatientUserTenantSqlServer e SchemaHistoryGuard:
  **37 aprovados, zero falhas/ignorados**. LocalDB descartavel, com
  `HEMODINKS_TEST_LOCALDB=1` e conexao SQL externa removida do processo.
  TRX: `out/issue197/sql197-targeted.trx` (ignorado pelo Git).
- `python -B -m unittest discover -s scripts/tests -q`: **120 aprovados**.
  A primeira tentativa revelou jq fora do PATH; a execucao final usou o jq
  ja existente em `out/workflow-test-tools`, sem alterar dependencias do projeto.
- `dotnet build HemodinksAPI.slnx -c Release --no-restore`: sucesso, dois avisos
  IDE0005 existentes em testes de identidade/senha, zero erros.
- `dotnet test HemodinksAPI.slnx -c Release --no-build`: **execucao ampla
  incompleta/anulada**, 958 aprovados, uma falha e 12 ignorados apos 6 min 21 s.
  Falha: `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`,
  `SQL Server Full-Text Search nao esta instalado nesta instancia.` Os 12 cenarios
  LoginBrowserTests exigem `HEMODINKS_E2E_FRONT_PATH`. A execucao deixou de progredir
  e seu testhost foi interrompido; nao se atribuiu a causa da parada ao erro FTS.
  TRX parcial: `out/issue197/sql197-full.trx`. Modulo de lembretes nao foi alterado.
- `dotnet tool run dotnet-ef migrations has-pending-model-changes --context
  AppDbContext --project HemodinksAPI.Infrastructure --startup-project
  HemodinksAPI.Infrastructure --configuration Release --no-build`: sem mudancas.
- `pwsh ./scripts/Test-Migrations.ps1 -NoBuild -Configuration Release`: sucesso,
  73 migrations auditadas, com os avisos historicos de revisao operacional.
- `dotnet publish tools/Hemodinks.SchemaGuard/Hemodinks.SchemaGuard.csproj
  --configuration Release --runtime linux-x64 --self-contained true`: sucesso;
  pacote em `out/issue197/schema-guard-linux`. CLI com identidades sinteticas:
  positivo retorna 0, banco divergente retorna 2, sem acesso SQL ou disclosure.
- YAML parse, Actionlint 1.7.12 (quatro workflows, shellcheck/pyflakes desabilitados)
  e `git diff --check`: aprovados. Release oficial do Actionlint verificada por
  SHA256 e mantida apenas em `out/`. Security revisou o diff sem novos bloqueios
  de codigo; nao executou testes nem editou arquivos.

O antes/depois acima compara configuracao real observada com controles locais;
nao afirma que a infraestrutura recebeu esses controles. Homologacao remota,
negativos de rede, RBAC efetivo, custo, troca de credenciais e producao continuam
pendentes da janela/autorizacao operacional e dos destinos confirmados. Nao
encerrar #197 como endurecimento operacional concluido apenas com CI verde.

Referencias oficiais: [menor privilegio e usuarios contidos](https://learn.microsoft.com/en-us/azure/azure-sql/database/secure-database?view=azuresql),
[controles de rede](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql),
[Private Endpoint](https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql),
[DENY e grants de coluna](https://learn.microsoft.com/en-us/sql/t-sql/statements/deny-object-permissions-transact-sql?view=sql-server-ver17).
