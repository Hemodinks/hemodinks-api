-- Auditoria somente leitura antes da migracao de isolamento de pacientes e usuarios.
-- Nenhum resultado: nao foram encontrados vinculos entre clinicas distintas.
-- Resultados mostram apenas identificadores; nao alteram nem transferem registros.
SELECT 'AgendaNotifications' AS Tabela, 'RecipientUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AgendaNotifications] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[RecipientUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'AgendaNotifications' AS Tabela, 'SenderUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AgendaNotifications] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[SenderUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'AtendimentosCirurgicos' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AtendimentosCirurgicos] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'AtendimentosCirurgicos' AS Tabela, 'MedicoAuxiliar1Id' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AtendimentosCirurgicos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoAuxiliar1Id]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'AtendimentosCirurgicos' AS Tabela, 'MedicoAuxiliar2Id' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AtendimentosCirurgicos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoAuxiliar2Id]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'AtendimentosCirurgicos' AS Tabela, 'MedicoResponsavelId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [AtendimentosCirurgicos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoResponsavelId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'ContasReceber' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [ContasReceber] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'EquipeMembros' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [EquipeMembros] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'EquipeOperadores' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [EquipeOperadores] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Equipes' AS Tabela, 'UsuarioLoginId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Equipes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UsuarioLoginId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Events' AS Tabela, 'MedicalUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Events] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicalUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Events' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Events] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'FaturamentosMedicos' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [FaturamentosMedicos] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'FinanceiroMigracaoInconsistencias' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [FinanceiroMigracaoInconsistencias] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'GrupoMedicoUsuarios' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [GrupoMedicoUsuarios] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Licencas' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Licencas] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Observacoes' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Observacoes] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Observacoes' AS Tabela, 'AutorUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Observacoes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[AutorUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Observacoes' AS Tabela, 'DestinatarioUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Observacoes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[DestinatarioUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'PacienteArquivos' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [PacienteArquivos] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'PacienteProcedimentos' AS Tabela, 'PacienteId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [PacienteProcedimentos] AS origem
JOIN [Pacientes] AS destino ON destino.Id = origem.[PacienteId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Pacientes' AS Tabela, 'MedicoAuxiliar1UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Pacientes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoAuxiliar1UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Pacientes' AS Tabela, 'MedicoAuxiliar2UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Pacientes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoAuxiliar2UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Pacientes' AS Tabela, 'MedicoUserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Pacientes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[MedicoUserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Pacientes' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Pacientes] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'PasswordResetTokens' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [PasswordResetTokens] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Recebimentos' AS Tabela, 'UsuarioCadastroId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Recebimentos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UsuarioCadastroId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'Recebimentos' AS Tabela, 'UsuarioEstornoId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [Recebimentos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UsuarioEstornoId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'UserArquivos' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [UserArquivos] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'UserLegalAcceptances' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [UserLegalAcceptances] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId
UNION ALL
SELECT 'UserPrivacyPreferences' AS Tabela, 'UserId' AS Coluna,
       origem.Id AS RegistroId, origem.ClinicaId AS ClinicaOrigem,
       destino.Id AS ReferenciaId, destino.ClinicaId AS ClinicaReferencia
FROM [UserPrivacyPreferences] AS origem
JOIN [Users] AS destino ON destino.Id = origem.[UserId]
WHERE origem.ClinicaId <> destino.ClinicaId;

