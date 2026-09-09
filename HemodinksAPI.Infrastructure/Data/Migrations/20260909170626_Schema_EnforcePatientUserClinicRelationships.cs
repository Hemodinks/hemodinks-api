using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Schema_EnforcePatientUserClinicRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgendaNotifications_Users_RecipientUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AgendaNotifications_Users_SenderUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Pacientes_PacienteId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoResponsavelId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_ContasReceber_Pacientes_PacienteId",
                table: "ContasReceber");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipeMembros_Users_UserId",
                table: "EquipeMembros");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipeOperadores_Users_UserId",
                table: "EquipeOperadores");

            migrationBuilder.DropForeignKey(
                name: "FK_Equipes_Users_UsuarioLoginId",
                table: "Equipes");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Users_MedicalUserId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Users_UserId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_FaturamentosMedicos_Pacientes_PacienteId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_PacienteId",
                table: "FinanceiroMigracaoInconsistencias");

            migrationBuilder.DropForeignKey(
                name: "FK_GrupoMedicoUsuarios_Users_UserId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropForeignKey(
                name: "FK_Licencas_Users_UserId",
                table: "Licencas");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Pacientes_PacienteId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Users_AutorUserId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Users_DestinatarioUserId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteArquivos_Pacientes_PacienteId",
                table: "PacienteArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteProcedimentos_Pacientes_PacienteId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar2UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_MedicoUserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_PasswordResetTokens_Users_UserId",
                table: "PasswordResetTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_Recebimentos_Users_UsuarioCadastroId",
                table: "Recebimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Recebimentos_Users_UsuarioEstornoId",
                table: "Recebimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_UserArquivos_Users_UserId",
                table: "UserArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_UserLegalAcceptances_Users_UserId",
                table: "UserLegalAcceptances");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPrivacyPreferences_Users_UserId",
                table: "UserPrivacyPreferences");

            migrationBuilder.DropIndex(
                name: "IX_UserPrivacyPreferences_UserId",
                table: "UserPrivacyPreferences");

            migrationBuilder.DropIndex(
                name: "IX_UserLegalAcceptances_UserId",
                table: "UserLegalAcceptances");

            migrationBuilder.DropIndex(
                name: "IX_UserArquivos_UserId",
                table: "UserArquivos");

            migrationBuilder.DropIndex(
                name: "IX_Recebimentos_UsuarioCadastroId",
                table: "Recebimentos");

            migrationBuilder.DropIndex(
                name: "IX_Recebimentos_UsuarioEstornoId",
                table: "Recebimentos");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_PacienteProcedimentos_PacienteId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropIndex(
                name: "IX_PacienteArquivos_PacienteId",
                table: "PacienteArquivos");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_AutorUserId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_DestinatarioUserId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_PacienteId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Licencas_UserId",
                table: "Licencas");

            migrationBuilder.DropIndex(
                name: "IX_GrupoMedicoUsuarios_UserId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_PacienteId",
                table: "FinanceiroMigracaoInconsistencias");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_PacienteId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropIndex(
                name: "IX_Events_MedicalUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_UserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_EquipeOperadores_UserId",
                table: "EquipeOperadores");

            migrationBuilder.DropIndex(
                name: "IX_EquipeMembros_UserId",
                table: "EquipeMembros");

            migrationBuilder.DropIndex(
                name: "IX_ContasReceber_PacienteId",
                table: "ContasReceber");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_MedicoResponsavelId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_PacienteId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_RecipientUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_SenderUserId",
                table: "AgendaNotifications");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Users_ClinicaId_Id",
                table: "Users",
                columns: new[] { "ClinicaId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Pacientes_ClinicaId_Id",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ClinicaId_UsuarioCadastroId",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "UsuarioCadastroId" });

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ClinicaId_UsuarioEstornoId",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "UsuarioEstornoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_MedicoAuxiliar1UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoAuxiliar1UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_MedicoAuxiliar2UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoAuxiliar2UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_MedicoUserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_ClinicaId_UsuarioLoginId",
                table: "Equipes",
                columns: new[] { "ClinicaId", "UsuarioLoginId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_ClinicaId_UserId",
                table: "EquipeOperadores",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_PacienteId",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "PacienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoAuxiliar1Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoAuxiliar2Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoResponsavelId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgendaNotifications_Users_ClinicaId_RecipientUserId",
                table: "AgendaNotifications",
                columns: new[] { "ClinicaId", "RecipientUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgendaNotifications_Users_ClinicaId_SenderUserId",
                table: "AgendaNotifications",
                columns: new[] { "ClinicaId", "SenderUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Pacientes_ClinicaId_PacienteId",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoAuxiliar1Id" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoAuxiliar2Id" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "MedicoResponsavelId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContasReceber_Pacientes_ClinicaId_PacienteId",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipeMembros_Users_ClinicaId_UserId",
                table: "EquipeMembros",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipeOperadores_Users_ClinicaId_UserId",
                table: "EquipeOperadores",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Equipes_Users_ClinicaId_UsuarioLoginId",
                table: "Equipes",
                columns: new[] { "ClinicaId", "UsuarioLoginId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Users_ClinicaId_MedicalUserId",
                table: "Events",
                columns: new[] { "ClinicaId", "MedicalUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Users_ClinicaId_UserId",
                table: "Events",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FaturamentosMedicos_Pacientes_ClinicaId_PacienteId",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_ClinicaId_PacienteId",
                table: "FinanceiroMigracaoInconsistencias",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GrupoMedicoUsuarios_Users_ClinicaId_UserId",
                table: "GrupoMedicoUsuarios",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Licencas_Users_ClinicaId_UserId",
                table: "Licencas",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Pacientes_ClinicaId_PacienteId",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Users_ClinicaId_AutorUserId",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "AutorUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Users_ClinicaId_DestinatarioUserId",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "DestinatarioUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteArquivos_Pacientes_ClinicaId_PacienteId",
                table: "PacienteArquivos",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteProcedimentos_Pacientes_ClinicaId_PacienteId",
                table: "PacienteProcedimentos",
                columns: new[] { "ClinicaId", "PacienteId" },
                principalTable: "Pacientes",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoAuxiliar1UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoAuxiliar1UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoAuxiliar2UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoAuxiliar2UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoUserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "MedicoUserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_UserId",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PasswordResetTokens_Users_ClinicaId_UserId",
                table: "PasswordResetTokens",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Recebimentos_Users_ClinicaId_UsuarioCadastroId",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "UsuarioCadastroId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recebimentos_Users_ClinicaId_UsuarioEstornoId",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "UsuarioEstornoId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserArquivos_Users_ClinicaId_UserId",
                table: "UserArquivos",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserLegalAcceptances_Users_ClinicaId_UserId",
                table: "UserLegalAcceptances",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPrivacyPreferences_Users_ClinicaId_UserId",
                table: "UserPrivacyPreferences",
                columns: new[] { "ClinicaId", "UserId" },
                principalTable: "Users",
                principalColumns: new[] { "ClinicaId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgendaNotifications_Users_ClinicaId_RecipientUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AgendaNotifications_Users_ClinicaId_SenderUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Pacientes_ClinicaId_PacienteId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_ClinicaId_MedicoResponsavelId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropForeignKey(
                name: "FK_ContasReceber_Pacientes_ClinicaId_PacienteId",
                table: "ContasReceber");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipeMembros_Users_ClinicaId_UserId",
                table: "EquipeMembros");

            migrationBuilder.DropForeignKey(
                name: "FK_EquipeOperadores_Users_ClinicaId_UserId",
                table: "EquipeOperadores");

            migrationBuilder.DropForeignKey(
                name: "FK_Equipes_Users_ClinicaId_UsuarioLoginId",
                table: "Equipes");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Users_ClinicaId_MedicalUserId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Users_ClinicaId_UserId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_FaturamentosMedicos_Pacientes_ClinicaId_PacienteId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropForeignKey(
                name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_ClinicaId_PacienteId",
                table: "FinanceiroMigracaoInconsistencias");

            migrationBuilder.DropForeignKey(
                name: "FK_GrupoMedicoUsuarios_Users_ClinicaId_UserId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropForeignKey(
                name: "FK_Licencas_Users_ClinicaId_UserId",
                table: "Licencas");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Pacientes_ClinicaId_PacienteId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Users_ClinicaId_AutorUserId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Users_ClinicaId_DestinatarioUserId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteArquivos_Pacientes_ClinicaId_PacienteId",
                table: "PacienteArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteProcedimentos_Pacientes_ClinicaId_PacienteId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoAuxiliar2UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_MedicoUserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Users_ClinicaId_UserId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_PasswordResetTokens_Users_ClinicaId_UserId",
                table: "PasswordResetTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_Recebimentos_Users_ClinicaId_UsuarioCadastroId",
                table: "Recebimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Recebimentos_Users_ClinicaId_UsuarioEstornoId",
                table: "Recebimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_UserArquivos_Users_ClinicaId_UserId",
                table: "UserArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_UserLegalAcceptances_Users_ClinicaId_UserId",
                table: "UserLegalAcceptances");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPrivacyPreferences_Users_ClinicaId_UserId",
                table: "UserPrivacyPreferences");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Users_ClinicaId_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Recebimentos_ClinicaId_UsuarioCadastroId",
                table: "Recebimentos");

            migrationBuilder.DropIndex(
                name: "IX_Recebimentos_ClinicaId_UsuarioEstornoId",
                table: "Recebimentos");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Pacientes_ClinicaId_Id",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_MedicoAuxiliar1UserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_MedicoAuxiliar2UserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_MedicoUserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId_UserId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Equipes_ClinicaId_UsuarioLoginId",
                table: "Equipes");

            migrationBuilder.DropIndex(
                name: "IX_EquipeOperadores_ClinicaId_UserId",
                table: "EquipeOperadores");

            migrationBuilder.DropIndex(
                name: "IX_ContasReceber_ClinicaId_PacienteId",
                table: "ContasReceber");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos");

            migrationBuilder.DropIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_MedicoResponsavelId",
                table: "AtendimentosCirurgicos");

            migrationBuilder.CreateIndex(
                name: "IX_UserPrivacyPreferences_UserId",
                table: "UserPrivacyPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalAcceptances_UserId",
                table: "UserLegalAcceptances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserArquivos_UserId",
                table: "UserArquivos",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioCadastroId",
                table: "Recebimentos",
                column: "UsuarioCadastroId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioEstornoId",
                table: "Recebimentos",
                column: "UsuarioEstornoId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_PacienteId",
                table: "PacienteProcedimentos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteArquivos_PacienteId",
                table: "PacienteArquivos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_AutorUserId",
                table: "Observacoes",
                column: "AutorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_DestinatarioUserId",
                table: "Observacoes",
                column: "DestinatarioUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_PacienteId",
                table: "Observacoes",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_UserId",
                table: "Licencas",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_UserId",
                table: "GrupoMedicoUsuarios",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_PacienteId",
                table: "FinanceiroMigracaoInconsistencias",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_PacienteId",
                table: "FaturamentosMedicos",
                column: "PacienteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_MedicalUserId",
                table: "Events",
                column: "MedicalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId",
                table: "Events",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_UserId",
                table: "EquipeOperadores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_UserId",
                table: "EquipeMembros",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_PacienteId",
                table: "ContasReceber",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar1Id");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar2Id");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                column: "MedicoResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_PacienteId",
                table: "AtendimentosCirurgicos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_RecipientUserId",
                table: "AgendaNotifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_SenderUserId",
                table: "AgendaNotifications",
                column: "SenderUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgendaNotifications_Users_RecipientUserId",
                table: "AgendaNotifications",
                column: "RecipientUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgendaNotifications_Users_SenderUserId",
                table: "AgendaNotifications",
                column: "SenderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Pacientes_PacienteId",
                table: "AtendimentosCirurgicos",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar1Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar1Id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar2Id",
                table: "AtendimentosCirurgicos",
                column: "MedicoAuxiliar2Id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AtendimentosCirurgicos_Users_MedicoResponsavelId",
                table: "AtendimentosCirurgicos",
                column: "MedicoResponsavelId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContasReceber_Pacientes_PacienteId",
                table: "ContasReceber",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipeMembros_Users_UserId",
                table: "EquipeMembros",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EquipeOperadores_Users_UserId",
                table: "EquipeOperadores",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Equipes_Users_UsuarioLoginId",
                table: "Equipes",
                column: "UsuarioLoginId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Users_MedicalUserId",
                table: "Events",
                column: "MedicalUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Users_UserId",
                table: "Events",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FaturamentosMedicos_Pacientes_PacienteId",
                table: "FaturamentosMedicos",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_PacienteId",
                table: "FinanceiroMigracaoInconsistencias",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GrupoMedicoUsuarios_Users_UserId",
                table: "GrupoMedicoUsuarios",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Licencas_Users_UserId",
                table: "Licencas",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Pacientes_PacienteId",
                table: "Observacoes",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Users_AutorUserId",
                table: "Observacoes",
                column: "AutorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Users_DestinatarioUserId",
                table: "Observacoes",
                column: "DestinatarioUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteArquivos_Pacientes_PacienteId",
                table: "PacienteArquivos",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteProcedimentos_Pacientes_PacienteId",
                table: "PacienteProcedimentos",
                column: "PacienteId",
                principalTable: "Pacientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar1UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar1UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoAuxiliar2UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar2UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_MedicoUserId",
                table: "Pacientes",
                column: "MedicoUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Users_UserId",
                table: "Pacientes",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PasswordResetTokens_Users_UserId",
                table: "PasswordResetTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Recebimentos_Users_UsuarioCadastroId",
                table: "Recebimentos",
                column: "UsuarioCadastroId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recebimentos_Users_UsuarioEstornoId",
                table: "Recebimentos",
                column: "UsuarioEstornoId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserArquivos_Users_UserId",
                table: "UserArquivos",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserLegalAcceptances_Users_UserId",
                table: "UserLegalAcceptances",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPrivacyPreferences_Users_UserId",
                table: "UserPrivacyPreferences",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
