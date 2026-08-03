:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'HemodinksProdAnon[_]%'
    THROW 51000, 'Este script so pode ser executado em uma copia HemodinksProdAnon_*.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260723205811_AddAtendimentoGlosa')
    THROW 51010, 'A ultima migration esperada nao foi aplicada.', 1;

IF EXISTS (
    SELECT p.ClinicaId, p.Id
    FROM dbo.Pacientes p
    INNER JOIN dbo.Users u ON u.Id = p.UserId AND u.ClinicaId = p.ClinicaId
    WHERE p.Data IS NOT NULL
      AND p.MedicoUserId IS NOT NULL
      AND NOT EXISTS (
          SELECT 1 FROM dbo.AtendimentosCirurgicos a
          WHERE a.ClinicaId = p.ClinicaId AND a.PacienteId = p.Id))
    THROW 51011, 'Existem pacientes elegiveis sem atendimento migrado.', 1;

IF EXISTS (
    SELECT ClinicaId, PacienteId
    FROM dbo.AtendimentosCirurgicos
    GROUP BY ClinicaId, PacienteId
    HAVING COUNT(*) > 1)
    THROW 51012, 'A migration criou atendimentos duplicados para o mesmo paciente.', 1;

IF EXISTS (
    SELECT fm.ClinicaId, fm.PacienteId
    FROM dbo.FaturamentosMedicos fm
    INNER JOIN dbo.AtendimentosCirurgicos a
        ON a.ClinicaId = fm.ClinicaId AND a.PacienteId = fm.PacienteId
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Faturamentos f
        WHERE f.ClinicaId = fm.ClinicaId AND f.AtendimentoCirurgicoId = a.Id))
    THROW 51013, 'Existem faturamentos medicos elegiveis sem faturamento novo.', 1;

IF EXISTS (
    SELECT ClinicaId, PacienteId
    FROM dbo.FaturamentosMedicos
    GROUP BY ClinicaId, PacienteId
    HAVING COUNT(*) > 1)
    THROW 51014, 'Ha mais de um faturamento legado por paciente; a regra atual consolida apenas um.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Faturamentos f
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.FaturamentoItens i WHERE i.FaturamentoId = f.Id))
    THROW 51015, 'Existem faturamentos novos sem itens.', 1;

IF EXISTS (
    SELECT 1
    FROM dbo.Faturamentos f
    WHERE f.ValorApresentado > 0
      AND NOT EXISTS (
          SELECT 1 FROM dbo.ContasReceber c WHERE c.FaturamentoId = f.Id))
    THROW 51016, 'Existem faturamentos positivos sem conta a receber.', 1;

IF EXISTS (
    SELECT pp.ClinicaId, pp.PacienteId
    FROM dbo.PacienteProcedimentos pp
    INNER JOIN dbo.AtendimentosCirurgicos a
        ON a.ClinicaId = pp.ClinicaId AND a.PacienteId = pp.PacienteId
    GROUP BY pp.ClinicaId, pp.PacienteId, a.Id
    HAVING COUNT(*) <> (
        SELECT COUNT(*)
        FROM dbo.AtendimentoProcedimentos ap
        WHERE ap.AtendimentoCirurgicoId = a.Id))
    THROW 51017, 'A quantidade de procedimentos migrados diverge do legado.', 1;

DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
DBCC CHECKDB (N'HemodinksProdAnon_20260728') WITH NO_INFOMSGS;

SELECT N'Migrations' AS Entidade, COUNT_BIG(*) AS Registros FROM dbo.__EFMigrationsHistory
UNION ALL SELECT N'Pacientes legados', COUNT_BIG(*) FROM dbo.Pacientes
UNION ALL SELECT N'Atendimentos novos', COUNT_BIG(*) FROM dbo.AtendimentosCirurgicos
UNION ALL SELECT N'Faturamentos médicos legados', COUNT_BIG(*) FROM dbo.FaturamentosMedicos
UNION ALL SELECT N'Faturamentos novos', COUNT_BIG(*) FROM dbo.Faturamentos
UNION ALL SELECT N'Itens de faturamento', COUNT_BIG(*) FROM dbo.FaturamentoItens
UNION ALL SELECT N'Contas a receber', COUNT_BIG(*) FROM dbo.ContasReceber
UNION ALL SELECT N'Recebimentos', COUNT_BIG(*) FROM dbo.Recebimentos
UNION ALL SELECT N'Glosas', COUNT_BIG(*) FROM dbo.Glosas
UNION ALL SELECT N'Inconsistências para conciliação', COUNT_BIG(*) FROM dbo.FinanceiroMigracaoInconsistencias;
