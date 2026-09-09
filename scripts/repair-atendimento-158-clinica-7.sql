-- Correcao confirmada pelo responsavel: atendimento 158 / paciente 173, clinica correta 7.
-- Executar inteiro no HemodinksDB. Nao altera outros atendimentos nem move cadastros compartilhados.
-- Se houver dependencias incompativeis, retorna os IDs e cancela sem gravar.
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.AtendimentosCirurgicos WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = 158 AND PacienteId = 173 AND ClinicaId IN (1, 7)
    )
        THROW 51000, 'Atendimento 158 nao corresponde ao estado esperado. Nenhuma correcao aplicada.', 1;

    IF NOT EXISTS (
        SELECT 1 FROM dbo.Pacientes WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = 173 AND ClinicaId = 7
    )
        THROW 51000, 'Paciente 173 precisa pertencer a clinica 7. Nenhuma correcao aplicada.', 1;

    DECLARE @Pendencias TABLE (Tabela sysname, RegistroId int, ClinicaAtual int NULL);

    INSERT INTO @Pendencias
    SELECT N'Users', vinculo.Id, usuario.ClinicaId
    FROM dbo.AtendimentosCirurgicos AS atendimento
    CROSS APPLY (VALUES (atendimento.MedicoResponsavelId),
                        (atendimento.MedicoAuxiliar1Id), (atendimento.MedicoAuxiliar2Id)) AS vinculo(Id)
    LEFT JOIN dbo.Users AS usuario WITH (HOLDLOCK) ON usuario.Id = vinculo.Id
    WHERE atendimento.Id = 158 AND vinculo.Id IS NOT NULL
      AND (usuario.Id IS NULL OR usuario.ClinicaId <> 7)
    UNION ALL
    SELECT N'Hospitais', a.HospitalId, h.ClinicaId
    FROM dbo.AtendimentosCirurgicos a
    LEFT JOIN dbo.Hospitais h WITH (HOLDLOCK) ON h.Id = a.HospitalId
    WHERE a.Id = 158 AND a.HospitalId IS NOT NULL AND (h.Id IS NULL OR h.ClinicaId <> 7)
    UNION ALL
    SELECT N'Convenios', a.ConvenioId, c.ClinicaId
    FROM dbo.AtendimentosCirurgicos a
    LEFT JOIN dbo.Convenios c WITH (HOLDLOCK) ON c.IdConvenio = a.ConvenioId
    WHERE a.Id = 158 AND a.ConvenioId IS NOT NULL AND (c.IdConvenio IS NULL OR c.ClinicaId <> 7)
    UNION ALL
    SELECT N'OPME', a.OpmeFornecedorId, o.ClinicaId
    FROM dbo.AtendimentosCirurgicos a
    LEFT JOIN dbo.OPME o WITH (HOLDLOCK) ON o.IdFornecedor = a.OpmeFornecedorId
    WHERE a.Id = 158 AND a.OpmeFornecedorId IS NOT NULL AND (o.IdFornecedor IS NULL OR o.ClinicaId <> 7)
    UNION ALL
    SELECT N'AtendimentoProcedimentos', Id, ClinicaId
    FROM dbo.AtendimentoProcedimentos WITH (UPDLOCK, HOLDLOCK)
    WHERE AtendimentoCirurgicoId = 158 AND ClinicaId <> 7
    UNION ALL
    SELECT N'AtendimentoArquivos', Id, ClinicaId
    FROM dbo.AtendimentoArquivos WITH (UPDLOCK, HOLDLOCK)
    WHERE AtendimentoCirurgicoId = 158 AND ClinicaId <> 7
    UNION ALL
    SELECT N'Faturamentos', Id, ClinicaId
    FROM dbo.Faturamentos WITH (UPDLOCK, HOLDLOCK)
    WHERE AtendimentoCirurgicoId = 158 AND ClinicaId <> 7;

    IF EXISTS (SELECT 1 FROM @Pendencias)
    BEGIN
        SELECT DISTINCT Tabela, RegistroId, ClinicaAtual FROM @Pendencias ORDER BY Tabela, RegistroId;
        THROW 51000, 'Existem vinculos fora da clinica 7. Envie o resultado para ajustar os vinculos; nenhuma alteracao foi gravada.', 1;
    END;

    UPDATE dbo.AtendimentosCirurgicos
    SET ClinicaId = 7, DataAtualizacao = SYSUTCDATETIME()
    WHERE Id = 158 AND PacienteId = 173 AND ClinicaId = 1;

    COMMIT TRANSACTION;

    SELECT Id AS AtendimentoId, PacienteId, ClinicaId,
           N'Clinica 7 confirmada' AS Resultado
    FROM dbo.AtendimentosCirurgicos WHERE Id = 158;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
