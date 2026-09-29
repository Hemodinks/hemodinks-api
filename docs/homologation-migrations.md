# Migrations e deploy da homologacao Render

## Estado confirmado em 29/09/2026

Servico real: hemodinks-api-1, ID srv-d8hmtje47okc738ldllg, branch developer, regiao Virginia, plano Free, ambiente ASP.NET Confirmation.

Nesta tarefa foram configuradas remotamente Database__RunMigrationsOnStartup=false e Database__RunMaintenanceOnStartup=false. O Render iniciou o deploy dep-dau2h40u01pc73b0koqg. Os logs confirmaram migracao automatica desabilitada e schema atualizado. Nenhuma migration foi executada por esta tarefa. Producao nao foi alterada.

O workflow corrigido esta local, sem commit/push nesta tarefa. O environment GitHub homologation foi criado, restrito a developer, com HOMOLOGATION_RENDER_SERVICE_ID configurado. Os dois secrets ainda precisam ser cadastrados. O auto-deploy remoto continua ativo e precisa ser desligado antes da ativacao do fluxo completo.

## Fluxo seguro

Publish Homologation e manual e aceita somente refs/heads/developer. Exige CI de push aprovado para o SHA exato. Com publish=false (padrao), gera bundle Linux e SQL idempotente sem acessar banco.

Com publish=true, valida destino Render, branch developer, auto-deploy desligado, migrations/manutencao/seeds desabilitados e connection string igual a do servico. Aplica todas as migrations pendentes. Somente apos sucesso define Database__SchemaManagedByDeployment=true e publica o mesmo SHA. Esta flag reutiliza a otimizacao existente de producao: dispensa consulta de migrations no startup/readiness, preservando o probe de disponibilidade SQL. Nao habilitar manualmente enquanto deploys puderem contornar o bundle.

Falha de migration impede deploy. Falha posterior de deploy nao reverte schema. Usar migrations compativeis com a versao anterior e backup disponivel. Nao selecionar publish=true durante o congelamento de alteracoes no banco.

## Ativacao pendente

1. Criar environment GitHub homologation restrito a developer, com protecoes adequadas.
2. Cadastrar secrets HOMOLOGATION_RENDER_API_KEY e HOMOLOGATION_SQL_CONNECTION_STRING e variavel HOMOLOGATION_RENDER_SERVICE_ID=srv-d8hmtje47okc738ldllg. Nunca reutilizar credenciais de producao. O runner precisa alcancar o banco.
3. Desligar Auto-Deploy no servico Render. Nao sincronizar o blueprint sobre outro servico. render.confirmation.yaml corresponde a homologacao; editar o arquivo nao sincroniza automaticamente o painel.
4. Verificar seeds desabilitados no painel. Integrar o workflow corrigido e executar primeiro com publish=false apos CI verde.
5. Apos fim do congelamento de banco, revisar SQL e executar publish=true para o commit revisado. Nao fazer deploy direto de uma versao dependente de schema novo.

## PR para main

Abrir ou mesclar PR para main nao dispara este workflow: nao ha trigger push/pull_request, e os dois jobs sao restritos a developer. Os workflows de producao nao foram alterados e continuam seguindo seus gatilhos existentes, incluindo publicacao por push em main. O render.yaml legado nao foi modificado nesta correcao.

## Diagnostico e verificacao

Na inicializacao das 20:58 UTC, HTTP ficou disponivel apos 142,9 s, dos quais 86,9 s na etapa SQL/EF, mesmo sem migrations pendentes. O timeout de login e 120 s. Apos desligar migracao automatica, a etapa SQL levou 34,3 s; uma amostra nao isola efeitos de cache, retomada do banco e recursos do container. A consulta de schema ainda ocorre ate ativar o fluxo gerenciado. Render Free pode continuar tendo cold start.

Validacao local: 8 testes DatabaseStartupPolicyTests aprovados, YAML parseado, Actionlint e git diff --check sem erros. Nao foi executado o workflow remoto de migrations.

Referencias: https://render.com/docs/deploys e https://api-docs.render.com/reference/update-env-var
