-- Issue #184: logical integrity of an isolated, quiescent restored copy ONLY.
-- Schema contract: 20261019121529_Schema_AddSurgeryType and the current EF model.
-- Run in a dedicated connection after setting SESSION_CONTEXT key
-- Restore184ExpectedDatabase to the exact restored database name (nvarchar).
-- Require database SELECT + VIEW DEFINITION. Execute with a client that exits on
-- SQL errors (sqlcmd -b). Only aggregate violation counts are returned.
-- No application rows/schema are changed; table variables are session-local.
-- This is NOT physical CHECKDB, complete schema equivalence, a row-count baseline,
-- or authorization/session-revocation validation. Older snapshots must first be
-- evaluated against their matching application/schema, never approved by this file.
-- CHECKs are verified by name/table, enabled/trusted state and live expression
-- violations; expression equivalence to the EF source is NOT established here.
-- Simple-FK tenant comparison is limited to the verified membership/user ownership
-- relation. Other ClinicaId properties may have distinct domain meanings.
SET NOCOUNT ON;

DECLARE @expected nvarchar(4000) = TRY_CONVERT(nvarchar(4000), SESSION_CONTEXT(N'Restore184ExpectedDatabase'));
IF @expected IS NULL
   OR DB_NAME() COLLATE Latin1_General_100_BIN2 NOT LIKE N'restore184-%'
   OR DATALENGTH(DB_NAME()) <= DATALENGTH(N'restore184-')
   OR @expected COLLATE Latin1_General_100_BIN2 <> DB_NAME() COLLATE Latin1_General_100_BIN2
   OR DATALENGTH(@expected) <> DATALENGTH(DB_NAME())
    THROW 51840, 'Restore verification refused: isolated database and exact session confirmation required.', 1;

IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION'), 0) <> 1
   OR COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SELECT'), 0) <> 1
    THROW 51841, 'Restore verification refused: complete read and metadata permissions required.', 1;

BEGIN TRY
    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
       OR COL_LENGTH(N'dbo.__EFMigrationsHistory', N'MigrationId') IS NULL
        THROW 51841, 'Restore verification refused: required current schema is missing or incompatible.', 1;
    DECLARE @currentSchema bit;
    EXEC sys.sp_executesql
        N'SELECT @valid = CASE WHEN MAX(MigrationId) = N''20261019121529_Schema_AddSurgeryType'' THEN 1 ELSE 0 END FROM dbo.__EFMigrationsHistory;',
        N'@valid bit OUTPUT', @valid = @currentSchema OUTPUT;
    IF COALESCE(@currentSchema, 0) <> 1
        THROW 51841, 'Restore verification refused: required current schema is missing or incompatible.', 1;

    -- Explicit FK manifest, including the 31 patient/user tenant relationships,
    -- subsequent event-mail/creator relationships and platform identity links.
    -- A model-level test prevents omissions when the EF schema changes.
    DECLARE @foreignKeys TABLE
    (Name sysname PRIMARY KEY, ChildTable sysname, ParentTable sysname,
     ChildColumns nvarchar(1000), ParentColumns nvarchar(1000));
    INSERT @foreignKeys VALUES
(N'FK_AgendaNotifications_Clinicas_ClinicaId', N'AgendaNotifications', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_AgendaNotifications_Events_EventId', N'AgendaNotifications', N'Events', N'EventId', N'Id'),
(N'FK_AgendaNotifications_Users_ClinicaId_RecipientUserId', N'AgendaNotifications', N'Users', N'ClinicaId,RecipientUserId', N'ClinicaId,Id'),
(N'FK_AgendaNotifications_Users_ClinicaId_SenderUserId', N'AgendaNotifications', N'Users', N'ClinicaId,SenderUserId', N'ClinicaId,Id'),
(N'FK_AtendimentoArquivos_AtendimentosCirurgicos_AtendimentoCirurgicoId', N'AtendimentoArquivos', N'AtendimentosCirurgicos', N'AtendimentoCirurgicoId', N'Id'),
(N'FK_AtendimentoArquivos_Clinicas_ClinicaId', N'AtendimentoArquivos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_AtendimentoProcedimentos_AtendimentosCirurgicos_AtendimentoCirurgicoId', N'AtendimentoProcedimentos', N'AtendimentosCirurgicos', N'AtendimentoCirurgicoId', N'Id'),
(N'FK_AtendimentoProcedimentos_Clinicas_ClinicaId', N'AtendimentoProcedimentos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_AtendimentosCirurgicos_Clinicas_ClinicaId', N'AtendimentosCirurgicos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_AtendimentosCirurgicos_Convenios_ConvenioId', N'AtendimentosCirurgicos', N'Convenios', N'ConvenioId', N'IdConvenio'),
(N'FK_AtendimentosCirurgicos_Hospitais_HospitalId', N'AtendimentosCirurgicos', N'Hospitais', N'HospitalId', N'Id'),
(N'FK_AtendimentosCirurgicos_OPME_OpmeFornecedorId', N'AtendimentosCirurgicos', N'OPME', N'OpmeFornecedorId', N'IdFornecedor'),
(N'FK_AtendimentosCirurgicos_Pacientes_ClinicaId_PacienteId', N'AtendimentosCirurgicos', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar1Id', N'AtendimentosCirurgicos', N'Users', N'ClinicaId,MedicoAuxiliar1Id', N'ClinicaId,Id'),
(N'FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar2Id', N'AtendimentosCirurgicos', N'Users', N'ClinicaId,MedicoAuxiliar2Id', N'ClinicaId,Id'),
(N'FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoResponsavelId', N'AtendimentosCirurgicos', N'Users', N'ClinicaId,MedicoResponsavelId', N'ClinicaId,Id'),
(N'FK_AuditoriasPlataforma_Clinicas_ClinicaId', N'AuditoriasPlataforma', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_AuditoriasPlataforma_UsuariosGlobais_UsuarioGlobalId', N'AuditoriasPlataforma', N'UsuariosGlobais', N'UsuarioGlobalId', N'Id'),
(N'FK_AuthenticationSessions_UsuariosClinicas_UsuarioClinicaId', N'AuthenticationSessions', N'UsuariosClinicas', N'UsuarioClinicaId', N'Id'),
(N'FK_AuthenticationSessions_UsuariosGlobais_UsuarioGlobalId', N'AuthenticationSessions', N'UsuariosGlobais', N'UsuarioGlobalId', N'Id'),
(N'FK_ConfiguracoesSistema_Clinicas_ClinicaId', N'ConfiguracoesSistema', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_ContasReceber_Clinicas_ClinicaId', N'ContasReceber', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_ContasReceber_Convenios_ConvenioId', N'ContasReceber', N'Convenios', N'ConvenioId', N'IdConvenio'),
(N'FK_ContasReceber_Faturamentos_FaturamentoId', N'ContasReceber', N'Faturamentos', N'FaturamentoId', N'Id'),
(N'FK_ContasReceber_Pacientes_ClinicaId_PacienteId', N'ContasReceber', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_ConvenioProcedimentoPrecos_Clinicas_ClinicaId', N'ConvenioProcedimentoPrecos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_ConvenioProcedimentoPrecos_Convenios_ConvenioId', N'ConvenioProcedimentoPrecos', N'Convenios', N'ConvenioId', N'IdConvenio'),
(N'FK_Convenios_Clinicas_ClinicaId', N'Convenios', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_EquipeLoginDesafios_Clinicas_ClinicaId', N'EquipeLoginDesafios', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_EquipeLoginDesafios_Equipes_EquipeId', N'EquipeLoginDesafios', N'Equipes', N'EquipeId', N'Id'),
(N'FK_EquipeMembros_Clinicas_ClinicaId', N'EquipeMembros', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_EquipeMembros_Equipes_EquipeId', N'EquipeMembros', N'Equipes', N'EquipeId', N'Id'),
(N'FK_EquipeMembros_Users_ClinicaId_UserId', N'EquipeMembros', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_EquipeOperadores_Clinicas_ClinicaId', N'EquipeOperadores', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_EquipeOperadores_Equipes_EquipeId', N'EquipeOperadores', N'Equipes', N'EquipeId', N'Id'),
(N'FK_EquipeOperadores_Users_ClinicaId_UserId', N'EquipeOperadores', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_Equipes_Clinicas_ClinicaId', N'Equipes', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Equipes_Users_ClinicaId_UsuarioLoginId', N'Equipes', N'Users', N'ClinicaId,UsuarioLoginId', N'ClinicaId,Id'),
(N'FK_EventEmailDeliveries_Clinicas_ClinicaId', N'EventEmailDeliveries', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_EventEmailDeliveries_Events_EventId', N'EventEmailDeliveries', N'Events', N'EventId', N'Id'),
(N'FK_EventEmailDeliveries_Users_ClinicaId_RecipientUserId', N'EventEmailDeliveries', N'Users', N'ClinicaId,RecipientUserId', N'ClinicaId,Id'),
(N'FK_EventEmailDeliveries_Users_ClinicaId_SenderUserId', N'EventEmailDeliveries', N'Users', N'ClinicaId,SenderUserId', N'ClinicaId,Id'),
(N'FK_Events_Clinicas_ClinicaId', N'Events', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Events_Users_ClinicaId_CreatedByUserId', N'Events', N'Users', N'ClinicaId,CreatedByUserId', N'ClinicaId,Id'),
(N'FK_Events_Users_ClinicaId_MedicalUserId', N'Events', N'Users', N'ClinicaId,MedicalUserId', N'ClinicaId,Id'),
(N'FK_Events_Users_ClinicaId_UserId', N'Events', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_FaturamentoHistoricoArquivos_Clinicas_ClinicaId', N'FaturamentoHistoricoArquivos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_FaturamentoItens_AtendimentoProcedimentos_AtendimentoProcedimentoId', N'FaturamentoItens', N'AtendimentoProcedimentos', N'AtendimentoProcedimentoId', N'Id'),
(N'FK_FaturamentoItens_Clinicas_ClinicaId', N'FaturamentoItens', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_FaturamentoItens_Faturamentos_FaturamentoId', N'FaturamentoItens', N'Faturamentos', N'FaturamentoId', N'Id'),
(N'FK_Faturamentos_AtendimentosCirurgicos_AtendimentoCirurgicoId', N'Faturamentos', N'AtendimentosCirurgicos', N'AtendimentoCirurgicoId', N'Id'),
(N'FK_Faturamentos_Clinicas_ClinicaId', N'Faturamentos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Faturamentos_Convenios_ConvenioId', N'Faturamentos', N'Convenios', N'ConvenioId', N'IdConvenio'),
(N'FK_FaturamentosMedicos_Clinicas_ClinicaId', N'FaturamentosMedicos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_FaturamentosMedicos_Pacientes_ClinicaId_PacienteId', N'FaturamentosMedicos', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_FinanceiroMigracaoInconsistencias_Clinicas_ClinicaId', N'FinanceiroMigracaoInconsistencias', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_FinanceiroMigracaoInconsistencias_Pacientes_ClinicaId_PacienteId', N'FinanceiroMigracaoInconsistencias', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_Glosas_Clinicas_ClinicaId', N'Glosas', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Glosas_FaturamentoItens_FaturamentoItemId', N'Glosas', N'FaturamentoItens', N'FaturamentoItemId', N'Id'),
(N'FK_Glosas_Faturamentos_FaturamentoId', N'Glosas', N'Faturamentos', N'FaturamentoId', N'Id'),
(N'FK_GrupoMedicoUsuarios_Clinicas_ClinicaId', N'GrupoMedicoUsuarios', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_GrupoMedicoUsuarios_GruposMedicos_GrupoMedicoId', N'GrupoMedicoUsuarios', N'GruposMedicos', N'GrupoMedicoId', N'Id'),
(N'FK_GrupoMedicoUsuarios_Users_ClinicaId_UserId', N'GrupoMedicoUsuarios', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_GruposMedicos_Clinicas_ClinicaId', N'GruposMedicos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Hospitais_Clinicas_ClinicaId', N'Hospitais', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_IdempotencyRequests_Clinicas_ClinicaId', N'IdempotencyRequests', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Licencas_Clinicas_ClinicaId', N'Licencas', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Licencas_Users_ClinicaId_UserId', N'Licencas', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_Observacoes_Clinicas_ClinicaId', N'Observacoes', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Observacoes_Observacoes_ObservacaoPaiId', N'Observacoes', N'Observacoes', N'ObservacaoPaiId', N'Id'),
(N'FK_Observacoes_Pacientes_ClinicaId_PacienteId', N'Observacoes', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_Observacoes_Users_ClinicaId_AutorUserId', N'Observacoes', N'Users', N'ClinicaId,AutorUserId', N'ClinicaId,Id'),
(N'FK_Observacoes_Users_ClinicaId_DestinatarioUserId', N'Observacoes', N'Users', N'ClinicaId,DestinatarioUserId', N'ClinicaId,Id'),
(N'FK_OPME_Clinicas_ClinicaId', N'OPME', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_PacienteArquivos_Clinicas_ClinicaId', N'PacienteArquivos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_PacienteArquivos_Pacientes_ClinicaId_PacienteId', N'PacienteArquivos', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_PacienteProcedimentos_Clinicas_ClinicaId', N'PacienteProcedimentos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_PacienteProcedimentos_Pacientes_ClinicaId_PacienteId', N'PacienteProcedimentos', N'Pacientes', N'ClinicaId,PacienteId', N'ClinicaId,Id'),
(N'FK_Pacientes_Clinicas_ClinicaId', N'Pacientes', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Pacientes_Convenios_ConvenioId', N'Pacientes', N'Convenios', N'ConvenioId', N'IdConvenio'),
(N'FK_Pacientes_Hospitais_HospitalId', N'Pacientes', N'Hospitais', N'HospitalId', N'Id'),
(N'FK_Pacientes_OPME_OpmeFornecedorId', N'Pacientes', N'OPME', N'OpmeFornecedorId', N'IdFornecedor'),
(N'FK_Pacientes_Users_ClinicaId_MedicoAuxiliar1UserId', N'Pacientes', N'Users', N'ClinicaId,MedicoAuxiliar1UserId', N'ClinicaId,Id'),
(N'FK_Pacientes_Users_ClinicaId_MedicoAuxiliar2UserId', N'Pacientes', N'Users', N'ClinicaId,MedicoAuxiliar2UserId', N'ClinicaId,Id'),
(N'FK_Pacientes_Users_ClinicaId_MedicoUserId', N'Pacientes', N'Users', N'ClinicaId,MedicoUserId', N'ClinicaId,Id'),
(N'FK_Pacientes_Users_ClinicaId_UserId', N'Pacientes', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_PasswordResetTokens_Clinicas_ClinicaId', N'PasswordResetTokens', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_PasswordResetTokens_Users_ClinicaId_UserId', N'PasswordResetTokens', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_Recebimentos_Clinicas_ClinicaId', N'Recebimentos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Recebimentos_ContasReceber_ContaReceberId', N'Recebimentos', N'ContasReceber', N'ContaReceberId', N'Id'),
(N'FK_Recebimentos_Users_ClinicaId_UsuarioCadastroId', N'Recebimentos', N'Users', N'ClinicaId,UsuarioCadastroId', N'ClinicaId,Id'),
(N'FK_Recebimentos_Users_ClinicaId_UsuarioEstornoId', N'Recebimentos', N'Users', N'ClinicaId,UsuarioEstornoId', N'ClinicaId,Id'),
(N'FK_RecursosGlosa_Clinicas_ClinicaId', N'RecursosGlosa', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_RecursosGlosa_Glosas_GlosaId', N'RecursosGlosa', N'Glosas', N'GlosaId', N'Id'),
(N'FK_TemporaryAccessCredentials_UsuariosGlobais_UsuarioGlobalId', N'TemporaryAccessCredentials', N'UsuariosGlobais', N'UsuarioGlobalId', N'Id'),
(N'FK_UserArquivos_Clinicas_ClinicaId', N'UserArquivos', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_UserArquivos_Users_ClinicaId_UserId', N'UserArquivos', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_UserLegalAcceptances_Clinicas_ClinicaId', N'UserLegalAcceptances', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_UserLegalAcceptances_Users_ClinicaId_UserId', N'UserLegalAcceptances', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_UserPrivacyPreferences_Clinicas_ClinicaId', N'UserPrivacyPreferences', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_UserPrivacyPreferences_Users_ClinicaId_UserId', N'UserPrivacyPreferences', N'Users', N'ClinicaId,UserId', N'ClinicaId,Id'),
(N'FK_Users_Clinicas_ClinicaId', N'Users', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_Users_Perfis_PerfilId', N'Users', N'Perfis', N'PerfilId', N'Id'),
(N'FK_UsuariosClinicas_Clinicas_ClinicaId', N'UsuariosClinicas', N'Clinicas', N'ClinicaId', N'Id'),
(N'FK_UsuariosClinicas_Perfis_PerfilId', N'UsuariosClinicas', N'Perfis', N'PerfilId', N'Id'),
(N'FK_UsuariosClinicas_Users_UserId', N'UsuariosClinicas', N'Users', N'UserId', N'Id'),
(N'FK_UsuariosClinicas_UsuariosGlobais_UsuarioGlobalId', N'UsuariosClinicas', N'UsuariosGlobais', N'UsuarioGlobalId', N'Id')
    ;

    IF EXISTS (
        SELECT 1 FROM @foreignKeys expected
        LEFT JOIN sys.foreign_keys actual
          ON actual.name = expected.Name AND actual.parent_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(expected.ChildTable))
         AND actual.referenced_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(expected.ParentTable))
        OUTER APPLY (
            SELECT STRING_AGG(CONVERT(nvarchar(max), child.name), N',') WITHIN GROUP (ORDER BY columns.constraint_column_id) AS ChildColumns,
                   STRING_AGG(CONVERT(nvarchar(max), parent.name), N',') WITHIN GROUP (ORDER BY columns.constraint_column_id) AS ParentColumns
            FROM sys.foreign_key_columns columns
            JOIN sys.columns child ON child.object_id = columns.parent_object_id AND child.column_id = columns.parent_column_id
            JOIN sys.columns parent ON parent.object_id = columns.referenced_object_id AND parent.column_id = columns.referenced_column_id
            WHERE columns.constraint_object_id = actual.object_id
        ) shape
        WHERE actual.object_id IS NULL OR shape.ChildColumns <> expected.ChildColumns OR shape.ParentColumns <> expected.ParentColumns)
        THROW 51841, 'Restore verification refused: required current relationships are missing or incompatible.', 1;

    DECLARE @checks TABLE (Name sysname PRIMARY KEY, TableName sysname);
    INSERT @checks VALUES
        (N'CK_AtendimentosCirurgicos_AuxiliaresDistintos', N'AtendimentosCirurgicos'),
        (N'CK_AtendimentosCirurgicos_EquipeObrigatoria', N'AtendimentosCirurgicos'),
        (N'CK_AtendimentosCirurgicos_TipoCirurgia', N'AtendimentosCirurgicos'),
        (N'CK_AtendimentoProcedimentos_PesoPercentual', N'AtendimentoProcedimentos'),
        (N'CK_AtendimentoProcedimentos_Quantidade', N'AtendimentoProcedimentos'),
        (N'CK_ContasReceber_Valores', N'ContasReceber'),
        (N'CK_ConvenioProcedimentoPrecos_Valores', N'ConvenioProcedimentoPrecos'),
        (N'CK_ConvenioProcedimentoPrecos_Vigencia', N'ConvenioProcedimentoPrecos'),
        (N'CK_Events_AllDayPeriod', N'Events'),
        (N'CK_Faturamentos_Valores', N'Faturamentos'),
        (N'CK_FaturamentoHistoricoArquivos_Ano', N'FaturamentoHistoricoArquivos'),
        (N'CK_FaturamentoHistoricoArquivos_Mes', N'FaturamentoHistoricoArquivos'),
        (N'CK_FaturamentoItens_Valores', N'FaturamentoItens'),
        (N'CK_Glosas_Valor', N'Glosas'),
        (N'CK_Pacientes_TipoCirurgia', N'Pacientes'),
        (N'CK_Recebimentos_Valor', N'Recebimentos'),
        (N'CK_RecursosGlosa_Valores', N'RecursosGlosa')
    ;
    IF EXISTS (
        SELECT 1 FROM @checks expected LEFT JOIN sys.check_constraints actual
          ON actual.name = expected.Name AND actual.parent_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(expected.TableName))
        WHERE actual.object_id IS NULL)
        THROW 51841, 'Restore verification refused: required current checks are missing.', 1;

    DECLARE @uniqueKeys TABLE (Name sysname, TableName sysname, Columns nvarchar(1000));
    INSERT @uniqueKeys VALUES
        (N'AK_Users_ClinicaId_Id', N'Users', N'ClinicaId,Id'),
        (N'AK_Pacientes_ClinicaId_Id', N'Pacientes', N'ClinicaId,Id'),
        (N'IX_UsuariosGlobais_Email', N'UsuariosGlobais', N'Email'),
        (N'IX_UsuariosClinicas_UserId', N'UsuariosClinicas', N'UserId'),
        (N'IX_UsuariosClinicas_UsuarioGlobalId_ClinicaId', N'UsuariosClinicas', N'UsuarioGlobalId,ClinicaId'),
        (N'IX_AuthenticationSessions_RefreshTokenHash', N'AuthenticationSessions', N'RefreshTokenHash');
    IF EXISTS (
        SELECT 1 FROM @uniqueKeys expected LEFT JOIN sys.indexes actual
          ON actual.name = expected.Name AND actual.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(expected.TableName))
        OUTER APPLY (
            SELECT STRING_AGG(CONVERT(nvarchar(max), columnInfo.name), N',') WITHIN GROUP (ORDER BY indexColumn.key_ordinal) AS Columns
            FROM sys.index_columns indexColumn JOIN sys.columns columnInfo
              ON columnInfo.object_id = indexColumn.object_id AND columnInfo.column_id = indexColumn.column_id
            WHERE indexColumn.object_id = actual.object_id AND indexColumn.index_id = actual.index_id AND indexColumn.key_ordinal > 0
        ) shape
        WHERE actual.index_id IS NULL OR actual.is_unique <> 1 OR actual.has_filter <> 0 OR shape.Columns <> expected.Columns)
        THROW 51841, 'Restore verification refused: required identity or tenant uniqueness is missing or incompatible.', 1;
    IF COL_LENGTH(N'dbo.UsuariosClinicas', N'PerfilId') IS NULL
       OR COL_LENGTH(N'dbo.Users', N'PerfilId') IS NULL
       OR COL_LENGTH(N'dbo.AuthenticationSessions', N'UsuarioGlobalId') IS NULL
        THROW 51841, 'Restore verification refused: required identity columns are missing.', 1;

    DECLARE @results TABLE (CheckName nvarchar(300) PRIMARY KEY, Violations bigint NOT NULL);
    INSERT @results
    SELECT N'DisabledOrUntrustedConstraints', COUNT_BIG(*) FROM (
        SELECT parent_object_id FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1
        UNION ALL SELECT parent_object_id FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1
    ) invalid JOIN sys.tables tables ON tables.object_id = invalid.parent_object_id AND tables.schema_id = SCHEMA_ID(N'dbo');
    INSERT @results
    SELECT N'DisabledIndexes', COUNT_BIG(*) FROM sys.indexes indexes
    JOIN sys.tables tables ON tables.object_id = indexes.object_id AND tables.schema_id = SCHEMA_ID(N'dbo')
    WHERE indexes.is_disabled = 1 AND indexes.is_hypothetical = 0;

    DECLARE @name sysname, @child sysname, @parent sysname, @join nvarchar(max), @notNull nvarchar(max),
            @sql nvarchar(max), @count bigint;
    DECLARE relationship CURSOR LOCAL FAST_FORWARD FOR SELECT Name, ChildTable, ParentTable FROM @foreignKeys ORDER BY Name;
    OPEN relationship;
    FETCH NEXT FROM relationship INTO @name, @child, @parent;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @join = STRING_AGG(CONVERT(nvarchar(max), N'p.' + QUOTENAME(parent.name) + N' = c.' + QUOTENAME(child.name)), N' AND '),
               @notNull = STRING_AGG(CONVERT(nvarchar(max), N'c.' + QUOTENAME(child.name) + N' IS NOT NULL'), N' AND ')
        FROM sys.foreign_keys fk JOIN sys.foreign_key_columns columns ON columns.constraint_object_id = fk.object_id
        JOIN sys.columns child ON child.object_id = columns.parent_object_id AND child.column_id = columns.parent_column_id
        JOIN sys.columns parent ON parent.object_id = columns.referenced_object_id AND parent.column_id = columns.referenced_column_id
        WHERE fk.name = @name AND fk.parent_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@child));
        SET @sql = N'SELECT @violations = COUNT_BIG(*) FROM dbo.' + QUOTENAME(@child) + N' c WHERE ' + @notNull
            + N' AND NOT EXISTS (SELECT 1 FROM dbo.' + QUOTENAME(@parent) + N' p WHERE ' + @join + N');';
        EXEC sys.sp_executesql @sql, N'@violations bigint OUTPUT', @violations = @count OUTPUT;
        INSERT @results VALUES (N'ForeignKey:' + @name, @count);
        -- ClinicaResolutionMiddleware explicitly requires these two owners to match.
        IF @name = N'FK_UsuariosClinicas_Users_UserId'
        BEGIN
            SET @sql = N'SELECT @violations = COUNT_BIG(*) FROM dbo.' + QUOTENAME(@child) + N' c JOIN dbo.'
                + QUOTENAME(@parent) + N' p ON ' + @join + N' WHERE c.ClinicaId <> p.ClinicaId;';
            EXEC sys.sp_executesql @sql, N'@violations bigint OUTPUT', @violations = @count OUTPUT;
            INSERT @results VALUES (N'Tenant:' + @name, @count);
        END;
        FETCH NEXT FROM relationship INTO @name, @child, @parent;
    END;
    CLOSE relationship;
    DEALLOCATE relationship;

    DECLARE checkRule CURSOR LOCAL FAST_FORWARD FOR
        SELECT expected.Name, expected.TableName, actual.definition FROM @checks expected
        JOIN sys.check_constraints actual ON actual.name = expected.Name AND actual.parent_object_id = OBJECT_ID(N'dbo.' + QUOTENAME(expected.TableName));
    OPEN checkRule;
    FETCH NEXT FROM checkRule INTO @name, @child, @sql;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'SELECT @violations = COUNT_BIG(*) FROM dbo.' + QUOTENAME(@child) + N' WHERE NOT (' + @sql + N');';
        EXEC sys.sp_executesql @sql, N'@violations bigint OUTPUT', @violations = @count OUTPUT;
        INSERT @results VALUES (N'Check:' + @name, @count);
        FETCH NEXT FROM checkRule INTO @name, @child, @sql;
    END;
    CLOSE checkRule;
    DEALLOCATE checkRule;

    EXEC sys.sp_executesql N'SELECT @violations = COUNT_BIG(*) FROM dbo.AuthenticationSessions s JOIN dbo.UsuariosClinicas membership ON membership.Id = s.UsuarioClinicaId WHERE s.UsuarioGlobalId <> membership.UsuarioGlobalId;',
        N'@violations bigint OUTPUT', @violations = @count OUTPUT;
    INSERT @results VALUES (N'SessionIdentityMismatch', @count);
    EXEC sys.sp_executesql N'SELECT @violations = COUNT_BIG(*) FROM dbo.UsuariosClinicas membership JOIN dbo.Users u ON u.Id = membership.UserId WHERE u.PerfilId <> membership.PerfilId;',
        N'@violations bigint OUTPUT', @violations = @count OUTPUT;
    INSERT @results VALUES (N'MembershipProfileMismatch', @count);

    SELECT CheckName, Violations FROM @results ORDER BY CheckName;
    IF EXISTS (SELECT 1 FROM @results WHERE Violations <> 0)
        THROW 51842, 'Restore verification failed: aggregate integrity violations were found.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() IN (51841, 51842) THROW;
    -- Never forward engine errors, which may contain data, to operational logs.
    THROW 51843, 'Restore verification inconclusive: a read or metadata check failed.', 1;
END CATCH;
