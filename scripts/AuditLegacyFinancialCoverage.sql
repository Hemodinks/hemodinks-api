:on error exit
SET NOCOUNT ON;

IF DB_NAME() NOT LIKE N'HemodinksProdAnon[_]%'
    THROW 51000, 'Este script so pode ser executado em uma copia HemodinksProdAnon_*.', 1;

WITH Legado AS (
    SELECT
        p.Id AS PacienteId,
        p.Data,
        p.MedicoUserId,
        p.OpmeFornecedorId AS OpmePaciente,
        a.OpmeFornecedorId AS OpmeAtendimento,
        TRY_CONVERT(decimal(18,2),
            CASE
                WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.Pagamento, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                    THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), '.', ''), ',', '.')
                ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.Pagamento)), 'R$', ''), NCHAR(160), '')
            END) AS PagamentoPaciente,
        TRY_CONVERT(decimal(18,2),
            CASE
                WHEN LTRIM(RTRIM(REPLACE(REPLACE(p.RepasseGlosa, 'R$', ''), NCHAR(160), ''))) LIKE '%,%'
                    THEN REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), '.', ''), ',', '.')
                ELSE REPLACE(REPLACE(LTRIM(RTRIM(p.RepasseGlosa)), 'R$', ''), NCHAR(160), '')
            END) AS GlosaPaciente,
        COALESCE(fm.HonorariosCirurgiao, 0)
            + COALESCE(fm.HonorariosAuxiliares, 0)
            + COALESCE(fm.HonorariosAnestesista, 0) AS ValorFaturamentoMedico,
        COALESCE(fm.ValorGlosa, 0) AS GlosaFaturamentoMedico
    FROM dbo.Pacientes p
    LEFT JOIN dbo.FaturamentosMedicos fm
        ON fm.ClinicaId = p.ClinicaId AND fm.PacienteId = p.Id
    LEFT JOIN dbo.AtendimentosCirurgicos a
        ON a.ClinicaId = p.ClinicaId AND a.PacienteId = p.Id
)
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN Data IS NULL AND MedicoUserId IS NULL THEN 1 ELSE 0 END) AS SemDataEMedico,
    SUM(CASE WHEN Data IS NULL AND MedicoUserId IS NOT NULL THEN 1 ELSE 0 END) AS SomenteSemData,
    SUM(CASE WHEN Data IS NOT NULL AND MedicoUserId IS NULL THEN 1 ELSE 0 END) AS SomenteSemMedico,
    SUM(CASE WHEN Data IS NOT NULL AND MedicoUserId IS NOT NULL THEN 1 ELSE 0 END) AS Elegiveis,
    SUM(CASE WHEN ValorFaturamentoMedico > 0 THEN 1 ELSE 0 END) AS ComValorNoFaturamentoMedico,
    SUM(CASE WHEN PagamentoPaciente > 0 THEN 1 ELSE 0 END) AS ComValorNoPaciente,
    SUM(CASE WHEN ValorFaturamentoMedico <= 0 AND PagamentoPaciente > 0 THEN 1 ELSE 0 END)
        AS ValorPacienteIgnoradoPelaRegraAtual,
    SUM(CASE WHEN GlosaFaturamentoMedico <= 0 AND GlosaPaciente > 0 THEN 1 ELSE 0 END)
        AS GlosaPacienteIgnoradaPelaRegraAtual,
    SUM(CASE WHEN OpmePaciente IS NOT NULL AND OpmeAtendimento IS NULL THEN 1 ELSE 0 END)
        AS OpmeNaoCopiadaParaAtendimento
FROM Legado;
