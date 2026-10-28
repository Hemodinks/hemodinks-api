# API #197 — Migração segura dos workflows SQL de produção

## Situação operacional (28/10/2026)

- Azure SQL: banco `HemodinksDB` no servidor `gm-tech-solution`.
- Runtime contido `hemodinks_runtime_prod` e migrator `hemodinks_migrator_prod` criados com identidades diferentes.
- O runtime passou na auditoria de permissões executada com sua própria identidade.
- API de produção: revisão `hemodinks-api-prod--p197sqlruntimev2` com 100% do tráfego, usando secret `connectionstrings-runtime-prod-v2`.
- Green anterior `hemodinks-api-prod--sha-96d25dd53c` mantida ativa com 0% para rollback.
- Banco e API responderam aos testes de saúde e leitura; a reversão real de tráfego não foi exercitada.
- Firewall no nível do servidor continua público, com 15 regras incluindo `AllowAllWindowsAzureIps`. A restrição de rede **não** está concluída.

## Alterações deste PR

- `publish-container.yml`: preparação e execução de migrations com `AZURE_SQL_MIGRATION_CONNECTION_STRING`; SchemaGuard usa `AZURE_SQL_RUNTIME_CONNECTION_STRING` para validar os privilégios e o histórico antes/depois do bundle.
- `apply-production-migrations.yml`: mesma separação de identidades e preflight sem conexão com o banco.
- Workflows mutáveis de produção e publicação manual de artefatos restritos à branch `main`.
- SchemaGuard impede runtime privilegiado e detecta histórico de migrations inconsistente.
- Scripts SQL auditáveis e testes negativos de privilégio. Scripts de provisionamento são **manuais**, não executados por workflows ou durante a inicialização da API.
- CI offline verifica referências aos novos secrets e os gates de aprovação.

## Condições de integração

1. Exigir CI verde e revisão do diff de segurança. Os scripts SQL de provisionamento **não** devem ser executados novamente em produção apenas por causa do merge.
2. Confirmar que o GitHub Environment `production` mantém `AZURE_SQL_RUNTIME_CONNECTION_STRING` e `AZURE_SQL_MIGRATION_CONNECTION_STRING`, com as identidades e o banco corretos. Não copiar segredos para arquivos, logs ou PR.
3. Validar que o migrator dispõe das permissões **exatamente necessárias** a cada migration futura; a combinação inicial `db_ddladmin` + `db_datareader` + `db_datawriter` não garante todas as operações possíveis.
4. Fazer merge somente após aprovação; **merge não autoriza executar** `Publish Production` ou `Apply Production Migrations`.
5. A publicação futura deve preservar a referência `connectionstrings-runtime-prod-v2` herdada da revisão ativa. Verificar o diff do template e o roteamento antes de qualquer execução.
6. Manter a Green e o secret anterior disponíveis durante a janela de observação. Rollback de tráfego não reverte schema.
7. Homologação permanece fora do escopo deste PR. Planejar alinhamento das identidades e workflows de homologação separadamente antes de rodar migrations nesse ambiente.

## Riscos e pendências

- Não há alteração de rede, firewall, VNet, Private Endpoint, Container Apps ou Azure SQL neste PR.
- A regra ampla `AllowAllWindowsAzureIps` permanece: mapear consumidores do servidor (PROD, HOMOL/Render, GitHub Actions, `ESCOLA_API`) e aprovar topologia e custos antes de restringir.
- Usuários SQL contidos podem acompanhar um PITR; #198 precisa de isolamento efetivo do destino antes de qualquer restauração real.
- Este PR não contém novos endpoints, ajustes de domínio, migrations EF, mudanças no frontend ou alterações de `ClinicaId`.
