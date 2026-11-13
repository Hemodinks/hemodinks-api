using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HemodinksAPI.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Baseline das 65 migrations de 20260601133147_InitialCreate a 20260908224929_AddTemporaryAccessCredentials (#231).
    /// Mantém o MigrationId da última migration consolidada: bancos existentes já a têm em __EFMigrationsHistory
    /// e não aplicam nada; só bancos novos executam este Up. Procedimento em docs/migrations-baseline.md.
    /// </summary>
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CBHPMGeral",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Procedimento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Porte = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CustoOperacional = table.Column<decimal>(type: "decimal(18,3)", nullable: true),
                    ValorReferencia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Capitulo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Grupo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CBHPMGeral", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clinicas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Cnpj = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: true),
                    FotoClinica = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ativa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Plano = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Trial"),
                    ModulosLiberados = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AssinaturaStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Trial"),
                    TrialAte = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssinaturaValidaAte = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LimiteUsuarios = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinicas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Perfis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perfis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsuariosGlobais",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecurityVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemporaryPasswordRecovery = table.Column<bool>(type: "bit", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Senha = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TentativasLoginFalhas = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UltimaFalhaLoginEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BloqueadoAte = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosGlobais", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracoesSistema",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    NomeEmpresa = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FotoEmpresa = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesSistema", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesSistema_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Convenios",
                columns: table => new
                {
                    IdConvenio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    DescricaoConvenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Convenios", x => x.IdConvenio);
                    table.ForeignKey(
                        name: "FK_Convenios_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaturamentoHistoricoArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Ano = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentoHistoricoArquivos", x => x.Id);
                    table.CheckConstraint("CK_FaturamentoHistoricoArquivos_Ano", "[Ano] BETWEEN 1900 AND 2100");
                    table.CheckConstraint("CK_FaturamentoHistoricoArquivos_Mes", "[Mes] BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_FaturamentoHistoricoArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GruposMedicos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposMedicos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GruposMedicos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Hospitais",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hospitais", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hospitais_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResourceLocation = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotencyRequests_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OPME",
                columns: table => new
                {
                    IdFornecedor = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Fornecedor = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OPME", x => x.IdFornecedor);
                    table.ForeignKey(
                        name: "FK_OPME_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Telefone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Cpf = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    Crm = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CrmUf = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    FotoPerfil = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Senha = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataNascimento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PrecisaTrocarSenha = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PerfilId = table.Column<int>(type: "int", nullable: false, defaultValue: 2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Perfis_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditoriasPlataforma",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Acao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Recurso = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntidadeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetalhesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Sucesso = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriasPlataforma", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditoriasPlataforma_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditoriasPlataforma_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemporaryAccessCredentials",
                columns: table => new
                {
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryAccessCredentials", x => x.UsuarioGlobalId);
                    table.ForeignKey(
                        name: "FK_TemporaryAccessCredentials_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConvenioProcedimentoPrecos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValorNegociado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PercentualPrincipal = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    PercentualAuxiliar1 = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    PercentualAuxiliar2 = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    VigenciaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VigenciaFinal = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Ativo = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvenioProcedimentoPrecos", x => x.Id);
                    table.CheckConstraint("CK_ConvenioProcedimentoPrecos_Valores", "[ValorNegociado] >= 0 AND [PercentualPrincipal] >= 0 AND [PercentualAuxiliar1] >= 0 AND [PercentualAuxiliar2] >= 0");
                    table.CheckConstraint("CK_ConvenioProcedimentoPrecos_Vigencia", "[VigenciaFinal] IS NULL OR [VigenciaFinal] >= [VigenciaInicio]");
                    table.ForeignKey(
                        name: "FK_ConvenioProcedimentoPrecos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConvenioProcedimentoPrecos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Equipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    UsuarioLoginId = table.Column<int>(type: "int", nullable: false),
                    ModoIdentificacao = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ativa = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    VersaoSessao = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipes_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Equipes_Users_UsuarioLoginId",
                        column: x => x.UsuarioLoginId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MedicalUserId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Start = table.Column<DateTime>(type: "datetime2", nullable: false),
                    End = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NotifyMedicalProfile = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    NotifyUser = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ReminderPeriodMinutes = table.Column<int>(type: "int", nullable: true),
                    LastReminderSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextReminderAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Users_MedicalUserId",
                        column: x => x.MedicalUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GrupoMedicoUsuarios",
                columns: table => new
                {
                    GrupoMedicoId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrupoMedicoUsuarios", x => new { x.GrupoMedicoId, x.UserId });
                    table.ForeignKey(
                        name: "FK_GrupoMedicoUsuarios_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GrupoMedicoUsuarios_GruposMedicos_GrupoMedicoId",
                        column: x => x.GrupoMedicoId,
                        principalTable: "GruposMedicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GrupoMedicoUsuarios_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Licencas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Plano = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataInicioTrial = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataFimTrial = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataFimLicenca = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FeaturesLiberadas = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licencas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licencas_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Licencas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pacientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataAtendimento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NomePaciente = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Diagnostico = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TratamentoMedico = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HospitalId = table.Column<int>(type: "int", nullable: true),
                    Hospital = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoUserId = table.Column<int>(type: "int", nullable: true),
                    Medico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar1UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar1 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar2UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    Convenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OpmeFornecedorId = table.Column<int>(type: "int", nullable: true),
                    OpmeFornecedor = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CbhpmPorte = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Procedimento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Autorizacao = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Pagamento = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RepasseGlosa = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StatusPago = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pacientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pacientes_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Hospitais_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_OPME_OpmeFornecedorId",
                        column: x => x.OpmeFornecedorId,
                        principalTable: "OPME",
                        principalColumn: "IdFornecedor",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Users_MedicoAuxiliar1UserId",
                        column: x => x.MedicoAuxiliar1UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Users_MedicoAuxiliar2UserId",
                        column: x => x.MedicoAuxiliar2UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Users_MedicoUserId",
                        column: x => x.MedicoUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pacientes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserArquivos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserArquivos_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLegalAcceptances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLegalAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLegalAcceptances_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserLegalAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPrivacyPreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    DocumentVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreferencesEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AnalyticsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPrivacyPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPrivacyPreferences_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPrivacyPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UsuariosClinicas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PerfilId = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ClinicaPadrao = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosClinicas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Perfis_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuariosClinicas_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipeLoginDesafios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecurityVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UtilizadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeLoginDesafios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeLoginDesafios_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeLoginDesafios_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EquipeMembros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeMembros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipeMembros_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipeOperadores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EquipeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrecisaTrocarPin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    TentativasFalhas = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    BloqueadoAte = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VersaoSessao = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataUltimaTroca = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipeOperadores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "Equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipeOperadores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgendaNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgendaNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgendaNotifications_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgendaNotifications_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgendaNotifications_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgendaNotifications_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AtendimentosCirurgicos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    DataProcedimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HospitalId = table.Column<int>(type: "int", nullable: true),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    OpmeFornecedorId = table.Column<int>(type: "int", nullable: true),
                    MedicoResponsavelId = table.Column<int>(type: "int", nullable: false),
                    MedicoAuxiliar1Id = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar2Id = table.Column<int>(type: "int", nullable: true),
                    Diagnostico = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TratamentoMedico = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NumeroAutorizacao = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ValorGlosa = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MotivoGlosa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacao = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentosCirurgicos", x => x.Id);
                    table.CheckConstraint("CK_AtendimentosCirurgicos_MedicosDistintos", "([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar1Id] <> [MedicoResponsavelId]) AND ([MedicoAuxiliar2Id] IS NULL OR ([MedicoAuxiliar2Id] <> [MedicoResponsavelId] AND ([MedicoAuxiliar1Id] IS NULL OR [MedicoAuxiliar2Id] <> [MedicoAuxiliar1Id])))");
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Hospitais_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_OPME_OpmeFornecedorId",
                        column: x => x.OpmeFornecedorId,
                        principalTable: "OPME",
                        principalColumn: "IdFornecedor",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar1Id",
                        column: x => x.MedicoAuxiliar1Id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoAuxiliar2Id",
                        column: x => x.MedicoAuxiliar2Id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AtendimentosCirurgicos_Users_MedicoResponsavelId",
                        column: x => x.MedicoResponsavelId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaturamentosMedicos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    HonorariosCirurgiao = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HonorariosAuxiliares = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HonorariosAnestesista = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AnestesistaFaturadoSeparado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Anestesista = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CodigoTussCbhpmAmb = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PorteCirurgicoAnestesico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GuiaAutorizacaoConvenio = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    GuiaInternacaoOuSadt = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OpmeMateriaisEspeciais = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TissXmlStatus = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ValorGlosa = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GlosaStatus = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RecursoGlosa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ConferenciaPagamentoRealizada = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DataPagamento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RepasseMedico = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RepasseMedicoObservacao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TipoFaturamentoParticular = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReciboNotaContrato = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompetenciaInicio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompetenciaFinal = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentosMedicos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaturamentosMedicos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaturamentosMedicos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinanceiroMigracaoInconsistencias",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    Campo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ValorOriginal = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Motivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Resolvida = table.Column<bool>(type: "bit", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataResolucao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceiroMigracaoInconsistencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceiroMigracaoInconsistencias_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinanceiroMigracaoInconsistencias_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Observacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    AutorUserId = table.Column<int>(type: "int", nullable: false),
                    DestinatarioUserId = table.Column<int>(type: "int", nullable: false),
                    ObservacaoPaiId = table.Column<int>(type: "int", nullable: true),
                    Texto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataLeitura = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MedicoUserId = table.Column<int>(type: "int", nullable: true),
                    Medico = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar1UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar1 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MedicoAuxiliar2UserId = table.Column<int>(type: "int", nullable: true),
                    MedicoAuxiliar2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Observacoes_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observacoes_Observacoes_ObservacaoPaiId",
                        column: x => x.ObservacaoPaiId,
                        principalTable: "Observacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observacoes_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Observacoes_Users_AutorUserId",
                        column: x => x.AutorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Observacoes_Users_DestinatarioUserId",
                        column: x => x.DestinatarioUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PacienteArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
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
                        name: "FK_PacienteArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PacienteArquivos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PacienteProcedimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CbhpmPorte = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Procedimento = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValorReferencia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Ordem = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PacienteProcedimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PacienteProcedimentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PacienteProcedimentos_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecurityVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsuarioGlobalId = table.Column<int>(type: "int", nullable: false),
                    UsuarioClinicaId = table.Column<int>(type: "int", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthenticationSessions_UsuariosClinicas_UsuarioClinicaId",
                        column: x => x.UsuarioClinicaId,
                        principalTable: "UsuariosClinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthenticationSessions_UsuariosGlobais_UsuarioGlobalId",
                        column: x => x.UsuarioGlobalId,
                        principalTable: "UsuariosGlobais",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AtendimentoArquivos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    NomeOriginal = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataUpload = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentoArquivos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtendimentoArquivos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AtendimentoArquivos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AtendimentoProcedimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    CbhpmCodigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CbhpmPorte = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PesoPercentual = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ValorReferencia = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ValorNegociado = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtendimentoProcedimentos", x => x.Id);
                    table.CheckConstraint("CK_AtendimentoProcedimentos_PesoPercentual", "[PesoPercentual] >= 0");
                    table.CheckConstraint("CK_AtendimentoProcedimentos_Quantidade", "[Quantidade] > 0");
                    table.ForeignKey(
                        name: "FK_AtendimentoProcedimentos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AtendimentoProcedimentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Faturamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoCirurgicoId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    NumeroGuia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NumeroLote = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Competencia = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataEnvio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataRetorno = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValorApresentado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosaRecuperada = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorReconhecido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faturamentos", x => x.Id);
                    table.CheckConstraint("CK_Faturamentos_Valores", "[ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorGlosaRecuperada] >= 0 AND [ValorReconhecido] >= 0");
                    table.ForeignKey(
                        name: "FK_Faturamentos_AtendimentosCirurgicos_AtendimentoCirurgicoId",
                        column: x => x.AtendimentoCirurgicoId,
                        principalTable: "AtendimentosCirurgicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContasReceber",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    ConvenioId = table.Column<int>(type: "int", nullable: true),
                    PacienteId = table.Column<int>(type: "int", nullable: false),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Competencia = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataEmissao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorOriginal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorAjustado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorRecebido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SaldoAberto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasReceber", x => x.Id);
                    table.CheckConstraint("CK_ContasReceber_Valores", "[ValorOriginal] >= 0 AND [ValorAjustado] >= 0 AND [ValorRecebido] >= 0 AND [SaldoAberto] >= 0");
                    table.ForeignKey(
                        name: "FK_ContasReceber_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Convenios_ConvenioId",
                        column: x => x.ConvenioId,
                        principalTable: "Convenios",
                        principalColumn: "IdConvenio",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContasReceber_Pacientes_PacienteId",
                        column: x => x.PacienteId,
                        principalTable: "Pacientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FaturamentoItens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    AtendimentoProcedimentoId = table.Column<int>(type: "int", nullable: true),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PesoPercentual = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorApresentado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorAprovado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MotivoGlosa = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturamentoItens", x => x.Id);
                    table.CheckConstraint("CK_FaturamentoItens_Valores", "[Quantidade] > 0 AND [PesoPercentual] >= 0 AND [ValorUnitario] >= 0 AND [ValorApresentado] >= 0 AND [ValorGlosado] >= 0 AND [ValorAprovado] >= 0");
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_AtendimentoProcedimentos_AtendimentoProcedimentoId",
                        column: x => x.AtendimentoProcedimentoId,
                        principalTable: "AtendimentoProcedimentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FaturamentoItens_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recebimentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    ContaReceberId = table.Column<int>(type: "int", nullable: false),
                    DataRecebimento = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValorRecebido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FormaRecebimento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReferenciaBancaria = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DocumentoComprovante = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UsuarioCadastroId = table.Column<int>(type: "int", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Estornado = table.Column<bool>(type: "bit", nullable: false),
                    DataEstorno = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioEstornoId = table.Column<int>(type: "int", nullable: true),
                    MotivoEstorno = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recebimentos", x => x.Id);
                    table.CheckConstraint("CK_Recebimentos_Valor", "[ValorRecebido] > 0");
                    table.ForeignKey(
                        name: "FK_Recebimentos_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_ContasReceber_ContaReceberId",
                        column: x => x.ContaReceberId,
                        principalTable: "ContasReceber",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_Users_UsuarioCadastroId",
                        column: x => x.UsuarioCadastroId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recebimentos_Users_UsuarioEstornoId",
                        column: x => x.UsuarioEstornoId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Glosas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoId = table.Column<int>(type: "int", nullable: false),
                    FaturamentoItemId = table.Column<int>(type: "int", nullable: true),
                    CodigoMotivo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DescricaoMotivo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValorGlosado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataGlosa = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Glosas", x => x.Id);
                    table.CheckConstraint("CK_Glosas_Valor", "[ValorGlosado] > 0");
                    table.ForeignKey(
                        name: "FK_Glosas_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Glosas_FaturamentoItens_FaturamentoItemId",
                        column: x => x.FaturamentoItemId,
                        principalTable: "FaturamentoItens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Glosas_Faturamentos_FaturamentoId",
                        column: x => x.FaturamentoId,
                        principalTable: "Faturamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecursosGlosa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClinicaId = table.Column<int>(type: "int", nullable: false),
                    GlosaId = table.Column<int>(type: "int", nullable: false),
                    DataEnvio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Justificativa = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ValorRecorrido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DataResposta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValorRecuperado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    DataAtualizacao = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecursosGlosa", x => x.Id);
                    table.CheckConstraint("CK_RecursosGlosa_Valores", "[ValorRecorrido] > 0 AND [ValorRecuperado] >= 0 AND [ValorRecuperado] <= [ValorRecorrido]");
                    table.ForeignKey(
                        name: "FK_RecursosGlosa_Clinicas_ClinicaId",
                        column: x => x.ClinicaId,
                        principalTable: "Clinicas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecursosGlosa_Glosas_GlosaId",
                        column: x => x.GlosaId,
                        principalTable: "Glosas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Clinicas",
                columns: new[] { "Id", "AssinaturaStatus", "AssinaturaValidaAte", "Ativa", "Cnpj", "DataAtualizacao", "DataCadastro", "FotoClinica", "LimiteUsuarios", "ModulosLiberados", "Nome", "Plano", "Slug", "TrialAte" },
                values: new object[] { 1, "Trial", null, true, null, null, new DateTime(2026, 7, 3, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "HemoDinks", "Trial", "hemodinks", null });

            migrationBuilder.InsertData(
                table: "Perfis",
                columns: new[] { "Id", "Nome" },
                values: new object[,]
                {
                    { 1, "Administrador" },
                    { 2, "Médicos" },
                    { 3, "Pacientes" },
                    { 4, "Controller" },
                    { 5, "SuperAdministrador" },
                    { 6, "Equipe" }
                });

            migrationBuilder.InsertData(
                table: "ConfiguracoesSistema",
                columns: new[] { "Id", "ClinicaId", "DataAtualizacao", "DataCadastro", "FotoEmpresa", "NomeEmpresa" },
                values: new object[] { 1, 1, null, new DateTime(2026, 6, 22, 0, 0, 0, 0, DateTimeKind.Utc), null, "Hemodinks" });

            migrationBuilder.InsertData(
                table: "Convenios",
                columns: new[] { "IdConvenio", "ClinicaId", "DescricaoConvenio" },
                values: new object[,]
                {
                    { 1, 1, "Amil" },
                    { 2, 1, "Bradesco Saúde" },
                    { 3, 1, "Cemig Saúde" },
                    { 4, 1, "Fusex" },
                    { 5, 1, "Geap" },
                    { 6, 1, "Ipsemg" },
                    { 7, 1, "Particular" },
                    { 8, 1, "Sul América" },
                    { 9, 1, "Unimed Uberlândia - Plano  Unimed Intercâmbio" }
                });

            migrationBuilder.InsertData(
                table: "Hospitais",
                columns: new[] { "Id", "ClinicaId", "Nome" },
                values: new object[,]
                {
                    { 1, 1, "Santa Clara - Mater Dei" },
                    { 2, 1, "Santa Genoveva - Mater Dei" },
                    { 3, 1, "UMC - Complexo Hospitalar" }
                });

            migrationBuilder.InsertData(
                table: "OPME",
                columns: new[] { "IdFornecedor", "ClinicaId", "Fornecedor" },
                values: new object[,]
                {
                    { 1, 1, "Promedom" },
                    { 2, 1, "AVL" },
                    { 3, 1, "GE" },
                    { 4, 1, "Spyner" }
                });

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
                name: "IX_AgendaNotifications_EventId",
                table: "AgendaNotifications",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_RecipientUserId",
                table: "AgendaNotifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgendaNotifications_SenderUserId",
                table: "AgendaNotifications",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoArquivos_AtendimentoCirurgicoId",
                table: "AtendimentoArquivos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoArquivos_ClinicaId_AtendimentoCirurgicoId",
                table: "AtendimentoArquivos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoProcedimentos_AtendimentoCirurgicoId",
                table: "AtendimentoProcedimentos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentoProcedimentos_ClinicaId_AtendimentoCirurgicoId_Ordem",
                table: "AtendimentoProcedimentos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ClinicaId_PacienteId_DataProcedimento",
                table: "AtendimentosCirurgicos",
                columns: new[] { "ClinicaId", "PacienteId", "DataProcedimento" });

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_ConvenioId",
                table: "AtendimentosCirurgicos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_HospitalId",
                table: "AtendimentosCirurgicos",
                column: "HospitalId");

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
                name: "IX_AtendimentosCirurgicos_OpmeFornecedorId",
                table: "AtendimentosCirurgicos",
                column: "OpmeFornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_AtendimentosCirurgicos_PacienteId",
                table: "AtendimentosCirurgicos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_Acao_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "Acao", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_ClinicaId_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "ClinicaId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_DataCadastro",
                table: "AuditoriasPlataforma",
                column: "DataCadastro");

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriasPlataforma_UsuarioGlobalId_DataCadastro",
                table: "AuditoriasPlataforma",
                columns: new[] { "UsuarioGlobalId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_RefreshTokenHash",
                table: "AuthenticationSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_UsuarioClinicaId",
                table: "AuthenticationSessions",
                column: "UsuarioClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_UsuarioGlobalId_RevokedAt_LastActivityAt",
                table: "AuthenticationSessions",
                columns: new[] { "UsuarioGlobalId", "RevokedAt", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CBHPMGeral_Codigo",
                table: "CBHPMGeral",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CBHPMGeral_Porte",
                table: "CBHPMGeral",
                column: "Porte");

            migrationBuilder.CreateIndex(
                name: "IX_Clinicas_Cnpj",
                table: "Clinicas",
                column: "Cnpj",
                unique: true,
                filter: "[Cnpj] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Clinicas_Slug",
                table: "Clinicas",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesSistema_ClinicaId",
                table: "ConfiguracoesSistema",
                column: "ClinicaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_FaturamentoId_DataVencimento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "FaturamentoId", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_NumeroDocumento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "NumeroDocumento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ClinicaId_Status_DataVencimento",
                table: "ContasReceber",
                columns: new[] { "ClinicaId", "Status", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_ConvenioId",
                table: "ContasReceber",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_FaturamentoId",
                table: "ContasReceber",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContasReceber_PacienteId",
                table: "ContasReceber",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ClinicaId_ConvenioId_CbhpmCodigo_Ativo",
                table: "ConvenioProcedimentoPrecos",
                columns: new[] { "ClinicaId", "ConvenioId", "CbhpmCodigo", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ClinicaId_ConvenioId_CbhpmCodigo_VigenciaInicio",
                table: "ConvenioProcedimentoPrecos",
                columns: new[] { "ClinicaId", "ConvenioId", "CbhpmCodigo", "VigenciaInicio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConvenioProcedimentoPrecos_ConvenioId",
                table: "ConvenioProcedimentoPrecos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_Convenios_ClinicaId_DescricaoConvenio",
                table: "Convenios",
                columns: new[] { "ClinicaId", "DescricaoConvenio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_ClinicaId_EquipeId_ExpiraEm_UtilizadoEm",
                table: "EquipeLoginDesafios",
                columns: new[] { "ClinicaId", "EquipeId", "ExpiraEm", "UtilizadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_ClinicaId_TokenHash",
                table: "EquipeLoginDesafios",
                columns: new[] { "ClinicaId", "TokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeLoginDesafios_EquipeId",
                table: "EquipeLoginDesafios",
                column: "EquipeId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_ClinicaId_UserId_Ativo",
                table: "EquipeMembros",
                columns: new[] { "ClinicaId", "UserId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_EquipeId_UserId",
                table: "EquipeMembros",
                columns: new[] { "EquipeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeMembros_UserId",
                table: "EquipeMembros",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_ClinicaId_Ativo",
                table: "EquipeOperadores",
                columns: new[] { "ClinicaId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_EquipeId_UserId",
                table: "EquipeOperadores",
                columns: new[] { "EquipeId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipeOperadores_UserId",
                table: "EquipeOperadores",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_ClinicaId_Nome",
                table: "Equipes",
                columns: new[] { "ClinicaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipes_UsuarioLoginId",
                table: "Equipes",
                column: "UsuarioLoginId",
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
                name: "IX_Events_MedicalUserId",
                table: "Events",
                column: "MedicalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UserId",
                table: "Events",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoHistoricoArquivos_ClinicaId_Ano_Mes_DataUpload",
                table: "FaturamentoHistoricoArquivos",
                columns: new[] { "ClinicaId", "Ano", "Mes", "DataUpload" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_AtendimentoProcedimentoId",
                table: "FaturamentoItens",
                column: "AtendimentoProcedimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_ClinicaId_FaturamentoId_Ordem",
                table: "FaturamentoItens",
                columns: new[] { "ClinicaId", "FaturamentoId", "Ordem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentoItens_FaturamentoId",
                table: "FaturamentoItens",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_AtendimentoCirurgicoId",
                table: "Faturamentos",
                column: "AtendimentoCirurgicoId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ClinicaId_AtendimentoCirurgicoId_Competencia",
                table: "Faturamentos",
                columns: new[] { "ClinicaId", "AtendimentoCirurgicoId", "Competencia" });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ClinicaId_NumeroGuia",
                table: "Faturamentos",
                columns: new[] { "ClinicaId", "NumeroGuia" });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_ConvenioId",
                table: "Faturamentos",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_ConferenciaPagamentoRealizada",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "ConferenciaPagamentoRealizada" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataCadastro",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_DataPagamento",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "DataPagamento" });

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_ClinicaId_PacienteId",
                table: "FaturamentosMedicos",
                columns: new[] { "ClinicaId", "PacienteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaturamentosMedicos_PacienteId",
                table: "FaturamentosMedicos",
                column: "PacienteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_ClinicaId_PacienteId_Campo_Resolvida",
                table: "FinanceiroMigracaoInconsistencias",
                columns: new[] { "ClinicaId", "PacienteId", "Campo", "Resolvida" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_ClinicaId_Resolvida_DataCadastro",
                table: "FinanceiroMigracaoInconsistencias",
                columns: new[] { "ClinicaId", "Resolvida", "DataCadastro" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceiroMigracaoInconsistencias_PacienteId",
                table: "FinanceiroMigracaoInconsistencias",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_ClinicaId_FaturamentoId_Status",
                table: "Glosas",
                columns: new[] { "ClinicaId", "FaturamentoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_FaturamentoId",
                table: "Glosas",
                column: "FaturamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Glosas_FaturamentoItemId",
                table: "Glosas",
                column: "FaturamentoItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_GrupoMedicoId",
                table: "GrupoMedicoUsuarios",
                columns: new[] { "ClinicaId", "GrupoMedicoId" });

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_ClinicaId_UserId",
                table: "GrupoMedicoUsuarios",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_GrupoMedicoUsuarios_UserId",
                table: "GrupoMedicoUsuarios",
                column: "UserId");

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
                name: "IX_Hospitais_ClinicaId_Nome",
                table: "Hospitais",
                columns: new[] { "ClinicaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRequests_ClinicaId_Operation_Scope_IdempotencyKey",
                table: "IdempotencyRequests",
                columns: new[] { "ClinicaId", "Operation", "Scope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRequests_CreatedAt",
                table: "IdempotencyRequests",
                column: "CreatedAt");

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
                name: "IX_Licencas_UserId",
                table: "Licencas",
                column: "UserId",
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
                name: "IX_Observacoes_ObservacaoPaiId",
                table: "Observacoes",
                column: "ObservacaoPaiId");

            migrationBuilder.CreateIndex(
                name: "IX_Observacoes_PacienteId",
                table: "Observacoes",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_OPME_ClinicaId_Fornecedor",
                table: "OPME",
                columns: new[] { "ClinicaId", "Fornecedor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PacienteArquivos_ClinicaId_PacienteId",
                table: "PacienteArquivos",
                columns: new[] { "ClinicaId", "PacienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteArquivos_PacienteId",
                table: "PacienteArquivos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_CbhpmCodigo",
                table: "PacienteProcedimentos",
                columns: new[] { "ClinicaId", "CbhpmCodigo" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_ClinicaId_PacienteId",
                table: "PacienteProcedimentos",
                columns: new[] { "ClinicaId", "PacienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PacienteProcedimentos_PacienteId",
                table: "PacienteProcedimentos",
                column: "PacienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_CbhpmCodigo",
                table: "Pacientes",
                column: "CbhpmCodigo");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId",
                table: "Pacientes",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_Data",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ClinicaId_DataAtendimento",
                table: "Pacientes",
                columns: new[] { "ClinicaId", "DataAtendimento" });

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_ConvenioId",
                table: "Pacientes",
                column: "ConvenioId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_HospitalId",
                table: "Pacientes",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoAuxiliar1UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar1UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoAuxiliar2UserId",
                table: "Pacientes",
                column: "MedicoAuxiliar2UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_MedicoUserId",
                table: "Pacientes",
                column: "MedicoUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_OpmeFornecedorId",
                table: "Pacientes",
                column: "OpmeFornecedorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pacientes_UserId",
                table: "Pacientes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ClinicaId",
                table: "PasswordResetTokens",
                column: "ClinicaId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ClinicaId_UserId_UsedAt_ExpiresAt",
                table: "PasswordResetTokens",
                columns: new[] { "ClinicaId", "UserId", "UsedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Perfis_Nome",
                table: "Perfis",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ClinicaId_ContaReceberId_Estornado",
                table: "Recebimentos",
                columns: new[] { "ClinicaId", "ContaReceberId", "Estornado" });

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_ContaReceberId",
                table: "Recebimentos",
                column: "ContaReceberId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioCadastroId",
                table: "Recebimentos",
                column: "UsuarioCadastroId");

            migrationBuilder.CreateIndex(
                name: "IX_Recebimentos_UsuarioEstornoId",
                table: "Recebimentos",
                column: "UsuarioEstornoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecursosGlosa_ClinicaId_GlosaId_Status",
                table: "RecursosGlosa",
                columns: new[] { "ClinicaId", "GlosaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecursosGlosa_GlosaId",
                table: "RecursosGlosa",
                column: "GlosaId");

            migrationBuilder.CreateIndex(
                name: "IX_TemporaryAccessCredentials_ClinicaId_UserId",
                table: "TemporaryAccessCredentials",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserArquivos_ClinicaId_UserId",
                table: "UserArquivos",
                columns: new[] { "ClinicaId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserArquivos_UserId",
                table: "UserArquivos",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalAcceptances_ClinicaId_UserId_AcceptedAtUtc",
                table: "UserLegalAcceptances",
                columns: new[] { "ClinicaId", "UserId", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalAcceptances_ClinicaId_UserId_DocumentType_DocumentVersion",
                table: "UserLegalAcceptances",
                columns: new[] { "ClinicaId", "UserId", "DocumentType", "DocumentVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalAcceptances_UserId",
                table: "UserLegalAcceptances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPrivacyPreferences_ClinicaId_UserId",
                table: "UserPrivacyPreferences",
                columns: new[] { "ClinicaId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPrivacyPreferences_UserId",
                table: "UserPrivacyPreferences",
                column: "UserId");

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
                unique: true,
                filter: "[PerfilId] <> 6");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PerfilId",
                table: "Users",
                column: "PerfilId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Telefone",
                table: "Users",
                column: "Telefone");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_ClinicaId_PerfilId_Ativo",
                table: "UsuariosClinicas",
                columns: new[] { "ClinicaId", "PerfilId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_PerfilId",
                table: "UsuariosClinicas",
                column: "PerfilId");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_UserId",
                table: "UsuariosClinicas",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosClinicas_UsuarioGlobalId_ClinicaId",
                table: "UsuariosClinicas",
                columns: new[] { "UsuarioGlobalId", "ClinicaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_Ativo",
                table: "UsuariosGlobais",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_BloqueadoAte",
                table: "UsuariosGlobais",
                column: "BloqueadoAte");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosGlobais_Email",
                table: "UsuariosGlobais",
                column: "Email",
                unique: true);

            AddLegacyState(migrationBuilder);
            AddFullTextSearch(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropFullTextSearch(migrationBuilder);

            migrationBuilder.DropTable(
                name: "AgendaNotifications");

            migrationBuilder.DropTable(
                name: "AtendimentoArquivos");

            migrationBuilder.DropTable(
                name: "AuditoriasPlataforma");

            migrationBuilder.DropTable(
                name: "AuthenticationSessions");

            migrationBuilder.DropTable(
                name: "CBHPMGeral");

            migrationBuilder.DropTable(
                name: "ConfiguracoesSistema");

            migrationBuilder.DropTable(
                name: "ConvenioProcedimentoPrecos");

            migrationBuilder.DropTable(
                name: "EquipeLoginDesafios");

            migrationBuilder.DropTable(
                name: "EquipeMembros");

            migrationBuilder.DropTable(
                name: "EquipeOperadores");

            migrationBuilder.DropTable(
                name: "FaturamentoHistoricoArquivos");

            migrationBuilder.DropTable(
                name: "FaturamentosMedicos");

            migrationBuilder.DropTable(
                name: "FinanceiroMigracaoInconsistencias");

            migrationBuilder.DropTable(
                name: "GrupoMedicoUsuarios");

            migrationBuilder.DropTable(
                name: "IdempotencyRequests");

            migrationBuilder.DropTable(
                name: "Licencas");

            migrationBuilder.DropTable(
                name: "Observacoes");

            migrationBuilder.DropTable(
                name: "PacienteArquivos");

            migrationBuilder.DropTable(
                name: "PacienteProcedimentos");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "Recebimentos");

            migrationBuilder.DropTable(
                name: "RecursosGlosa");

            migrationBuilder.DropTable(
                name: "TemporaryAccessCredentials");

            migrationBuilder.DropTable(
                name: "UserArquivos");

            migrationBuilder.DropTable(
                name: "UserLegalAcceptances");

            migrationBuilder.DropTable(
                name: "UserPrivacyPreferences");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "UsuariosClinicas");

            migrationBuilder.DropTable(
                name: "Equipes");

            migrationBuilder.DropTable(
                name: "GruposMedicos");

            migrationBuilder.DropTable(
                name: "ContasReceber");

            migrationBuilder.DropTable(
                name: "Glosas");

            migrationBuilder.DropTable(
                name: "UsuariosGlobais");

            migrationBuilder.DropTable(
                name: "FaturamentoItens");

            migrationBuilder.DropTable(
                name: "AtendimentoProcedimentos");

            migrationBuilder.DropTable(
                name: "Faturamentos");

            migrationBuilder.DropTable(
                name: "AtendimentosCirurgicos");

            migrationBuilder.DropTable(
                name: "Pacientes");

            migrationBuilder.DropTable(
                name: "Convenios");

            migrationBuilder.DropTable(
                name: "Hospitais");

            migrationBuilder.DropTable(
                name: "OPME");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Clinicas");

            migrationBuilder.DropTable(
                name: "Perfis");
        }
    }
}
