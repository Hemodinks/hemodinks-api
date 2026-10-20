# API #178 — tipo de cirurgia

`TipoCirurgia` aceita `Eletiva` e `Urgencia`. Criações e registros anteriores
recebem `Eletiva`; atualizações que omitem o campo preservam o valor existente.
O registro cirúrgico legado de `Paciente` e cada `AtendimentoCirurgico` possuem
classificações independentes. Faturamentos normalizados usam o atendimento.

Listagens de pacientes, faturamentos médicos, atendimentos, faturamentos,
contas a receber e resumo financeiro aceitam `tipoCirurgia`. Para Todos, omitir
o filtro. O resumo preserva as regras financeiras existentes e acrescenta
`porTipoCirurgia`; a quantidade conta atendimentos distintos, mesmo quando há
mais de um faturamento para uma cirurgia.

## Exportação

Para `pacientes` e `faturamentos-medicos`, `reportType` aceita `completo`
(padrão) ou `sintetico`, em PDF/XLSX. O conteúdo usa as consultas autorizadas
da listagem, incluindo filtros, clínica e visibilidade do usuário. O sintético
contém totais por tipo, sem nomes. Não interpreta valores monetários legados
nem altera a conferência de pagamento.

O snapshot é capturado durante a requisição, limitado a 1.000 registros e
45.000 bytes de mensagem. Excesso ou filtros não suportados retornam erro,
sem enfileirar arquivo parcial. Alterações de contagem/duplicidade durante a
paginação são rejeitadas; a captura não oferece isolamento transacional entre
páginas. CBHPM e mensagens antigas sem snapshot continuam somente com metadados.
O destino de armazenamento permanece privado e separado por clínica.

## Publicação e rollback

1. Aplicar a migration aditiva `20261019121529_Schema_AddSurgeryType` uma vez,
   com executor serializado, antes das novas instâncias. Não executar migrations
   simultaneamente em API/workers.
2. Atualizar todos os workers antes da nova API: workers antigos não renderizam
   o snapshot novo. Depois publicar a API e, por último, o frontend #173.
3. No rollback de aplicação, manter as colunas. O DEFAULT permite INSERTs dos
   binários antigos; UPDATEs antigos não sobrescrevem a classificação.
4. O Down do schema bloqueia se qualquer tabela contiver `Urgencia`. Nunca
   converter esses registros para Eletiva apenas para remover as colunas.

O banco restringe os dois valores por CHECK. Não há novos índices; avaliar
volume e duração dos locks no ambiente de destino antes da publicação.

## Evidências locais

Artefatos de validação ficam em `out/issue-validation/`: testes direcionados
(`issue178-final-targeted.trx`), regressão (`issue178-regression.trx`), verificação
do modelo EF (`issue178-model.log`) e SQL incremental idempotente
(`issue178-rollout.sql`). Os testes SQL usam bancos descartáveis LocalDB e
incluem replay do script, compatibilidade com binários antigos, rollback,
restrições, isolamento e regras financeiras. Os testes de exportação executam
o worker real contra armazenamento HTTP local simulado.

Resultado: 24 testes direcionados aprovados; regressão com 1.011 aprovados,
3 falhas e 12 testes de navegador ignorados. A preparação do teste de backfill
legado foi adaptada à nova propriedade; sua repetição
(`issue178-backfill-recheck.trx`) alcançou a falha ambiental já conhecida de
Full-Text ausente. As pendências restantes são esse requisito de Full-Text
(backfill e concorrência de lembretes) e a preparação antiga que tenta remover
`AK_Users_ClinicaId_Id` ainda referenciada por Events. O modelo EF não tem
alterações pendentes e o SQL incremental foi reaplicado duas vezes nos testes.
O build também mantém os avisos NuGet existentes de vulnerabilidade em
`System.Security.Cryptography.Pkcs` 9.0.4; dependências não foram alteradas.

Não houve publicação nem alteração no frontend. A validação visual/E2E de
navegador depende da implementação do frontend #173; carga e bloqueios em
produção não foram medidos.

## Retomada da issue

A revisão independente de segurança encontrou duas divergências no relatório
completo de pacientes e faturamentos médicos: procedimentos secundários eram
omitidos quando havia procedimento principal legado e a data de solicitação
substituía uma data cirúrgica ausente. O snapshot agora prioriza a coleção
ordenada de procedimentos, mantendo o fallback para registros legados, e usa
somente `DataAtendimento` na coluna `Data cirurgia`.

Oito novos casos cobrem ambos os recursos, filtro por procedimento secundário,
PDF/XLSX, totais sem duplicação, fallback legado e datas cirúrgicas preenchidas
ou ausentes. Antes da correção, seis casos falharam pelas divergências e dois
passaram. Após a correção, a bateria direcionada passou com 31 testes, nenhum
ignorado, incluindo migration/rollback em LocalDB, isolamento e privacidade.
Evidências: `issue178-resume-red.trx`, `issue178-resume-green.trx` e
`issue178-resume-model.log` em `out/issue-validation/`. A verificação do
`AppDbContext` confirmou que o modelo EF continua sem alterações pendentes.

A regressão completa desta retomada (`issue178-resume-regression.trx`) terminou
com 1.019 aprovados, três falhas e 12 testes de navegador ignorados:

- `PatientUserTenantSqlServerTests.SqlServer_RejectsCrossClinicPatientAndUserRelationships`:
  a preparação tenta remover `AK_Users_ClinicaId_Id`, referenciada por `Events`.
- `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`:
  Full-Text Search não instalado no LocalDB.
- `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`:
  Full-Text Search não instalado no LocalDB.

São as pendências já documentadas; nenhuma nova falha de regressão foi observada.
Os 12 cenários `LoginBrowserTests.Browser_UsesRealApi` foram ignorados por falta
de `HEMODINKS_E2E_FRONT_PATH`. O E2E responsivo do tipo de cirurgia com frontend
e API integrados permanece pendente do frontend #173. Não houve commit, merge,
deploy, nova dependência ou alteração de schema nesta retomada.
