using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HemodinksAPI.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260811233000_AddPacienteDataAtendimento")]
public partial class AddPacienteDataAtendimento : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DataAtendimento",
            table: "Pacientes",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Pacientes_ClinicaId_DataAtendimento",
            table: "Pacientes",
            columns: new[] { "ClinicaId", "DataAtendimento" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Pacientes_ClinicaId_DataAtendimento",
            table: "Pacientes");

        migrationBuilder.DropColumn(
            name: "DataAtendimento",
            table: "Pacientes");
    }
}
