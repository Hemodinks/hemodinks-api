# API #184 — auditoria e runbook de recuperação Azure SQL

Auditoria em 23/10/2026 (consultas estenderam-se até 24/10 UTC), checkout
`aed454a`, branch `developer`. Complexidade alta, risco alto. Retomada após
`usage_limit_exceeded` na conversa “Executar issue Front #181”, já na tarefa
[API #184](https://github.com/Hemodinks/hemodinks-api/issues/184).
Escopo: auditoria Azure somente leitura, documentação e verificador local de
integridade. A continuação resolveu a referência de conexão exclusivamente em
memória, projetando apenas servidor/banco; nenhum valor de conexão ou credencial
foi registrado. Sem consultas SQL no Azure, restore, provisionamento pago, deploy
ou mudanças de produção.

Plano executado: identificar alvos e políticas; revisar dependências #173,
SchemaGuard e integridade; preparar ensaio isolado e decisões de recuperação;
revisar evidências e registrar bloqueios. Database e Security fizeram revisões
independentes sem editar arquivos na primeira etapa. Na continuação, Database
implementou o SQL e seus testes; Security revisou os três arquivos sem editar
(papéis high; modelo efetivo não exposto).

## Evidência coletada e seus limites

| Item | Resultado observado |
| --- | --- |
| API | `hemodinks-api-prod`, RG `rg-hemodinks-prod`; conexão usa `secretRef=connectionstrings-defaultconnection`. Na continuação, referência resolvida em memória sem saída de credenciais. |
| Servidor SQL inventariado | `gm-tech-solution`, RG `rg-hemodinks-prod`, região `brazilsouth`. |
| Banco configurado da API | Referência resolvida confirma `tcp:gm-tech-solution.database.windows.net,1433`, banco `HemodinksDB`. Confirmação da configuração, sem abrir conexão SQL nem consultar dados. |
| Estado/SKU | `Paused`, `GP_S_Gen5_2`, General Purpose serverless Gen5, 0,5–2 vCores, autopause 30 min, máximo 32 GiB. Nenhuma conexão foi aberta para acordá-lo. |
| PITR/STR | `retentionDays=7`, `diffBackupIntervalInHours=12`. |
| Limite inferior publicado | `earliestRestoreDate=2026-10-17T01:21:24.059984Z` na segunda consulta. É uma fotografia móvel; reler imediatamente antes do ensaio. Não foi obtido limite superior restaurável nem horário do último log backup. |
| Redundância | `currentBackupStorageRedundancy=Local`, requested `Local`; `zoneRedundant=false`. Não declarar recuperação regional por geo-backup disponível. |
| LTR | semanal/mensal/anual `PT0S`, `weekOfYear=0`: retenção longa não configurada nesta política. |
| Rede do servidor | `publicNetworkAccess=Enabled`; 14 regras, uma permite Azure Services (`0.0.0.0`). IPs não registrados. Essas regras não comprovam quarentena de um novo banco no mesmo servidor. |
| Identidade da auditoria | API ARM de permissões do servidor retorna `actions=["*"]`, sem `notActions` nos blocos correspondentes. Isso não prova autorização operacional, ausência de deny assignments, permissões SQL ou privilégios da identidade OIDC do deploy. |
| Administração Entra | Administrador `ActiveDirectory` existe; ARM GET específico na continuação confirmou `azureADOnlyAuthentication=false`. Identificadores pessoais não coletados. |
| Identidade SQL configurada da API | Comparação em memória entre o usuário da conexão e `administratorLogin` do servidor retornou `true`; usuário e senha estão configurados. Saída restrita a booleanos, sem nomes/credenciais. Não foi aberta sessão SQL. |
| Alertas no RG | Listagens metric alerts, activity-log alerts e scheduled query rules retornaram vazias. Não exclui alertas em outro RG/escopo, Azure Service Health ou monitor externo. |
| Diagnóstico/auditoria | Lista de diagnostic settings do banco vazia; audit-policy do banco e do servidor `Disabled`, Azure Monitor target `false`. |
| Rotas privadas do servidor | Listagens VNet rules e private endpoint connections retornaram vazias na continuação. Não foi alterada nenhuma regra. |
| Workers | Existe Function App `hemodinks-workers-production`; a coleção consultada de app settings não contém `ConnectionStrings__DefaultConnection`, e a de connection strings não identificou conexão SQL. Isso não comprova ausência de escritores nem resolve sua configuração em execução. |

Comandos usados: `az account show`, `az containerapp show` com projeção de
referência, `az sql server list`, `az sql db list/show`, `az sql db str-policy
show`, `az sql db ltr-policy show`, `az sql server firewall-rule list`,
`az sql server ad-admin list`, `az monitor metrics alert list`, `az monitor
activity-log alert list`, `az monitor diagnostic-settings list`, `az sql db
audit-policy show` e ARM GET de `Microsoft.Authorization/permissions` e
`Microsoft.Insights/scheduledQueryRules`. Todos com projeções sem conteúdo clínico.
A CLI de scheduled-query exigiu extensão ausente e falhou; a consulta foi
concluída por ARM GET, sem instalar extensão. Metadados de política não são
prova de cadeia íntegra, backup recente ou restauração bem-sucedida.

Na continuação (até `2026-10-24T01:32:41Z`), usaram-se também `az sql server
audit-policy show`, ARM GET de `azureADOnlyAuthentications/Default`,
`az sql server vnet-rule list`, ARM GET de `privateEndpointConnections` e
configuração das aplicações com sanitização em memória. Nova consulta do banco
publicou `earliestRestoreDate=2026-10-17T01:30:38.9258989Z`, ainda `Paused`/`Local`.

## RPO, RTO, custo e responsáveis

Metas propostas para aprovação do negócio: **RPO até 15 minutos e RTO até
4 horas para incidente lógico na mesma região**. São objetivos de planejamento,
não SLA aprovado nem capacidade medida. Perda lógica detectada tardiamente pode
exigir voltar antes da janela do RPO e reconciliar escritas válidas. Incidente
regional não tem objetivo validado com a redundância atual.

No ensaio, registrar UTC de início do incidente simulado, ponto escolhido,
submissão, banco Online, fim da integridade, API isolada pronta e liberação.
RPO observado = instante do incidente menos último estado válido recuperado;
RTO técnico = liberação validada menos início do incidente. Reportar também
restore puro e tempo de decisão separadamente; atualmente todas as durações
de recuperação e amostras de RPO são **não medidas (n=0)**.

Consulta à [Azure Retail Prices API](https://prices.azure.com/api/retail/prices)
com `serviceName=SQL Database`, `armRegionName=brazilsouth`,
`priceType=Consumption`: Gen5 General Purpose Serverless, medidor pago `vCore`,
SKU `1 vCore`: **US$ 0,99134/vCore-h**. Storage General Purpose pago:
**US$ 0,2185/GB-mês**. Não aplicar medidores Free nem suplemento Zone Redundancy.
Exemplo orçamentário de compute a 2 vCores por 4 h: **US$ 7,93072**, mais
storage (32 × 0,2185 = US$ 6,992/mês como referência mensal), backups cobrados,
rede, observabilidade e tempo adicional até descarte. Não é teto nem cotação
contratual; confirmar dimensionamento, rateio, impostos, moeda e eventual
arredondamento no desenho do ensaio com FinOps. O SKU real do destino ainda não
foi escolhido e nenhum recurso foi criado.

Designar nominalmente antes do ensaio: responsável do incidente/negócio aprova
perda aceitável e cutover; Database opera restore e valida schema; Security
aprova acesso/LGPD/reconciliação de segurança; DevOps coordena rede, escritores
e tráfego; FinOps aprova custo/expiração. Titulares e substitutos ainda pendentes.
Não ampliar permanentemente o OIDC de deploy para administrar/restaurar SQL.

## Ensaio isolado — bloqueado até os pré-requisitos

1. Confirmar com o responsável a origem efetiva da API e de cada worker, região,
   subscription e IDs de origem/destino. Usar canal restrito, sem copiar secrets.
   Aprovar destino, custo, prazo de descarte e tratamento de dados clínicos.
2. Revalidar política e selecionar ponto UTC permitido pelo serviço, anterior ao
   incidente simulado e dentro da janela. Não inferir horário do último backup
   a partir do intervalo diferencial de 12 h. Registrar ticket/aprovações.
3. Provisionar somente após autorização um **novo banco** `restore184-<UTC>-<id>`.
   PITR usa o mesmo servidor lógico; por isso o destino herda o contexto de rede
   e pode restaurar usuários/permissões contidos no snapshot. Se não houver
   quarentena verificável nesse servidor, bloquear o procedimento e desenhar
   alternativa suportada antes de criar. A quarentena deve valer **antes do
   primeiro estado Online**: revogar usuários depois do restore não elimina a
   janela em que credenciais restauradas podem funcionar. Não prometer PITR direto em outro
   servidor. Não abrir firewall amplo nem alterar produção para facilitar teste.
4. No Portal Azure SQL, abrir Restore da origem confirmada, escolher PITR,
   nome novo, ponto e SKU aprovados; conferir o resumo antes de submeter.
   Registrar correlation/request ID técnico e horários sem payload. Nunca
   substituir/excluir origem. RBAC de controle e acesso SQL são verificações
   distintas; validar identidades temporárias antes da operação.
5. Manter destino sem conexão de APIs/revisões/labels/workers produtivos.
   Antes de clientes de aplicação conectarem, o operador autorizado deve conferir
   ACLs e usuários restaurados por conexão controlada restrita à validação;
   suspender jobs, integrações, e-mail, SMS e webhooks do host de ensaio.
   Um nome diferente de banco não constitui isolamento.
6. Aguardar estado Online, registrar duração e executar a validação abaixo com
   identidade read-only restrita à cópia. Não aplicar migrations automaticamente.
7. Subir host de ensaio privado e descartável com integrações externas bloqueadas.
   Executar negativos com clínicas e usuários sintéticos. Dados reais restaurados
   continuam dados clínicos: não exportar dumps ou capturas; acesso mínimo e
   evidências agregadas em armazenamento restrito. Anonimização exige procedimento
   separado aprovado; não executar scripts históricos automaticamente.
8. Registrar resultados/erros, RPO/RTO e decisão. Descartar somente o destino
   exato com autorização e dupla conferência de resource ID; revogar acesso
   temporário e confirmar fim da cobrança. Não apagar evidência mínima aprovada.

O usuário autorizou nesta conversa a criação e posterior exclusão do banco
temporário. Não informou teto numérico de custo; a estimativa acima não é teto
contratual. Essa autorização está registrada e não precisa ser solicitada de novo
para a mesma operação, dentro de um desenho comprovadamente isolado.

Bloqueios atuais: destino/quarentena e identidade temporária de validação não validados; inventário
operacional e responsáveis ainda incompletos. Origem configurada da API confirmada;
demais escritores ainda precisam ser inventariados.
Poder técnico ARM não substitui essas aprovações. Não foi iniciado restore.

**Impedimento de isolamento observado:** regras de firewall do servidor permitem
acesso a todos os seus bancos e a regra Azure Services não se restringe à nossa
subscription ([documentação de firewall](https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure?view=azuresql)).
Usuários contidos autenticam no próprio banco; o snapshot pode devolver usuários
e permissões ([documentação de usuários contidos](https://learn.microsoft.com/en-us/sql/relational-databases/security/contained-database-users-making-your-database-portable?view=sql-server-ver16)).
Portanto não é possível declarar a cópia inacessível às identidades produtivas
apenas por nome, tags ou por não trocar a conexão da aplicação. Um private endpoint
também se aplica ao servidor e não desliga o acesso público sozinho
([Private Link](https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql)).
ARM não prova as permissões SQL dentro do snapshot. Sem controle comprovado
antes do Online, o ensaio permanece bloqueado mesmo com aprovação de custo;
não será criada cópia clínica neste servidor para corrigir permissões depois.

A evidência adicional de que a API usa o administrador SQL do servidor torna
esse impedimento concreto: ACLs só da nova cópia não segregam essa identidade
([permissões do Server admin](https://learn.microsoft.com/en-us/azure/azure-sql/database/logins-create-manage?view=azuresql)).
Reduzir os privilégios da aplicação exige investigação e alteração operacional
próprias, fora desta auditoria; nada foi alterado automaticamente. A revisão de
Security não identificou alternativa suportada de PITR isolado pré-Online com
as políticas atuais sem mudar controles produtivos. Uma cópia do estado atual
para outro servidor seria outro procedimento e não provaria PITR histórico;
copiar depois de restaurar aqui não elimina a exposição inicial.

## Integridade e segurança antes de liberar a cópia

O verificador `scripts/ValidateRestoredDatabase.sql` atende ao contrato de schema
`20261019121529_Schema_AddSurgeryType` deste checkout. Exige banco com prefixo
literal `restore184-`, confirmação exata do nome (case e tamanho), permissões
`SELECT` e `VIEW DEFINITION` e cópia sem escritores. Em conexão dedicada da cópia
**já isolada**, definir primeiro e então executar o arquivo na mesma sessão:

```sql
EXEC sys.sp_set_session_context
    @key = N'Restore184ExpectedDatabase',
    @value = N'restore184-<nome-exato-do-destino>';
-- Executar scripts/ValidateRestoredDatabase.sql nesta mesma conexão.
```

Sucesso retorna somente `CheckName`/`Violations` com contagens zero. O cliente
deve tratar erro SQL como falha (por exemplo `sqlcmd -b`): `51840` recusa alvo/
confirmação, `51841` recusa permissões/schema, `51842` indica violações e `51843`
indica inspeção inconclusiva com erro interno sanitizado. Não aprovar inspeção
parcial ou erro como ausência de violações. O script não altera dados/schema
persistentes; guard de nome impede erro de alvo, mas **não comprova quarentena**.

O manifesto cobre 107 FKs (inclusive compostas tenant e identidade), 17 CHECKs
e seis unicidades críticas. Confere presença/shape das FKs e órfãos, estado de
constraints/índices e divergências de usuário global da sessão e do vínculo de
clínica/perfil. A comparação adicional de clínica em FK simples é limitada a
`UsuariosClinicas → Users`, cuja propriedade foi confirmada no fluxo existente;
outros `ClinicaId` podem ter significado de domínio diferente. CHECKs são
verificados por nome/tabela, estado e violações de sua expressão atual, sem
afirmar equivalência dessa expressão ao modelo EF. O teste do manifesto detecta
omissões de FKs/CHECKs do modelo atual. Não é comparação completa de schema,
baseline de linhas, CHECKDB, teste de autorização nem prova de revogações.

- Comparar `dbo.__EFMigrationsHistory` com **o commit compatível com o ponto
  restaurado**, não automaticamente com HEAD. Executar o SchemaGuard compilado
  desse commit via configuração segura restrita à cópia. Retornos: `0` histórico
  igual, `10` prefixo com migrations conhecidas pendentes, `3` inconsistente,
  `2` inspeção indisponível. `10` não autoriza migration nem cutover neste ensaio.
  `0` verifica histórico, não integridade física nem todas as constraints.
- Comparar metadados com o schema esperado desse commit: `sys.foreign_keys`,
  `sys.foreign_key_columns`, `sys.key_constraints`, `sys.indexes` e
  `sys.check_constraints`. Exigir constraints esperadas presentes e habilitadas/
  confiáveis (`is_disabled=0`, `is_not_trusted=0` quando aplicável); índices
  necessários habilitados. Constraint ausente não aparece numa lista de inválidas.
- Para versões posteriores à migration
  `20260909170626_Schema_EnforcePatientUserClinicRelationships`, verificar
  `AK_Users_ClinicaId_Id`, `AK_Pacientes_ClinicaId_Id` e as 31 FKs compostas
  dessa migration, incluindo `FK_Pacientes_Users_ClinicaId_UserId` e
  `FK_PasswordResetTokens_Users_ClinicaId_UserId`. Conferir também FKs adicionadas
  por migrations posteriores, conforme commit compatível.
- Produzir contagens de divergências por relação, incluindo referências não
  nulas órfãs via LEFT JOIN. O script existente
  `scripts/audit-patient-user-clinic-relationships.sql` mostra IDs: **não executar
  sua saída crua como evidência compartilhável**. Adaptar previamente a contagens;
  seu INNER JOIN sozinho não detecta órfãos.
- Conferir `UsuariosClinicas.UserId → Users.Id` (clínica e perfil),
  `AuthenticationSessions.UsuarioClinicaId → UsuariosClinicas.Id` (usuário global)
  e órfãos. Divergência de perfil exige análise da regra vigente, sem correção
  automática. FKs simples não garantem essas igualdades.
- Comparar contagens por tabela/clínica com baseline do mesmo ponto, sem IDs
  individuais na evidência. Avaliar integridade física com ferramenta/procedimento
  SQL aprovado e carga prevista na cópia; não publicar resultados com valores
  de registros. `ValidateFinancialMigrationCopy.sql` contém banco/migration
  históricos fixos e não é um verificador genérico de PITR.
- Negativos obrigatórios no host isolado: A possui recurso, B tenta ler/listar,
  alterar/excluir/exportar com ID e `ClinicaId` forjados; backend deve negar ou
  não revelar. Administrador não recebe bypass implícito. Registrar só resultado.
- PITR pode ressuscitar sessões, senhas, vínculos, permissões e dados eliminados.
  Reconciliar revogações, exclusões e alterações de segurança posteriores ao ponto
  antes da exposição. Validar rejeição de sessão/token anterior ao incidente e
  usuário desativado. Não presumir que incrementar uma SecurityVersion restaurada
  evita colisão com versões emitidas depois; o mecanismo deve ser revisado contra
  o fluxo real. Nenhuma alteração de autenticação é autorizada por este runbook.

## Decisão por cenário e cutover

| Cenário | Conduta e condição de saída |
| --- | --- |
| Deploy ruim, schema compatível | Seguir `production-blue-green-runbook.md`: smoke de PREVIOUS, confirmar tráfego 100% e preservar revisões. Sem restore SQL. |
| Migration falhou ou schema incompatível | Interromper rollout; avaliar estado parcial e escritores. Preferir forward fix revisado. `Down()` ou retry não comprovam reversibilidade de dados. |
| Exclusão/corrupção lógica | PITR em novo destino antes do primeiro evento ruim, validar e reconciliar escritas válidas. Restore de banco compartilhado afeta todas as clínicas; não há PITR nativo por ClinicaId. |
| Banco removido ou indisponibilidade regional | Verificar backup efetivamente disponível e janela correspondente. Não presumir LTR/geo-restore com Local e LTR desativado; acionar responsável/Azure e registrar indisponibilidade da opção. |
| Comprometimento de acesso | Conter e revogar primeiro; restaurar dados não elimina a credencial comprometida. Exigir revisão de Security antes da liberação. |

Ensaio de mesa realizado sobre esses cinco cenários e workflows existentes;
nenhuma troca real de tráfego foi ensaiada. Para cutover real, aprovação
operacional adicional é obrigatória: congelar **todos** os escritores, incluindo
API em todas as revisões/labels, workers, integrações e operadores. O lock
`production-container-publish` não cobre operações manuais Azure. `0%` no FQDN
principal não bloqueia acesso direto por label. Workers devem ser controlados
separadamente: no publish dependem de migration, não de sucesso do deploy API.

Após validação/reconciliação, atualizar referências de conexão pelo fluxo seguro
aprovado, verificar schema e readiness e só então liberar escritores e tráfego.
Nunca permitir escrita simultânea na origem e destino. Depois da primeira escrita
no destino, voltar apenas a connection string pode perder dados; requer nova
decisão e reconciliação. Preservar a origem bloqueada conforme política aprovada.

## Pendências, periodicidade e status

Ensaio proposto **trimestral** e após mudança relevante de schema, backup,
identidade ou topologia; aprovação pelos responsáveis pendente. Conferência
mensal de políticas/janela/alertas e vencimento das evidências. Alertas devem ter
responsável e teste de entrega: redução da janela, mudança/desativação de política,
falha de restore e indisponibilidade; implementação fora desta auditoria.

A confirmação `backup_pitr_confirmed` do workflow manual é declaração humana,
não evidência de restore. `publish-container.yml` não possui esse input nem
SchemaGuard pré/pós equivalente ao manual; achado já registrado na #173 e
preservado fora do escopo. Não inferir segurança de schema só de health HTTP.

Status: origem configurada da API confirmada, auditoria de infraestrutura e
runbook preparados; **capacidade de recuperação, isolamento do destino,
inventário completo de escritores, RPO/RTO e restore real ainda não validados**.
Não encerrar a #184 como recuperação comprovada. Evidência mínima futura:
aprovadores/ticket, IDs técnicos de origem/destino, UTC e ponto, SKU/custo,
resultados agregados, RPO/RTO medidos, decisão e descarte confirmado.

Validação local nesta retomada:

- `python -B -m unittest scripts.tests.test_production_deployment_gates -v`:
  **6 aprovados** (gates/rollback offline, sem Azure).
- `dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --configuration Release
  --no-restore --filter 'FullyQualifiedName~AppDbContextTenancyTests|FullyQualifiedName~PlatformDbContextIsolationTests'
  --verbosity minimal`: **9 aprovados, zero falhas/ignorados**. Build emitiu
  avisos IDE0005 em arquivos existentes, sem erros.
- Referências locais e padrões de secrets revisados; somente este documento
  adicionado na primeira etapa. A continuação acrescentou o verificador e seus
  testes. Security aprovou o runbook preparatório, sem atestar isolamento real.

Esses testes não validam o banco restaurado, rede, RPO/RTO nem E2E em Azure;
o ensaio operacional segue pendente.

Validação final da continuação, em LocalDB descartável: **14 aprovados, zero
falhas/ignorados**, em 16 s, com `HEMODINKS_TEST_LOCALDB=1` e sem conexão SQL
externa no processo de testes. Comando `dotnet test` em Debug, `--no-build
--no-restore`, filtro de `RestoreDatabaseIntegrityTests`,
`PatientUserTenantSqlServerTests`, `AppDbContextTenancyTests` e
`PlatformDbContextIsolationTests`. TRX local ignorado pelo Git:
`out/issue184-validation/restore184-validation.trx`. Inclui execução positiva sob
usuário SQL sem login, com somente SELECT/VIEW DEFINITION, além dos negativos de
alvo, permissão, schema, constraints, órfãos, clínicas e identidade de sessão.
Database também executou 13 testes direcionados antes desta rodada ampliada.
Security aprovou estaticamente SQL/testes/runbook; referências, diff e padrões
de dados sensíveis foram conferidos. Não se repetiram gates/fluxos da aplicação
sem alteração. Nenhum recurso Azure foi criado ou alterado.

Desempenho em volume clínico real não foi medido. O sucesso em banco sintético
valida o verificador; não substitui o restore PITR, a quarentena nem medidas de
RPO/RTO. Próximo passo operacional: revisar em mudança separada a identidade de
runtime e controles de rede/acesso para comprovar isolamento antes do Online;
então definir destino, ponto e SKU do ensaio já autorizado e executar o runbook.

Referências oficiais consultadas em 24/10/2026 UTC:
[restore e permissões](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql),
[políticas e redundância](https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview?view=azuresql),
[Azure CLI SQL DB](https://learn.microsoft.com/en-us/cli/azure/sql/db?view=azure-cli-latest).
