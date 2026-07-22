# Transição do faturamento e contas a receber

## Fontes oficiais

- `AtendimentosCirurgicos` e `AtendimentoProcedimentos`: dados clínicos e snapshot dos preços na data da cirurgia.
- `Faturamentos`, `FaturamentoItens`, `Glosas` e `RecursosGlosa`: valores cobrados, reconhecidos e recuperados.
- `ContasReceber` e `Recebimentos`: valores efetivamente recebidos, saldo, conciliação e estorno.
- `ConvenioProcedimentoPrecos`: preços contratuais; não pertence ao financeiro.

`Pacientes`, `PacienteProcedimentos` e `FaturamentosMedicos` permanecem durante a transição para leitura e compatibilidade. O cadastro de paciente não executa mais `FaturamentoMedicoSync`.

## Migration

A migration `20260722181233_AddNormalizedBillingAndReceivables` é aditiva. Ela:

1. cria as tabelas, índices, chaves e restrições do novo modelo;
2. copia atendimentos legados que têm data e médico válidos;
3. copia procedimentos como snapshot;
4. converte o faturamento legado para itens normalizados;
5. converte glosas e pagamentos conferidos, marcando-os como dados migrados que exigem conciliação;
6. não remove nem altera as tabelas antigas.

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
