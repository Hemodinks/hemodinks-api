# Deploy da homologacao

## Escolha do modo em Publish Homologation

| Modo | Quando usar | Banco | Publica API? |
| --- | --- | --- | --- |
| `diagnose` | Conferir a configuracao do Render | Nao acessa | Nao |
| `diagnose-sql` | Investigar conexao e historico de migrations | Apenas leitura | Nao |
| `prepare` (padrao) | Revisar SQL e pacote | Nao acessa | Nao |
| `publish` | Publicar o commit da `developer` | Consulta o historico e, se houver migrations pendentes conhecidas, aplica o bundle | Sim, apos verificacao |

No modo `publish`, o job escolhe sozinho entre `deploy-only` (historico igual ao commit) e
`migrate-and-deploy` (somente migrations conhecidas pendentes). Historico desconhecido, fora de ordem ou
adiantado bloqueia o deploy. Os antigos modos `deploy-no-schema-changes` e `migrate-and-deploy` deixaram de
ser opcoes de entrada.

## Passo a passo

1. Fazer merge na `developer` e aguardar o CI aprovado para o commit (o workflow verifica isso).
2. Actions > Publish Homologation > Run workflow > branch `developer`.
3. Opcional: rodar `prepare` para revisar o SQL gerado (artefato `homologation-migrations-<sha>`).
4. Rodar `publish`. Quando houver migration, garanta antes um backup ou ponto de restauracao do banco de homologacao.
5. Aguardar sucesso e verificar login e funcionalidades na homologacao. Nao usar deploy direto no Render para contornar verificacoes.

O front de homologacao e publicado pela integracao da Vercel a partir da `developer`; nenhum workflow do
`hemodinks-front` publica a homologacao.

## Garantias e limites

O fluxo valida URL do servico, branch developer, Auto-Deploy desligado, migrations/manutencao/seeds desabilitados e correspondencia exata da connection string do secret com a configurada no Render. Nunca imprime essas credenciais.

A ferramenta Hemodinks.SchemaGuard usa os metadados EF Core do proprio commit e GetAppliedMigrationsAsync, sem iniciar a API. Nao cria banco/tabelas, nao chama Migrate, EnsureCreated ou SaveChanges. Falha de conexao, historico vazio, migrations pendentes, desconhecidas ou divergentes bloqueiam deploy. O historico deve corresponder integralmente: isso tambem bloqueia rollback para codigo com menos migrations.

Esta verificacao compara historico EF; nao detecta alteracoes manuais de colunas fora das migrations. Nao certifica compatibilidade de dados ou de SQL escrito manualmente. Deploy sem alterar schema nao impede escritas normais da aplicacao apos publicacao.

Apos verificacao bem-sucedida ou aplicacao bem-sucedida do bundle, o workflow define Database__SchemaManagedByDeployment=true e publica o SHA verificado. Isso reutiliza a otimizacao de producao para dispensar consulta de migrations no startup/readiness; o probe de disponibilidade SQL permanece. Render Free ainda pode sofrer cold start.

Falha no gate impede alteracao de configuracao e deploy. Falha posterior de deploy nao reverte migrations ja aplicadas pelo modo `publish`. Concurrency serializa execucoes deste workflow; evitar operacoes manuais concorrentes.

## Configuracao

Environment GitHub homologation restrito a developer:
- Secret HOMOLOGATION_RENDER_API_KEY.
- Secret HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING (mesmo valor instalado no Render, DML restrito).
- Secret HOMOLOGATION_SQL_MIGRATION_CONNECTION_STRING (identidade separada, somente bundle).
- Provisionamento, verificacao efetiva, rede e rollback: [runbook SQL](issue-197-sql-runtime-network.md).
- Variavel HOMOLOGATION_RENDER_SERVICE_ID=srv-d8hmtje47okc738ldllg.

Servico: hemodinks-api-1, https://hemodinks-api-1-90nb.onrender.com, developer, Virginia, ASP.NET Confirmation. Auto-Deploy precisa permanecer Off. Seeds e manutencao permanecem desabilitados. Os secrets estao cadastrados no environment `homologation`.

## Producao e PR para main

Somente workflow_dispatch e permitido, e os dois jobs sao restritos a refs/heads/developer. Um PR/merge para main nao dispara homologacao. Os workflows e configuracoes de producao nao foram alterados; seus gatilhos atuais continuam valendo.

## Validacao

`tools/Hemodinks.SchemaGuard` tem testes para historico igual, ordenacao, migrations pendentes, banco adiantado,
historico divergente/vazio, ausencia da tabela de historico sem criacao de tabelas e falha de conexao.
O fluxo completo foi exercitado em homologacao (por exemplo, a publicacao da migration
`20261107043000_Schema_AddSurgeryConsolidationStatus`).

## Diagnostico SQL somente de leitura

Usar mode=diagnose-sql na branch developer. Valida o destino Render e os secrets, compila o verificador sem credenciais e consulta conexao/historico. Nao aplica migrations, nao muda variaveis Render e nao faz deploy. Mode=diagnose continua validando somente a configuracao Render.

Os logs mostram etapa (OpeningSqlConnection ou ReadingMigrationHistory), duracao, categoria e numero/state/severity SQL. Nunca imprimem exception.Message, stack trace, servidor, usuario ou connection string. SqlTimeout nao prova firewall ou banco pausado: exige correlacao com a infraestrutura. DatabaseUnavailable tambem nao prova auto-pause. Nao ha retry automatico nem aumento dos timeouts neste incremento.
