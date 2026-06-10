using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientsAndCpf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "Users",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pacientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NomePaciente = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Hospital = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Medico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Convenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Procedimento = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Autorizacao = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Pagamento = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RepasseGlosa = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StatusPago = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pacientes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PacienteArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PacienteArquivos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PacienteArquivos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Users])
                BEGIN
                    ;WITH DoctorsToDelete AS
                    (
                        SELECT TOP (40) [Id]
                        FROM [Users]
                        WHERE [PerfilId] = 2
                        ORDER BY [Id]
                    )
                    DELETE FROM [Users]
                    WHERE [Id] IN (SELECT [Id] FROM DoctorsToDelete);

                    DECLARE @Patients TABLE
                    (
                        [Nome] nvarchar(255) NOT NULL,
                        [Email] nvarchar(255) NOT NULL,
                        [Telefone] nvarchar(20) NOT NULL,
                        [Cpf] nvarchar(11) NOT NULL,
                        [DataNascimento] datetime2 NOT NULL
                    );

                    INSERT INTO @Patients ([Nome], [Email], [Telefone], [Cpf], [DataNascimento])
                    VALUES
                        (N'Helena Oliveira', N'paciente1@hemodinks.com', N'+5581998800001', N'00000010073', '1980-01-01'),
                        (N'Miguel Santos', N'paciente2@hemodinks.com', N'+5581998800002', N'00000010154', '1981-02-02'),
                        (N'Laura Costa', N'paciente3@hemodinks.com', N'+5581998800003', N'00000010235', '1982-03-03'),
                        (N'Arthur Lima', N'paciente4@hemodinks.com', N'+5581998800004', N'00000010316', '1983-04-04'),
                        (N'Sophia Pereira', N'paciente5@hemodinks.com', N'+5581998800005', N'00000010405', '1984-05-05'),
                        (N'Davi Martins', N'paciente6@hemodinks.com', N'+5581998800006', N'00000010588', '1985-06-06'),
                        (N'Alice Ferreira', N'paciente7@hemodinks.com', N'+5581998800007', N'00000010669', '1986-07-07'),
                        (N'Gabriel Souza', N'paciente8@hemodinks.com', N'+5581998800008', N'00000010740', '1987-08-08'),
                        (N'Manuela Rocha', N'paciente9@hemodinks.com', N'+5581998800009', N'00000010820', '1988-09-09'),
                        (N'Bernardo Alves', N'paciente10@hemodinks.com', N'+5581998800010', N'00000010901', '1989-10-10');

                    INSERT INTO [Users]
                        ([Nome], [Email], [Telefone], [Cpf], [Senha], [DataCadastro], [DataNascimento], [Ativo], [PrecisaTrocarSenha], [PerfilId])
                    SELECT
                        p.[Nome],
                        p.[Email],
                        p.[Telefone],
                        p.[Cpf],
                        N'Kl1yEm2WrGuiBEQwYoHP63cZ1+KdFTYRPLhXzTNw+SHctPbC',
                        SYSUTCDATETIME(),
                        p.[DataNascimento],
                        CAST(1 AS bit),
                        CAST(1 AS bit),
                        3
                    FROM @Patients p
                    WHERE NOT EXISTS (SELECT 1 FROM [Users] u WHERE u.[Email] = p.[Email]);

                    INSERT INTO [Pacientes] ([UserId], [NomePaciente], [StatusPago])
                    SELECT u.[Id], u.[Nome], CAST(0 AS bit)
                    FROM [Users] u
                    WHERE u.[PerfilId] = 3
                        AND NOT EXISTS (SELECT 1 FROM [Pacientes] p WHERE p.[UserId] = u.[Id]);
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Cpf",
                table: "Users",
                column: "Cpf",
                unique: true,
                filter: "[Cpf] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteArquivos_PacienteId",
                table: "PacienteArquivos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_UserId",
                table: "Pacientes",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PacienteArquivos");

            migrationBuilder.DropTable(
                name: "Pacientes");

            migrationBuilder.DropIndex(
                name: "IX_Users_Cpf",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "Users");
        }
    }
}
