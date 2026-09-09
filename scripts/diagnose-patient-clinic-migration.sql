-- Somente leitura. Executar no banco de destino do publish-container.
-- Retorna apenas identificadores, sem nomes nem outros dados pessoais.
-- Nao determina automaticamente qual clinica/paciente esta correto.
SELECT DB_NAME() AS Banco;

SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
WHERE MigrationId = N'20260909170626_Schema_EnforcePatientUserClinicRelationships';

SELECT
    atendimento.Id AS AtendimentoId,
    atendimento.ClinicaId AS ClinicaAtendimento,
    atendimento.PacienteId,
    paciente.ClinicaId AS ClinicaPaciente,
    CASE WHEN paciente.Id IS NULL THEN N'Paciente inexistente'
         ELSE N'Clinicas diferentes' END AS Inconsistencia
FROM dbo.AtendimentosCirurgicos AS atendimento
LEFT JOIN dbo.Pacientes AS paciente ON paciente.Id = atendimento.PacienteId
WHERE paciente.Id IS NULL OR paciente.ClinicaId <> atendimento.ClinicaId
ORDER BY atendimento.ClinicaId, atendimento.Id;

-- Para conferir os demais relacionamentos exigidos pela mesma migracao,
-- executar tambem scripts/audit-patient-user-clinic-relationships.sql.
