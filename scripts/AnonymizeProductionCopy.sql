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

UPDATE dbo.Users SET
    Nome = CONCAT(N'Usuário Teste ', Id),
    Telefone = CONCAT(N'819', RIGHT(CONCAT(N'00000000', Id), 8)),
    Email = CONCAT(N'usuario', Id, N'@example.invalid'),
    Senha = N'$(LocalPasswordHash)',
    DataNascimento = CASE WHEN DataNascimento IS NULL THEN NULL
        ELSE DATEFROMPARTS(1970 + (Id % 31), 1 + (Id % 12), 1 + (Id % 28)) END,
    PrecisaTrocarSenha = 0,
    FotoPerfil = NULL,
    Cpf = CASE WHEN Cpf IS NULL THEN NULL
        ELSE CONCAT(N'900', RIGHT(CONCAT(N'00000000', Id), 8)) END,
    Crm = CASE WHEN Crm IS NULL THEN NULL ELSE CONCAT(N'TESTE', Id) END,
    CrmUf = CASE WHEN CrmUf IS NULL THEN NULL ELSE N'PE' END;

UPDATE dbo.Pacientes SET
    NomePaciente = CONCAT(N'Paciente Teste ', Id),
    Data = CASE WHEN Data IS NULL THEN NULL ELSE DATEADD(DAY, (Id % 61) - 30, Data) END,
    Hospital = CASE WHEN Hospital IS NULL THEN NULL
        ELSE CONCAT(N'Hospital Teste ', COALESCE(HospitalId, Id)) END,
    Medico = CASE WHEN Medico IS NULL THEN NULL
        ELSE CONCAT(N'Médico Teste ', COALESCE(MedicoUserId, Id)) END,
    Convenio = CASE WHEN Convenio IS NULL THEN NULL
        ELSE CONCAT(N'Convênio Teste ', COALESCE(ConvenioId, Id)) END,
    Procedimento = CASE WHEN Procedimento IS NULL THEN NULL
        ELSE CONCAT(N'Procedimento Teste Paciente ', Id) END,
    Autorizacao = CASE WHEN Autorizacao IS NULL THEN NULL ELSE CONCAT(N'AUT-TESTE-', Id) END,
    MedicoAuxiliar1 = CASE WHEN MedicoAuxiliar1 IS NULL THEN NULL
        ELSE CONCAT(N'Auxiliar Teste ', COALESCE(MedicoAuxiliar1UserId, Id)) END,
    MedicoAuxiliar2 = CASE WHEN MedicoAuxiliar2 IS NULL THEN NULL
        ELSE CONCAT(N'Auxiliar Teste ', COALESCE(MedicoAuxiliar2UserId, Id)) END,
    Diagnostico = CASE WHEN Diagnostico IS NULL THEN NULL ELSE N'Diagnóstico anonimizado' END,
    TratamentoMedico = CASE WHEN TratamentoMedico IS NULL THEN NULL ELSE N'Tratamento anonimizado' END,
    OpmeFornecedor = CASE WHEN OpmeFornecedor IS NULL THEN NULL
        ELSE CONCAT(N'Fornecedor Teste ', COALESCE(OpmeFornecedorId, Id)) END;

UPDATE dbo.Clinicas SET Nome = N'Clínica Teste Local', Slug = N'hemodinks-teste-local';
UPDATE dbo.ConfiguracoesSistema SET NomeEmpresa = N'Clínica Teste Local', FotoEmpresa = NULL;
UPDATE dbo.Convenios SET DescricaoConvenio = CONCAT(N'Convênio Teste ', IdConvenio);
UPDATE dbo.Hospitais SET Nome = CONCAT(N'Hospital Teste ', Id);
UPDATE dbo.OPME SET Fornecedor = CONCAT(N'Fornecedor Teste ', IdFornecedor);
UPDATE dbo.GruposMedicos SET Nome = CONCAT(N'Grupo Médico Teste ', Id);

UPDATE dbo.Events SET
    Title = CONCAT(N'Evento Teste ', Id),
    Description = CASE WHEN Description IS NULL THEN NULL
        ELSE N'Descrição anonimizada para testes locais.' END,
    Start = DATEADD(DAY, (Id % 61) - 30, Start),
    [End] = DATEADD(DAY, (Id % 61) - 30, [End]),
    LastReminderSentAt = CASE WHEN LastReminderSentAt IS NULL THEN NULL
        ELSE DATEADD(DAY, (Id % 61) - 30, LastReminderSentAt) END,
    NextReminderAt = CASE WHEN NextReminderAt IS NULL THEN NULL
        ELSE DATEADD(DAY, (Id % 61) - 30, NextReminderAt) END,
    CompletedAt = CASE WHEN CompletedAt IS NULL THEN NULL
        ELSE DATEADD(DAY, (Id % 61) - 30, CompletedAt) END;

UPDATE dbo.AgendaNotifications SET
    Title = CONCAT(N'Notificação Teste ', Id),
    Message = N'Conteúdo anonimizado para testes locais.';

UPDATE dbo.Observacoes SET
    Texto = CONCAT(N'Observação anonimizada ', Id),
    Medico = CASE WHEN Medico IS NULL THEN NULL
        ELSE CONCAT(N'Médico Teste ', COALESCE(MedicoUserId, Id)) END,
    MedicoAuxiliar1 = CASE WHEN MedicoAuxiliar1 IS NULL THEN NULL
        ELSE CONCAT(N'Auxiliar Teste ', COALESCE(MedicoAuxiliar1UserId, Id)) END,
    MedicoAuxiliar2 = CASE WHEN MedicoAuxiliar2 IS NULL THEN NULL
        ELSE CONCAT(N'Auxiliar Teste ', COALESCE(MedicoAuxiliar2UserId, Id)) END;

UPDATE dbo.FaturamentosMedicos SET
    Anestesista = CASE WHEN Anestesista IS NULL THEN NULL ELSE CONCAT(N'Anestesista Teste ', Id) END,
    GuiaAutorizacaoConvenio = CASE WHEN GuiaAutorizacaoConvenio IS NULL THEN NULL
        ELSE CONCAT(N'GUIA-AUT-TESTE-', Id) END,
    GuiaInternacaoOuSadt = CASE WHEN GuiaInternacaoOuSadt IS NULL THEN NULL
        ELSE CONCAT(N'GUIA-SADT-TESTE-', Id) END,
    OpmeMateriaisEspeciais = CASE WHEN OpmeMateriaisEspeciais IS NULL THEN NULL
        ELSE N'Material especial anonimizado' END,
    RecursoGlosa = CASE WHEN RecursoGlosa IS NULL THEN NULL ELSE N'Recurso de glosa anonimizado' END,
    RepasseMedicoObservacao = CASE WHEN RepasseMedicoObservacao IS NULL THEN NULL
        ELSE N'Observação de repasse anonimizada' END,
    ReciboNotaContrato = CASE WHEN ReciboNotaContrato IS NULL THEN NULL ELSE CONCAT(N'DOC-TESTE-', Id) END,
    Observacoes = CASE WHEN Observacoes IS NULL THEN NULL ELSE N'Observação financeira anonimizada' END,
    DataCadastro = DATEADD(DAY, (PacienteId % 61) - 30, DataCadastro),
    DataAtualizacao = CASE WHEN DataAtualizacao IS NULL THEN NULL
        ELSE DATEADD(DAY, (PacienteId % 61) - 30, DataAtualizacao) END,
    CompetenciaInicio = CASE WHEN CompetenciaInicio IS NULL THEN NULL
        ELSE DATEADD(DAY, (PacienteId % 61) - 30, CompetenciaInicio) END,
    CompetenciaFinal = CASE WHEN CompetenciaFinal IS NULL THEN NULL
        ELSE DATEADD(DAY, (PacienteId % 61) - 30, CompetenciaFinal) END,
    HonorariosCirurgiao = CASE WHEN HonorariosCirurgiao IS NULL THEN NULL
        ELSE ROUND(HonorariosCirurgiao * (0.90 + ((PacienteId % 21) / 100.0)), 2) END,
    HonorariosAuxiliares = CASE WHEN HonorariosAuxiliares IS NULL THEN NULL
        ELSE ROUND(HonorariosAuxiliares * (0.90 + ((PacienteId % 21) / 100.0)), 2) END,
    HonorariosAnestesista = CASE WHEN HonorariosAnestesista IS NULL THEN NULL
        ELSE ROUND(HonorariosAnestesista * (0.90 + ((PacienteId % 21) / 100.0)), 2) END,
    ValorGlosa = CASE WHEN ValorGlosa IS NULL THEN NULL
        ELSE ROUND(ValorGlosa * (0.90 + ((PacienteId % 21) / 100.0)), 2) END,
    RepasseMedico = CASE WHEN RepasseMedico IS NULL THEN NULL
        ELSE ROUND(RepasseMedico * (0.90 + ((PacienteId % 21) / 100.0)), 2) END;

UPDATE dbo.PacienteProcedimentos SET
    Procedimento = CONCAT(N'Procedimento Teste ', Id),
    ValorReferencia = CASE WHEN ValorReferencia IS NULL THEN NULL
        ELSE ROUND(ValorReferencia * (0.90 + ((PacienteId % 21) / 100.0)), 2) END;

UPDATE dbo.PacienteArquivos SET
    NomeOriginal = CONCAT(N'arquivo-paciente-', Id, N'.dat'),
    ContentType = N'application/octet-stream',
    Url = CONCAT(N'https://example.invalid/pacientes/arquivo-', Id);

UPDATE dbo.UserArquivos SET
    NomeOriginal = CONCAT(N'arquivo-usuario-', Id, N'.dat'),
    ContentType = N'application/octet-stream',
    Url = CONCAT(N'https://example.invalid/usuarios/arquivo-', Id);

UPDATE dbo.Licencas SET Observacoes = CASE WHEN Observacoes IS NULL THEN NULL
    ELSE N'Observação de licença anonimizada' END;

DELETE FROM dbo.PasswordResetTokens;
DELETE FROM dbo.IdempotencyRequests;

COMMIT TRANSACTION;
