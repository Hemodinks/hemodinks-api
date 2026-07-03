using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicasAndClinicaIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Cpf",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId_UsedAt_ExpiresAt",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_PacienteProcedimentos_CbhpmCodigo",
                table: "PacienteProcedimentos");

            migrationBuilder.DropIndex(
                name: "IX_OPME_Fornecedor",
                table: "OPME");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_AutorUserId_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_DestinatarioUserId_DataLeitura_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_PacienteId_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Hospitais_Nome",
                table: "Hospitais");

            migrationBuilder.DropIndex(
                name: "IX_GruposMedicos_Nome",
                table: "GruposMedicos");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos");

            migrationBuilder.DropIndex(
                name: "IX_Events_NextReminderAt_IsCompleted",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_Start_End_IsCompleted",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Convenios_DescricaoConvenio",
                table: "Convenios");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_RecipientUserId_ReadAt_CreatedAt",
                table: "AgendaNotifications");

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "UserArquivos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "PasswordResetTokens",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Pacientes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "PacienteProcedimentos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "PacienteArquivos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "OPME",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Observacoes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Licencas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Hospitais",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "GruposMedicos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "GrupoMedicoUsuarios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "FaturamentosMedicos",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "Convenios",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "ConfiguracoesSistema",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClinicaId",
                table: "AgendaNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Clinicas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinicas", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Clinicas",
                columns: new[] { "Id", "Ativa", "DataAtualizacao", "DataCadastro", "Nome", "Slug" },
                values: new object[] { 1, true, null, new DateTime(2026, 7, 3, 0, 0, 0, 0, DateTimeKind.Utc), "HemoDinks", "hemodinks" });

            migrationBuilder.UpdateData(
                table: "ConfiguracoesSistema",
                keyColumn: "Id",
                keyValue: 1,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 1,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 2,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 3,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 4,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 5,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 6,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 7,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 8,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Convenios",
                keyColumn: "IdConvenio",
                keyValue: 9,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Hospitais",
                keyColumn: "Id",
                keyValue: 1,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Hospitais",
                keyColumn: "Id",
                keyValue: 2,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Hospitais",
                keyColumn: "Id",
                keyValue: 3,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "OPME",
                keyColumn: "IdFornecedor",
                keyValue: 1,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "OPME",
                keyColumn: "IdFornecedor",
                keyValue: 2,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "OPME",
                keyColumn: "IdFornecedor",
                keyValue: 3,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "OPME",
                keyColumn: "IdFornecedor",
                keyValue: 4,
                column: "ClinicaId",
                value: 1);

            migrationBuilder.Sql(@"
UPDATE [Users] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [UserArquivos] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [PasswordResetTokens] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Pacientes] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [PacienteProcedimentos] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [PacienteArquivos] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [OPME] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Observacoes] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Licencas] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Hospitais] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [GruposMedicos] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [GrupoMedicoUsuarios] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [FaturamentosMedicos] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Events] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [Convenios] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [ConfiguracoesSistema] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
UPDATE [AgendaNotifications] SET [ClinicaId] = 1 WHERE [ClinicaId] IS NULL;
");

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "UserArquivos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "PasswordResetTokens",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Pacientes",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "PacienteProcedimentos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "PacienteArquivos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "OPME",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Observacoes",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Licencas",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Hospitais",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "GruposMedicos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "GrupoMedicoUsuarios",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "FaturamentosMedicos",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Events",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "Convenios",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "ConfiguracoesSistema",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClinicaId",
                table: "AgendaNotifications",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicaId",
                table: "Users",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicaId_Cpf",
                table: "Users",
                columns: new[] { "ClinicaId", "Cpf" },
                unique: true,
                filter: "[Cpf] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users",
                columns: new[] { "ClinicaId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserArquivos_ClinicaId_UserId",
                table: "UserArquivos",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ClinicaId",
                table: "PasswordResetTokens",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ClinicaId_UserId_UsedAt_ExpiresAt",
                table: "PasswordResetTokens",
                columns: new[] { "ClinicaId", "UserId", "UsedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId",
                table: "Pacientes",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_CbhpmCodigo",
                table: "PacienteProcedimentos",
                columns: new[] { "ClinicaId", "CbhpmCodigo" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_PacienteId",
                table: "PacienteProcedimentos",
                columns: new[] { "ClinicaId", "PacienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteArquivos_ClinicaId_PacienteId",
                table: "PacienteArquivos",
                columns: new[] { "ClinicaId", "PacienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_OPME_ClinicaId_Fornecedor",
                table: "OPME",
                columns: new[] { "ClinicaId", "Fornecedor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_AutorUserId",
                table: "Observacoes",
                column: "AutorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_ClinicaId_AutorUserId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "AutorUserId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_ClinicaId_DestinatarioUserId_DataLeitura_DataCadastro",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "DestinatarioUserId", "DataLeitura", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_ClinicaId_PacienteId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "ClinicaId", "PacienteId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_DestinatarioUserId",
                table: "Observacoes",
                column: "DestinatarioUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_PacienteId",
                table: "Observacoes",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_ClinicaId",
                table: "Licencas",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_Licencas_ClinicaId_UserId",
                table: "Licencas",
                columns: new[] { "ClinicaId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hospitais_ClinicaId_Nome",
                table: "Hospitais",
                columns: new[] { "ClinicaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GruposMedicos_ClinicaId",
                table: "GruposMedicos",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_GruposMedicos_ClinicaId_Nome",
                table: "GruposMedicos",
                columns: new[] { "ClinicaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_GrupoMedicoId",
                table: "GrupoMedicoUsuarios",
                columns: new[] { "ClinicaId", "GrupoMedicoId" });

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_UserId",
                table: "GrupoMedicoUsuarios",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "ConferenciaPagamentoRealizada" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_PacienteId",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "PacienteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClinicaId_MedicalUserId",
                table: "Events",
                columns: new[] { "ClinicaId", "MedicalUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClinicaId_NextReminderAt_IsCompleted",
                table: "Events",
                columns: new[] { "ClinicaId", "NextReminderAt", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClinicaId_Start_End_IsCompleted",
                table: "Events",
                columns: new[] { "ClinicaId", "Start", "End", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ClinicaId_UserId",
                table: "Events",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Convenios_ClinicaId_DescricaoConvenio",
                table: "Convenios",
                columns: new[] { "ClinicaId", "DescricaoConvenio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesSistema_ClinicaId",
                table: "ConfiguracoesSistema",
                column: "ClinicaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_ClinicaId_EventId",
                table: "AgendaNotifications",
                columns: new[] { "ClinicaId", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_ClinicaId_RecipientUserId_ReadAt_CreatedAt",
                table: "AgendaNotifications",
                columns: new[] { "ClinicaId", "RecipientUserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_ClinicaId_SenderUserId",
                table: "AgendaNotifications",
                columns: new[] { "ClinicaId", "SenderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_RecipientUserId",
                table: "AgendaNotifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clinicas_Slug",
                table: "Clinicas",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgendaNotifications_Clinicas_ClinicaId",
                table: "AgendaNotifications",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ConfiguracoesSistema_Clinicas_ClinicaId",
                table: "ConfiguracoesSistema",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Convenios_Clinicas_ClinicaId",
                table: "Convenios",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Clinicas_ClinicaId",
                table: "Events",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FaturamentosMedicos_Clinicas_ClinicaId",
                table: "FaturamentosMedicos",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GrupoMedicoUsuarios_Clinicas_ClinicaId",
                table: "GrupoMedicoUsuarios",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GruposMedicos_Clinicas_ClinicaId",
                table: "GruposMedicos",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Hospitais_Clinicas_ClinicaId",
                table: "Hospitais",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Licencas_Clinicas_ClinicaId",
                table: "Licencas",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Observacoes_Clinicas_ClinicaId",
                table: "Observacoes",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OPME_Clinicas_ClinicaId",
                table: "OPME",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteArquivos_Clinicas_ClinicaId",
                table: "PacienteArquivos",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PacienteProcedimentos_Clinicas_ClinicaId",
                table: "PacienteProcedimentos",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Pacientes_Clinicas_ClinicaId",
                table: "Pacientes",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PasswordResetTokens_Clinicas_ClinicaId",
                table: "PasswordResetTokens",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserArquivos_Clinicas_ClinicaId",
                table: "UserArquivos",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Clinicas_ClinicaId",
                table: "Users",
                column: "ClinicaId",
                principalTable: "Clinicas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgendaNotifications_Clinicas_ClinicaId",
                table: "AgendaNotifications");

            migrationBuilder.DropForeignKey(
                name: "FK_ConfiguracoesSistema_Clinicas_ClinicaId",
                table: "ConfiguracoesSistema");

            migrationBuilder.DropForeignKey(
                name: "FK_Convenios_Clinicas_ClinicaId",
                table: "Convenios");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_Clinicas_ClinicaId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_FaturamentosMedicos_Clinicas_ClinicaId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropForeignKey(
                name: "FK_GrupoMedicoUsuarios_Clinicas_ClinicaId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropForeignKey(
                name: "FK_GruposMedicos_Clinicas_ClinicaId",
                table: "GruposMedicos");

            migrationBuilder.DropForeignKey(
                name: "FK_Hospitais_Clinicas_ClinicaId",
                table: "Hospitais");

            migrationBuilder.DropForeignKey(
                name: "FK_Licencas_Clinicas_ClinicaId",
                table: "Licencas");

            migrationBuilder.DropForeignKey(
                name: "FK_Observacoes_Clinicas_ClinicaId",
                table: "Observacoes");

            migrationBuilder.DropForeignKey(
                name: "FK_OPME_Clinicas_ClinicaId",
                table: "OPME");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteArquivos_Clinicas_ClinicaId",
                table: "PacienteArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_PacienteProcedimentos_Clinicas_ClinicaId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropForeignKey(
                name: "FK_Pacientes_Clinicas_ClinicaId",
                table: "Pacientes");

            migrationBuilder.DropForeignKey(
                name: "FK_PasswordResetTokens_Clinicas_ClinicaId",
                table: "PasswordResetTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_UserArquivos_Clinicas_ClinicaId",
                table: "UserArquivos");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Clinicas_ClinicaId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Clinicas");

            migrationBuilder.DropIndex(
                name: "IX_Users_ClinicaId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ClinicaId_Cpf",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ClinicaId_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserArquivos_ClinicaId_UserId",
                table: "UserArquivos");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_ClinicaId",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_ClinicaId_UserId_UsedAt_ExpiresAt",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_Pacientes_ClinicaId",
                table: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_CbhpmCodigo",
                table: "PacienteProcedimentos");

            migrationBuilder.DropIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_PacienteId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropIndex(
                name: "IX_PacienteArquivos_ClinicaId_PacienteId",
                table: "PacienteArquivos");

            migrationBuilder.DropIndex(
                name: "IX_OPME_ClinicaId_Fornecedor",
                table: "OPME");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_AutorUserId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_ClinicaId_AutorUserId_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_ClinicaId_DestinatarioUserId_DataLeitura_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_ClinicaId_PacienteId_DataCadastro",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_DestinatarioUserId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Observacoes_PacienteId",
                table: "Observacoes");

            migrationBuilder.DropIndex(
                name: "IX_Licencas_ClinicaId",
                table: "Licencas");

            migrationBuilder.DropIndex(
                name: "IX_Licencas_ClinicaId_UserId",
                table: "Licencas");

            migrationBuilder.DropIndex(
                name: "IX_Hospitais_ClinicaId_Nome",
                table: "Hospitais");

            migrationBuilder.DropIndex(
                name: "IX_GruposMedicos_ClinicaId",
                table: "GruposMedicos");

            migrationBuilder.DropIndex(
                name: "IX_GruposMedicos_ClinicaId_Nome",
                table: "GruposMedicos");

            migrationBuilder.DropIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_GrupoMedicoId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_UserId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos");

            migrationBuilder.DropIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_PacienteId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropIndex(
                name: "IX_Events_ClinicaId_MedicalUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ClinicaId_NextReminderAt_IsCompleted",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ClinicaId_Start_End_IsCompleted",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ClinicaId_UserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Convenios_ClinicaId_DescricaoConvenio",
                table: "Convenios");

            migrationBuilder.DropIndex(
                name: "IX_ConfiguracoesSistema_ClinicaId",
                table: "ConfiguracoesSistema");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_ClinicaId_EventId",
                table: "AgendaNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_ClinicaId_RecipientUserId_ReadAt_CreatedAt",
                table: "AgendaNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_ClinicaId_SenderUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AgendaNotifications_RecipientUserId",
                table: "AgendaNotifications");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "UserArquivos");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Pacientes");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "PacienteProcedimentos");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "PacienteArquivos");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "OPME");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Observacoes");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Licencas");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Hospitais");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "GruposMedicos");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "GrupoMedicoUsuarios");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "FaturamentosMedicos");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "Convenios");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "ConfiguracoesSistema");

            migrationBuilder.DropColumn(
                name: "ClinicaId",
                table: "AgendaNotifications");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Cpf",
                table: "Users",
                column: "Cpf",
                unique: true,
                filter: "[Cpf] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_UsedAt_ExpiresAt",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "UsedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_CbhpmCodigo",
                table: "PacienteProcedimentos",
                column: "CbhpmCodigo");

            migrationBuilder.CreateIndex(
                name: "IX_OPME_Fornecedor",
                table: "OPME",
                column: "Fornecedor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_AutorUserId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "AutorUserId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_DestinatarioUserId_DataLeitura_DataCadastro",
                table: "Observacoes",
                columns: new[] { "DestinatarioUserId", "DataLeitura", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_PacienteId_DataCadastro",
                table: "Observacoes",
                columns: new[] { "PacienteId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitais_Nome",
                table: "Hospitais",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GruposMedicos_Nome",
                table: "GruposMedicos",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos",
                column: "ConferenciaPagamentoRealizada");

            migrationBuilder.CreateIndex(
                name: "IX_Events_NextReminderAt_IsCompleted",
                table: "Events",
                columns: new[] { "NextReminderAt", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Start_End_IsCompleted",
                table: "Events",
                columns: new[] { "Start", "End", "IsCompleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Convenios_DescricaoConvenio",
                table: "Convenios",
                column: "DescricaoConvenio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_RecipientUserId_ReadAt_CreatedAt",
                table: "AgendaNotifications",
                columns: new[] { "RecipientUserId", "ReadAt", "CreatedAt" });
        }
    }
}
