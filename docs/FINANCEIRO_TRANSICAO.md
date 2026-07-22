# Transição do faturamento e contas a receber

## Fontes oficiais

- `AtendimentosCirurgicos` e `AtendimentoProcedimentos`: dados clínicos e snapshot dos preços na data da cirurgia.
- `Faturamentos`, `FaturamentoItens`, `Glosas` e `RecursosGlosa`: valores cobrados, reconhecidos e recuperados.
- `ContasReceber` e `Recebimentos`: valores efetivamente recebidos, saldo, conciliação e estorno.
- `ConvenioProcedimentoPrecos`: preços contratuais; não pertence ao financeiro.

O cadastro de paciente não executa mais `FaturamentoMedicoSync`.

## O que é legado, exatamente

- `FaturamentosMedicos` é a tabela financeira legada. Ela concentra honorários, guias, glosa, recurso, conferência de pagamento e repasse em um registro por paciente.
- `PacienteProcedimentos` é a associação clínica legada de procedimentos diretamente ao paciente. No fluxo novo, o snapshot oficial pertence a `AtendimentoProcedimentos`. A tabela só poderá ser desativada quando nenhum outro módulo clínico depender dela.
- `Pacientes` **não é uma tabela legada e não deve ser removida**. Apenas estes campos antigos deixam de ser fonte oficial do atendimento/faturamento: `Data`, `Diagnostico`, `TratamentoMedico`, `HospitalId`, `Hospital`, `MedicoUserId`, `Medico`, `MedicoAuxiliar1UserId`, `MedicoAuxiliar1`, `MedicoAuxiliar2UserId`, `MedicoAuxiliar2`, `ConvenioId`, `Convenio`, `OpmeFornecedorId`, `OpmeFornecedor`, `CbhpmCodigo`, `CbhpmPorte`, `Procedimento`, `Autorizacao`, `Pagamento`, `RepasseGlosa` e `StatusPago`.
- `PacienteArquivos`, dados cadastrais e relacionamentos de acesso do paciente continuam ativos; não fazem parte da retirada financeira.

## Migration

A migration `20260722181233_AddNormalizedBillingAndReceivables` é aditiva. Ela:

1. cria as tabelas, índices, chaves e restrições do novo modelo;
2. copia atendimentos legados que têm data e médico válidos;
3. copia procedimentos como snapshot;
4. converte o faturamento legado para itens normalizados;
5. converte glosas e pagamentos conferidos, marcando-os como dados migrados que exigem conciliação;
6. não remove nem altera as tabelas antigas.

Em 22/07/2026, a cadeia completa até `20260722181233_AddNormalizedBillingAndReceivables` foi aplicada com sucesso em um SQL Server LocalDB descartável e o banco foi removido após o teste. Isso valida o SQL gerado e o caminho de banco vazio; não substitui o ensaio com cópia anonimizada da homologação.

Não aplicar automaticamente em produção. Antes da implantação:

1. gerar e revisar o script SQL com `scripts/Export-MigrationScripts.ps1`;
2. restaurar um backup recente em homologação;
3. executar a migration em homologação e comparar contagens e totais por clínica;
4. revisar títulos com prefixo `LEG-FAT-` e recebimentos com forma `Outro`;
5. validar rollback e backup;
6. agendar a execução em produção com observabilidade e plano de retorno.

## Regras protegidas no backend

- valores monetários usam `decimal(18,2)`;
- os itens calculam valor apresentado por quantidade, peso e valor unitário;
- faturamentos e contas usam `rowversion`;
- títulos são idempotentes por clínica e número de documento;
- a soma das parcelas não pode exceder o faturamento;
- recebimentos acima do saldo são recusados;
- estornos preservam o lançamento e recalculam saldo e status;
- alterações de glosa reconciliam o valor ajustado dos títulos sem permitir valor abaixo do já recebido;
- vigências sobrepostas de preço são recusadas pelo caso de uso e inícios duplicados têm índice único;
- todas as novas entidades são filtradas e validadas por `ClinicaId`.

## Plano para desativar o legado

1. **Observação:** manter o legado somente para leitura, medir chamadas aos endpoints antigos e comparar por clínica as contagens e os totais migrados.
2. **Conciliação:** resolver todos os registros `LEG-FAT-*`, divergências de glosa/pagamento e atendimentos que não puderam ser migrados por falta de data ou médico válido.
3. **Corte de escrita:** remover permissões e telas que escrevem em `FaturamentosMedicos`, nos campos clínico-financeiros antigos de `Pacientes` e em `PacienteProcedimentos`; manter uma janela de rollback somente leitura.
4. **Corte de leitura:** trocar relatórios e integrações restantes para as tabelas normalizadas e acompanhar por pelo menos um ciclo financeiro completo, com zero acesso ao legado.
5. **Arquivo:** exportar por clínica as tabelas/colunas antigas, registrar hash, contagens, totais, data de retenção e local do backup. Validar restauração antes da remoção.
6. **Remoção de código:** excluir `FaturamentoMedico`, `FaturamentoMedicoSync`, endpoints/queries antigos e propriedades obsoletas dos contratos. Remover `PacienteProcedimentos` apenas após confirmar que não há uso clínico fora do financeiro.
7. **Migration destrutiva separada:** somente depois da aprovação operacional, criar uma nova migration para remover `FaturamentosMedicos`, as colunas antigas listadas de `Pacientes` e, se liberada, `PacienteProcedimentos`. Nunca misturar essa remoção com a migration aditiva atual.
8. **Produção:** exigir backup/PITR testado, janela de manutenção, script revisado, métricas pós-corte e plano de restauração. A migration destrutiva não deve ser executada automaticamente no startup.

Critério mínimo para avançar à etapa destrutiva: 100% dos registros elegíveis conciliados, zero escrita legada, zero leitura legada por um ciclo completo, exportação restaurável aprovada e aceite formal do responsável financeiro.
