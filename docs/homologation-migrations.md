# Migrations e deploy da homologação Render

A alteração está preparada no repositório; não aplica migrations nem muda o ambiente remoto por si só. Não há alteração de entidade, schema ou migration neste incremento. Os workflows Azure/produção e o Render confirmation opcional permanecem intactos.

## Fluxo

`Publish Homologation` é manual, na branch `main` usada pelo `render.yaml`. Exige CI de push aprovado para o SHA exato e gera bundle EF Core Linux e SQL idempotente auditável. Com `publish=false` (padrão), termina aí, sem credenciais de banco e sem deploy.

Quando houver autorização para publicar, `publish=true` usa o environment GitHub `homologation`, valida o destino Render, aplica o bundle e publica o mesmo SHA, aguardando status `live`. Falha de migration impede deploy. Falha posterior de deploy não desfaz schema: investigar antes de repetir. O bundle aplica todas as migrations pendentes, não somente as criadas neste incremento; não selecionar publicação durante o congelamento de alterações de banco.

O banco continua validado no startup/readiness: `SchemaManagedByDeployment` não foi habilitado. Desligar execução de migrations reduz trabalho de startup, mas não elimina suspensão do Render ou do banco.

## Ativação operacional (não executada nesta tarefa)

1. Confirmar que o serviço de homologação é `hemodinks-api-1-90nb.onrender.com`, ligado à branch `main`. O workflow bloqueia outro destino. Se a URL legítima mudar, revisar a constante explicitamente.
2. Criar environment `homologation`, restringir à branch `main` e configurar proteções de revisão adequadas. Cadastrar secrets `HOMOLOGATION_RENDER_API_KEY` e `HOMOLOGATION_SQL_CONNECTION_STRING` e variável `HOMOLOGATION_RENDER_SERVICE_ID`. Usar acesso Render restrito à homologação, quando disponível. A connection string deve coincidir exatamente com a do serviço de homologação; o preflight compara sem imprimi-la. Não usar secrets de produção. O runner precisa alcançar o SQL com permissão de migration.
3. Desativar Auto-Deploy no serviço Render ANTES de integrar/sincronizar a configuração. Isso evita publicação concorrente que contorne o workflow. O blueprint declara `autoDeployTrigger: off`.
4. Configurar `Database__RunMigrationsOnStartup=false` no serviço, com manutenção/seeds desativados conforme blueprint. Não iniciar deploy/sincronização do blueprint enquanto a suspensão de mudanças remotas estiver em vigor. Variáveis no painel precisam ser verificadas: editar YAML não comprova configuração efetiva.
5. Após integrar e obter CI verde, executar primeiro com `publish=false` e revisar SQL/artefato. Quando acabar o congelamento e houver backup disponível, executar com `publish=true` para o mesmo commit revisado. Nunca usar deploy direto do painel para uma versão dependente de novo schema.

Enquanto não houver novas mudanças no schema, o bundle futuramente não terá trabalho se o banco já estiver atualizado. Isso não é presumido nem verificado nesta tarefa, que não acessa bancos.

Migrations futuras devem ser compatíveis com a versão anterior em execução (expandir, migrar dados, remover em incremento posterior). Não há rollback automático de schema. Concurrency serializa publicações deste workflow; mudanças manuais no Render continuam sendo responsabilidade operacional.

Referências: [deploy por commit](https://render.com/docs/deploys) e [blueprints](https://render.com/docs/blueprint-spec).
