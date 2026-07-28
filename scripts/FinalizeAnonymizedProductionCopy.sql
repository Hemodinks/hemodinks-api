:on error exit
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'HemodinksProdRaw[_]%'
    THROW 51000, 'Este script so pode ser executado em uma copia HemodinksProdRaw_*.', 1;

BEGIN TRANSACTION;

UPDATE dbo.Pacientes SET
    Procedimento = CASE WHEN Procedimento IS NULL THEN NULL
        ELSE CONCAT(N'Procedimento Teste Paciente ', Id) END,
    Diagnostico = CASE WHEN Diagnostico IS NULL THEN NULL ELSE N'Diagnóstico anonimizado' END,
    TratamentoMedico = CASE WHEN TratamentoMedico IS NULL THEN NULL ELSE N'Tratamento anonimizado' END;

COMMIT TRANSACTION;

DECLARE @Falhas int = 0;

SELECT @Falhas += COUNT(*) FROM dbo.Users
WHERE Nome NOT LIKE N'Usuário Teste %'
   OR Email NOT LIKE N'usuario%@example.invalid'
   OR Telefone NOT LIKE N'819%'
   OR Senha NOT LIKE N'PBKDF2-SHA256$210000$%';

SELECT @Falhas += COUNT(*) FROM dbo.Pacientes
WHERE NomePaciente NOT LIKE N'Paciente Teste %'
   OR (Procedimento IS NOT NULL AND Procedimento NOT LIKE N'Procedimento Teste Paciente %')
   OR (Diagnostico IS NOT NULL AND Diagnostico <> N'Diagnóstico anonimizado')
   OR (TratamentoMedico IS NOT NULL AND TratamentoMedico <> N'Tratamento anonimizado');

SELECT @Falhas += COUNT(*) FROM dbo.Observacoes
WHERE Texto NOT LIKE N'Observação anonimizada %';

SELECT @Falhas += COUNT(*) FROM dbo.PacienteArquivos
WHERE Url NOT LIKE N'https://example.invalid/%';

SELECT @Falhas += COUNT(*) FROM dbo.UserArquivos
WHERE Url NOT LIKE N'https://example.invalid/%';

SELECT @Falhas += COUNT(*) FROM dbo.PasswordResetTokens;
SELECT @Falhas += COUNT(*) FROM dbo.IdempotencyRequests;

IF @Falhas > 0
    THROW 51001, 'A validacao da anonimizacao encontrou campos pendentes.', 1;

DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
DBCC CHECKDB (N'HemodinksProdRaw_20260728') WITH NO_INFOMSGS;

SELECT
    N'VALIDATION_OK' AS Resultado,
    (SELECT COUNT(*) FROM dbo.Users) AS Usuarios,
    (SELECT COUNT(*) FROM dbo.Pacientes) AS Pacientes,
    (SELECT COUNT(*) FROM dbo.FaturamentosMedicos) AS Faturamentos;
