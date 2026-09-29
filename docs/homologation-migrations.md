# Deploy da homologacao

## Escolha do modo em Publish Homologation

| Modo | Quando usar | Banco | Publica API? |
| --- | --- | --- | --- |
| prepare (padrao) | Revisar SQL e pacote | Nao acessa | Nao |
| deploy-no-schema-changes | Correcoes durante congelamento do banco | Apenas consulta historico de migrations | Sim, se historico corresponder ao commit |
| migrate-and-deploy | Mudanca de schema autorizada | Aplica todas as migrations pendentes | Sim, apos sucesso |

O antigo booleano publish foi substituido por mode. Integracoes que usam workflow_dispatch devem enviar mode. O padrao continua sendo apenas preparar.

## Passo a passo

1. Enviar codigo para developer e aguardar CI aprovado para o commit.
2. Actions > Publish Homologation > Run workflow > branch developer.
3. Escolher prepare para revisao; durante congelamento, usar deploy-no-schema-changes para publicar. Usar migrate-and-deploy somente quando alteracoes de schema forem autorizadas e backup estiver disponivel.
4. Aguardar sucesso e verificar login e funcionalidades na homologacao. Nao usar deploy direto no Render para contornar verificacoes.

## Garantias e limites

O fluxo valida URL do servico, branch developer, Auto-Deploy desligado, migrations/manutencao/seeds desabilitados e correspondencia exata da connection string do secret com a configurada no Render. Nunca imprime essas credenciais.

A ferramenta Hemodinks.SchemaGuard usa os metadados EF Core do proprio commit e GetAppliedMigrationsAsync, sem iniciar a API. Nao cria banco/tabelas, nao chama Migrate, EnsureCreated ou SaveChanges. Falha de conexao, historico vazio, migrations pendentes, desconhecidas ou divergentes bloqueiam deploy. O historico deve corresponder integralmente: isso tambem bloqueia rollback para codigo com menos migrations.

Esta verificacao compara historico EF; nao detecta alteracoes manuais de colunas fora das migrations. Nao certifica compatibilidade de dados ou de SQL escrito manualmente. Deploy sem alterar schema nao impede escritas normais da aplicacao apos publicacao.

Apos verificacao bem-sucedida OU aplicacao bem-sucedida do bundle, o workflow define Database__SchemaManagedByDeployment=true e publica o SHA verificado. Isso reutiliza a otimizacao de producao para dispensar consulta de migrations no startup/readiness; o probe de disponibilidade SQL permanece. Render Free ainda pode sofrer cold start.

Falha no gate impede alteracao de configuracao e deploy. Falha posterior de deploy nao reverte migrations ja aplicadas no modo migrate-and-deploy. Concurrency serializa execucoes deste workflow; evitar operacoes manuais concorrentes.

## Configuracao

Environment GitHub homologation restrito a developer:
- Secret HOMOLOGATION_RENDER_API_KEY.
- Secret HOMOLOGATION_SQL_CONNECTION_STRING.
- Variavel HOMOLOGATION_RENDER_SERVICE_ID=srv-d8hmtje47okc738ldllg.

Servico: hemodinks-api-1, https://hemodinks-api-1-90nb.onrender.com, developer, Virginia, ASP.NET Confirmation. Auto-Deploy precisa permanecer Off. Seeds e manutencao permanecem desabilitados. Os secrets foram cadastrados e a preparacao anterior concluiu na execucao 36633800221; esse resultado nao valida os novos modos deste incremento.

## Producao e PR para main

Somente workflow_dispatch e permitido, e os dois jobs sao restritos a refs/heads/developer. Um PR/merge para main nao dispara homologacao. Os workflows e configuracoes de producao nao foram alterados; seus gatilhos atuais continuam valendo.

## Validacao

Testes locais cobrem historico igual, ordenacao, migrations pendentes, banco adiantado, historico divergente/vazio, ausencia da tabela de historico sem criacao de tabelas e falha de conexao. Nenhum banco remoto foi acessado ou alterado nesta implementacao.

Resultados deste incremento: 17 testes passaram (9 do verificador e 8 de startup); CLI sem connection string retorna codigo 2; Actionlint/YAML e verificacao de whitespace aprovados. Novos modos ainda nao executados no GitHub nem em bancos remotos.
