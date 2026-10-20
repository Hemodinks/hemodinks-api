using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class FaturamentoFinancialContractTests
{
    private static readonly DateTime ReferenceDate = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FinancialFields_AgreeAcrossBillingPatientListAndDetail_WithoutInferringSettlement(bool paid, bool hasDate)
    {
        await using var db = TestDbContextFactory.Create();
        db.Users.Add(SyntheticUser(17610, 1, Perfil.MedicosId));
        var record = SyntheticRecord(17601, 1, 17610, paid);
        record.FaturamentoMedico!.DataPagamento = hasDate ? ReferenceDate.AddMonths(-2) : null;
        db.Pacientes.Add(record);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var billing = Assert.Single((await Billing(db, Perfil.AdministradorId)).Items);
        var list = Assert.Single((await new GetAllPacientesQueryHandler(db, NullLogger<GetAllPacientesQueryHandler>.Instance)
            .Handle(new GetAllPacientesQuery { CurrentPerfilId = Perfil.AdministradorId, CurrentUserId = 17699 }, default)).Items);
        var detail = await new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance)
            .Handle(new GetPacienteByIdQuery(17601, 17699, Perfil.AdministradorId), default);
        Assert.NotNull(detail);

        foreach (var dto in new[] { billing, list, detail })
        {
            Assert.Equal(17601, dto.Id);
            Assert.Equal(paid, dto.StatusPago);
            Assert.Equal("150.50", dto.Pagamento);
            Assert.Equal("20.25", dto.RepasseGlosa);
            Assert.NotNull(dto.Faturamento);
            Assert.Equal(200m, dto.Faturamento.HonorariosCirurgiao);
            Assert.Equal(150.50m, dto.Faturamento.RepasseMedico);
            Assert.Equal(20.25m, dto.Faturamento.ValorGlosa);
            Assert.Equal(paid, dto.Faturamento.ConferenciaPagamentoRealizada);
            Assert.Equal(hasDate ? ReferenceDate.AddMonths(-2) : (DateTime?)null, dto.Faturamento.DataPagamento);
            Assert.Null(dto.Faturamento.GuiaAutorizacaoConvenio);
            Assert.Null(dto.Faturamento.GuiaInternacaoOuSadt);
            Assert.Null(dto.Faturamento.TissXmlStatus);
            Assert.Equal(0, dto.ArquivosCount);
            Assert.Null(dto.Cpf);
        }
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRecord_WithoutBillingOrAmount_PreservesItsDeclaredPaymentStatus(bool paid)
    {
        await using var db = TestDbContextFactory.Create();
        db.Users.Add(SyntheticUser(17610, 1, Perfil.MedicosId));
        var record = SyntheticRecord(17601, 1, 17610, paid);
        record.FaturamentoMedico = null;
        record.Pagamento = null;
        record.RepasseGlosa = null;
        db.Pacientes.Add(record);
        await db.SaveChangesAsync();

        var dto = Assert.Single((await Billing(db, Perfil.AdministradorId)).Items);

        Assert.Equal(paid, dto.StatusPago);
        Assert.Null(dto.Faturamento);
        Assert.Null(dto.Pagamento);
        Assert.Null(dto.RepasseGlosa);
    }

    [Fact]
    public async Task TwoFilteredRecords_PreservePaymentStatusAndStablePages_WhenChecklistDataIsIncomplete()
    {
        await using var db = TestDbContextFactory.Create();
        db.Users.AddRange(SyntheticUser(17610, 1, Perfil.MedicosId), SyntheticUser(17611, 1, Perfil.MedicosId));
        db.Pacientes.AddRange(SyntheticRecord(17601, 1, 17610, true), SyntheticRecord(17602, 1, 17610, false),
            SyntheticRecord(17603, 1, 17611, false));
        await db.SaveChangesAsync();
        var handler = new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);
        GetAllFaturamentosMedicosQuery Query(int page, int size) => new()
        {
            Page = page, PageSize = size, CurrentPerfilId = Perfil.MedicosId, CurrentUserId = 17610,
            Medico = "outside-scope", CompetenciaInicio = ReferenceDate, CompetenciaFinal = ReferenceDate
        };

        var all = await handler.Handle(Query(1, 100), default);
        var first = await handler.Handle(Query(1, 1), default);
        var second = await handler.Handle(Query(2, 1), default);
        var repeated = await handler.Handle(Query(1, 1), default);

        Assert.Equal(new[] { 17601, 17602 }, all.Items.Select(x => x.Id));
        Assert.Equal(new[] { 17601 }, first.Items.Select(x => x.Id));
        Assert.Equal(new[] { 17602 }, second.Items.Select(x => x.Id));
        Assert.Equal(first.Items.Select(x => x.Id), repeated.Items.Select(x => x.Id));
        Assert.Equal(2, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, second.TotalItems);
        Assert.True(all.Items[0].StatusPago);
        Assert.False(all.Items[1].StatusPago);
        Assert.All(all.Items, item => Assert.Null(item.Faturamento!.GuiaInternacaoOuSadt));

        var emptyMonth = Query(1, 100);
        emptyMonth.CompetenciaInicio = ReferenceDate.AddMonths(1);
        emptyMonth.CompetenciaFinal = ReferenceDate.AddMonths(1);
        Assert.Empty((await handler.Handle(emptyMonth, default)).Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyConferenceMismatch_DoesNotOverrideThePatientsDeclaredPaymentStatus(bool paid)
    {
        await using var db = TestDbContextFactory.Create();
        db.Users.Add(SyntheticUser(17610, 1, Perfil.MedicosId));
        var record = SyntheticRecord(17601, 1, 17610, paid);
        record.FaturamentoMedico!.ConferenciaPagamentoRealizada = !paid;
        record.FaturamentoMedico.DataPagamento = ReferenceDate;
        db.Pacientes.Add(record);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var billing = Assert.Single((await Billing(db, Perfil.AdministradorId)).Items);
        var detail = await new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance)
            .Handle(new GetPacienteByIdQuery(17601, 17699, Perfil.AdministradorId), default);

        Assert.NotNull(detail);
        Assert.Equal(paid, billing.StatusPago);
        Assert.Equal(paid, detail.StatusPago);
        Assert.Equal(!paid, billing.Faturamento!.ConferenciaPagamentoRealizada);
        Assert.Equal(!paid, detail.Faturamento!.ConferenciaPagamentoRealizada);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(Perfil.AdministradorId)]
    [InlineData(Perfil.SuperAdministradorId)]
    [InlineData(Perfil.ControllerId)]
    public async Task FinancialData_RemainsIsolated_WhenAuthenticatedClinicChanges(int perfil)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var platform = new ClinicaContext();
        platform.SetPlatformScope();
        await using (var seed = new AppDbContext(options, platform))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Clinicas.Add(new Clinica { Id = 2, Nome = "TENANT-176-B", Slug = "tenant-176-b" });
            seed.Users.AddRange(SyntheticUser(17610, 1, Perfil.MedicosId), SyntheticUser(17611, 2, Perfil.MedicosId));
            seed.Pacientes.AddRange(SyntheticRecord(17601, 1, 17610, true), SyntheticRecord(17602, 2, 17611, false));
            await seed.SaveChangesAsync();
        }
        var clinic = new ClinicaContext();
        clinic.SetCurrent(1, Clinica.DefaultSlug);
        await using var db = new AppDbContext(options, clinic);
        var detail = new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance);

        Assert.Equal(new[] { 17601 }, (await Billing(db, perfil)).Items.Select(x => x.Id));
        Assert.Null(await detail.Handle(new GetPacienteByIdQuery(17602, 17699, perfil), default));
        clinic.SetCurrent(2, "tenant-176-b");
        Assert.Equal(new[] { 17602 }, (await Billing(db, perfil)).Items.Select(x => x.Id));
        Assert.Null(await detail.Handle(new GetPacienteByIdQuery(17601, 17699, perfil), default));
        Assert.False((await detail.Handle(new GetPacienteByIdQuery(17602, 17699, perfil), default))!.StatusPago);
    }

    [Fact]
    public async Task TeamFinancialScope_UsesOnlyActiveMembers_AndDoesNotRevealAnotherDoctorsDetail()
    {
        await using var db = TestDbContextFactory.Create();
        db.Users.AddRange(SyntheticUser(17610, 1, Perfil.MedicosId), SyntheticUser(17611, 1, Perfil.MedicosId));
        db.Pacientes.AddRange(SyntheticRecord(17601, 1, 17610, true), SyntheticRecord(17602, 1, 17611, false));
        db.Equipes.Add(new Equipe { Id = 17620, ClinicaId = 1, Nome = "TEAM-176", UsuarioLogin = SyntheticUser(17630, 1, Perfil.EquipeId) });
        db.EquipeMembros.AddRange(new EquipeMembro { ClinicaId = 1, EquipeId = 17620, UserId = 17610, Ativo = true },
            new EquipeMembro { ClinicaId = 1, EquipeId = 17620, UserId = 17611, Ativo = false });
        await db.SaveChangesAsync();

        var result = await new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance)
            .Handle(new GetAllFaturamentosMedicosQuery { CurrentPerfilId = Perfil.EquipeId, CurrentUserId = 17630, CurrentEquipeId = 17620 }, default);
        var detail = new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance);

        Assert.Equal(new[] { 17601 }, result.Items.Select(x => x.Id));
        Assert.NotNull(await detail.Handle(new GetPacienteByIdQuery(17601, 17630, Perfil.EquipeId, 17620), default));
        Assert.Null(await detail.Handle(new GetPacienteByIdQuery(17602, 17630, Perfil.EquipeId, 17620), default));
    }

    private static Task<HemodinksAPI.Application.Features.Common.PagedResult<PacienteDto>> Billing(AppDbContext db, int perfil) =>
        new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance)
            .Handle(new GetAllFaturamentosMedicosQuery { CurrentPerfilId = perfil, CurrentUserId = 17699 }, default);

    private static User SyntheticUser(int id, int clinic, int perfil) => new()
    {
        Id = id, ClinicaId = clinic, Nome = string.Empty, Telefone = string.Empty,
        Email = $"u{id}@example.test", Senha = "unused-fixture-hash", PerfilId = perfil, DataCadastro = ReferenceDate, Ativo = true
    };

    private static Paciente SyntheticRecord(int id, int clinic, int doctor, bool paid) => new()
    {
        Id = id, ClinicaId = clinic, User = SyntheticUser(id + 100, clinic, Perfil.PacientesId), NomePaciente = string.Empty,
        MedicoUserId = doctor, StatusPago = paid, Data = ReferenceDate, Pagamento = "150.50", RepasseGlosa = "20.25",
        FaturamentoMedico = new FaturamentoMedico
        {
            ClinicaId = clinic, HonorariosCirurgiao = 200m, RepasseMedico = 150.50m, ValorGlosa = 20.25m,
            ConferenciaPagamentoRealizada = paid, DataCadastro = ReferenceDate, CompetenciaInicio = ReferenceDate.AddMonths(-1)
        }
    };
}
