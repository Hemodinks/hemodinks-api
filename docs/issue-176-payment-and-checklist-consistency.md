# Issue #176 — quitação, valores e checklist operacional

## Diagnóstico e limites da evidência

A coluna `Pendências` do PDF/XLSX atual é `pendingChecklistItems`, calculada
em `hemodinks-front/src/features/billing/billingUtils.ts`. Cada item com
`warning` ou `missing` soma um alerta, independentemente do estado de pagamento.
Não representa número de atendimentos inadimplentes nem saldo devedor.

Assim, dois atendimentos podem ter cinco e seis alertas, totalizando onze itens
de checklist. Um atendimento marcado pago pode manter cinco alertas. A captura
posterior, filtrada por `Pagos`, apresenta um registro com cinco alertas e não
demonstra erro matemático ou quitação financeira incorreta na API.

Não foram recebidos IDs internos e chaves completas dos filtros para conciliar
os registros reais. Nenhum registro real foi consultado, alterado ou considerado
quitado por inferência da imagem. Dados pessoais e clínicos das capturas não
foram copiados para fixtures, logs ou este relatório.

## Fonte de verdade médica existente

- `Paciente.StatusPago` é a declaração persistida de quitação do cadastro médico,
  recebida pelos comandos de criação/edição de paciente. Checklist, anexos,
  honorários, glosa e data isolada não alteram esse booleano na leitura.
- `FaturamentoMedicoSync.EnsureSynced` copia esse estado para
  `ConferenciaPagamentoRealizada`. Essa flag não constitui um segundo lançamento
  de recebimento nem deve prevalecer sobre `statusPago` em registros divergentes.
- Create/update gravam `DataPagamento` informada quando `StatusPago=true` e a
  limpam quando false. O contrato atual aceita true sem data; não foi endurecido.
  Uma data presente em registro legado false não transforma sua leitura em true.
- `HonorariosCirurgiao` prioriza a soma positiva dos `ValorReferencia` dos
  procedimentos; sem essa soma, usa o valor textual de `Pagamento`, se conversível,
  ou preserva o valor estruturado existente. `RepasseMedico` usa o valor informado
  em `Pagamento`. Por isso, honorários estimados e repasse podem divergir.
- Glosa não desfaz automaticamente quitação declarada. Diferença entre honorários,
  repasse e glosa exige conciliação contextual, não correção automática dos valores.

O frontend atual usa `statusPago=true` para `Pago`. Quando false, classifica
`Pendente` se há valor/texto de pagamento ou glosa e `Sem valor` caso contrário.
`pendingCount`, `missingAmountCount` e `recordsWithPendingItems` são métricas
distintas em `billingAnalytics.ts`. Se todos os registros estiverem marcados
quitados, a contagem de situação financeira pendente é zero mesmo com checklist
incompleto. `Pendente` deve ser explicado como **quitação não registrada**, sem
presumir inadimplência, vencimento ou valor em aberto não representados no contrato.

## Contrato compatível para frontend #171

Não foram adicionados campos, endpoints ou migrations. Os conceitos
`pendenciasFinanceiras` e `pendenciasCadastraisOuDocumentais` abaixo são semântica
de UI/documentação; **não são propriedades novas do JSON da API**.

| Conceito | Fonte atual e significado |
|---|---|
| Identificação do atendimento | `PacienteDto.id`; referência interna para o item exibido |
| Quitação declarada | `statusPago`; true produz `Pago` sem contar alertas documentais como dívida |
| Quitação não registrada | `statusPago=false`; motivo seguro para a UI, sem inventar recebimento/atraso/saldo |
| Data informada | `faturamento.dataPagamento`, nullable; falta da data não prova inadimplência |
| Honorários estimados/registrados | `faturamento.honorariosCirurgiao`; não é saldo nem soma dos recebimentos |
| Repasse informado | `faturamento.repasseMedico`, com origem textual em `pagamento`; não confirma quitação sozinho |
| Glosa informada | `faturamento.valorGlosa`, `glosaStatus`, `recursoGlosa`; mostrar sem alterar `statusPago` |
| Conferência declarada | `faturamento.conferenciaPagamentoRealizada`; divergência legada entre flags pede conciliação |
| Dados operacionais | Guias, TISS, procedimentos/códigos/portes, anexos e demais campos já disponíveis no DTO |
| Pendências operacionais | `billingChecklist`/`pendingChecklistItems` no frontend; podem ser vários por atendimento, inclusive pago |

O popup pode usar **o mesmo `BillingRecord.paciente` autorizado que compôs a
linha**, evitando nova consulta e diferença entre filtros. Um item de quitação
não registrada pode referenciar `id` + campo `statusPago`, com tipo/motivo/status
e descrição fixa segura definidos na UI; isso não é um ID de conta a receber.
Itens de checklist devem permanecer em seção separada, com seus próprios motivos.
Não incluir diagnóstico, tratamento, CPF ou dados desnecessários no texto de
pendência. Se `faturamento` for null, não inventar lançamentos, data ou zero monetário.

Na correção frontend #171, PDF, XLSX, popup e indicadores devem usar o mesmo
conjunto final de registros e filtros, com nomenclatura explícita de checklist.
Não substituir a coluna por um suposto número de devedores. Se houver atualização
concorrente entre páginas, o contrato atual não fornece snapshot transacional de
toda a exportação; não se promete consistência de snapshot entre requisições.

## Fluxo, autorização e filtros preservados

`reportData.loadReportData` e `billingPageUtils.loadBillingPatients` carregam todas
as páginas de `GET /api/faturamentos-medicos/` e constroem os registros no frontend.
O PDF observado não é produzido pelo worker de exportações da API.

- A lista médica, a lista de pacientes e o detalhe compartilham os campos de
  pagamento, mas não todos os escopos de acesso: médico vê faturamento apenas como
  cirurgião principal; pacientes também admitem vínculos auxiliares/grupo existentes.
- Admin/super/controller continuam limitados à clínica autenticada. Equipe depende
  dos membros ativos nos três vínculos médicos. Não aceitar `ClinicaId` como
  autoridade enviado pelo cliente nem usar nome do médico como autorização.
- `GET /api/pacientes/{id}` aplica `PacienteAccess` e tenant antes de mapear o DTO.
  Fora do escopo/clínica retorna 404, sem revelar campos financeiros.
- Faturamentos aceitam page/pageSize/search/medico/convenio/procedimento e competência.
  Escopo/filtros precedem contagem e paginação. A ordenação existente desempata por
  ID. `sortBy/sortDirection` não são parâmetros desse endpoint.
- Competência dessa lista usa o cadastro do faturamento ou do usuário legado,
  não a data de pagamento nem a competência armazenada pelo sync. Status, hospital
  e período de pagamento são filtrados no frontend após carregar as páginas.
  Nenhuma dessas regras foi alterada para a #176.

## Ledger financeiro é outro contrato

`Faturamento → ContaReceber → Recebimento` calcula recebimentos não estornados,
saldo aberto e estados aberto/parcial/recebido/vencido. Várias contas podem
representar parcelas. Esse fluxo **não atualiza automaticamente**
`Paciente.StatusPago` ou o faturamento médico usado no relatório observado.
Não converter um recebimento parcial em `Pago` nem atribuir lançamentos de um
contrato ao outro sem vínculo e regra de conciliação definidos.

Achado separado, não corrigido oportunisticamente: o handler
`Features/Financeiro/Relatorios/FinanceiroResumoUseCases.cs`,
`ObterPacienteFinanceiroResumoQueryHandler`, restringe paciente/médico, mas não
aplica o escopo de equipe. A clínica permanece filtrada pelo EF, porém a rota
aceita Equipe. **Não reutilizar esse endpoint para o popup da #171** sem tratar
essa autorização em tarefa própria. O popup pode utilizar o DTO já autorizado.

## Correção mínima comprovada

`FaturamentoMedicoSync.ParseCurrency` removia qualquer ponto: o texto aceito
`100.50` virava `10050`, também afetando glosa/repasse na escrita. Se o número
limpo não tem vírgula e tem um único ponto seguido de uma ou duas casas, agora
ele é decimal. Formatos brasileiros e interpretações legadas permanecem:
`R$ 1.234,56` → 1234.56; `1.000` → 1000; `1.234.567` → 1234567.

A ambiguidade de três casas após ponto mantém a precedência brasileira por
compatibilidade. O parser de fallback do outro módulo Financeiro não foi
substituído; não uniformizar `1.000` entre módulos silenciosamente. Demais
permissividades textuais legadas foram preservadas. Valores ausentes/não
conversíveis continuam null, sem criar zero, inferir quitação ou recalcular dados
existentes em lote. A correção só atua quando a sincronização normal é chamada.

## Verificação

Complexidade alta e risco alto; Database e Security (`high`) investigaram sem
editar. QA (`medium`) foi acionado, mas atingiu limite de uso antes de entregar
arquivos/testes. Principal implementou a correção, os testes e o relatório.
Modelos dos subagentes não identificados pelo runtime.

Antes da correção: `issue176-parser-red.trx`, **6 falhas/11 aprovados**,
demonstrando ponto decimal e efeitos em repasse/glosa.

Rodada direcionada final: `issue176-final-regression.trx`, **96 aprovados,
0 falhas e 1 ignorado**, incluindo os **31 casos novos** de sync, contrato e HTTP.
Cobertura: declaração quitada com checklist vazio, valores parciais sem inferência,
data e flags legadas divergentes, glosa, ausência de faturamento, equivalência
lista/detalhe, dois registros/páginas, isolamento/admin/super/equipe, tentativa
HTTP de trocar a clínica por query/header, recurso externo 404 e anônimo 401.
O teste ignorado foi
`PacienteCommandHandlerTests.DeletePaciente_SqlServer_EnforcesScopeAndCascadesOnlyAuthorizedPatient`:
requer `HEMODINKS_TEST_LOCALDB=1` para um SQL Server LocalDB descartável.

Comando da rodada direcionada:

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter 'FullyQualifiedName~Billing176_|FullyQualifiedName~FaturamentoMedicoFinancialSyncTests|FullyQualifiedName~FaturamentoFinancialContractTests|FullyQualifiedName~FaturamentoMedicoQueryHandlerTests|FullyQualifiedName~PacienteCommandHandler|FullyQualifiedName~FinanceiroCalculationsTests|FullyQualifiedName~ListOrderingTests|FullyQualifiedName~TenantAudit_|FullyQualifiedName~EndpointSecurityConventionTests' --logger 'trx;LogFileName=issue176-final-regression.trx' --results-directory out/issue-validation --verbosity quiet
```

Suíte completa executada uma vez: `issue176-complete.trx`, **939 aprovados,
2 falhas e 38 ignorados**, em 7m37s. As falhas foram:

- `LegacyFinancialBackfillMigrationTests.Migration_backfills_valid_legacy_values_and_audits_invalid_originals`;
- `EventReminderProcessorConcurrencyTests.ProcessDueReminders_ClaimsEventBeforeSendingAcrossReplicas`.

Ambas falharam durante migrations com `SQL Server Full-Text Search nao esta
instalado nesta instancia`, antes das assertions finais. A suíte completa não
está totalmente aprovada. Não se alterou a configuração do SQL Server nem se
suprimiram esses testes. Essa rodada precedeu os dois casos finais de flags
legadas divergentes; a rodada direcionada posterior compilou e aprovou todos os
31 casos novos, sem outra alteração no código de produção.

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-build --no-restore --logger 'trx;LogFileName=issue176-complete.trx' --results-directory out/issue-validation --verbosity quiet
```

Os contextos de dados dos testes novos são descartáveis e usam EF InMemory;
o caso HTTP exercita a aplicação via WebApplicationFactory. Não substituem a
validação de migrations/concorrência no SQL Server. Fixtures não contêm CPF,
nomes ou informações de pacientes reais. `git diff --check` passou; a revisão
dos arquivos novos não encontrou secrets nem dados sensíveis reais.

Sem alteração no ambiente de produção, nova dependência, migration, commit, push, merge ou
deploy. Ajuste visual/nomenclatura/popup e exportações pertencem à dependência
frontend #171 e não foram implementados no repositório API.
