using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Financeiro;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class SurgeryTypeQueriesTests
{
    [Fact]
    public async Task TypeFilters_KeepLegacyAndAttendanceIndependent_AndDoNotDuplicateFinancialTotals()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new PlatformDbContext(options);
        await seed.Database.EnsureCreatedAsync();
        seed.Clinicas.Add(new Clinica { Id = 2, Nome = "Other", Slug = "other-type" });
        var doctor = User(1, "doctor");
        var patient = Patient(1, "elective", TipoCirurgia.Eletiva);
        var urgentLegacy = Patient(1, "urgent", TipoCirurgia.Urgencia);
        var remote = Patient(2, "remote", TipoCirurgia.Urgencia);
        var remoteDoctor = User(2, "remote-doctor");
        var urgentAttendance = new AtendimentoCirurgico { ClinicaId = 1, Paciente = patient,
            MedicoResponsavel = doctor, DataProcedimento = new(2030, 1, 1), TipoCirurgia = TipoCirurgia.Urgencia };
        var electiveAttendance = new AtendimentoCirurgico { ClinicaId = 1, Paciente = patient,
            MedicoResponsavel = doctor, DataProcedimento = new(2030, 1, 1), TipoCirurgia = TipoCirurgia.Eletiva };
        var remoteAttendance = new AtendimentoCirurgico { ClinicaId = 2, Paciente = remote,
            MedicoResponsavel = remoteDoctor, DataProcedimento = new(2030, 1, 1), TipoCirurgia = TipoCirurgia.Urgencia };
        seed.Pacientes.Add(urgentLegacy);
        seed.Faturamentos.AddRange(Bill(urgentAttendance, doctor, 100), Bill(urgentAttendance, doctor, 200),
            Bill(electiveAttendance, doctor, 500), Bill(remoteAttendance, remoteDoctor, 700));
        await seed.SaveChangesAsync();
        var tenant = new ClinicaContext(); tenant.SetCurrent(1, Clinica.DefaultSlug);
        await using var db = new AppDbContext(options, tenant);
        var patients = new GetAllPacientesQueryHandler(db, NullLogger<GetAllPacientesQueryHandler>.Instance);
        var selected = await patients.Handle(new() { CurrentPerfilId = Perfil.SuperAdministradorId,
            TipoCirurgia = TipoCirurgia.Urgencia, PageSize = 1 }, default);
        Assert.Equal(1, selected.TotalItems);
        Assert.Equal(urgentLegacy.Id, Assert.Single(selected.Items).Id);
        var sorted = await patients.Handle(new() { CurrentPerfilId = Perfil.AdministradorId,
            SortBy = "tipoCirurgia", SortDirection = "asc" }, default);
        Assert.Equal(new[] { TipoCirurgia.Eletiva, TipoCirurgia.Urgencia }, sorted.Items.Select(x => x.TipoCirurgia));
        var medicalBilling = new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);
        var medical = await medicalBilling.Handle(new() { CurrentPerfilId = Perfil.AdministradorId, TipoCirurgia = TipoCirurgia.Urgencia }, default);
        Assert.Equal(urgentLegacy.Id, Assert.Single(medical.Items).Id);
        Assert.Equal(1, medical.TotalItems);
        var attendances = await new ListarAtendimentosQueryHandler(db).Handle(new() { TipoCirurgia = TipoCirurgia.Urgencia,
            CurrentPerfilId = Perfil.AdministradorId }, default);
        Assert.Equal(urgentAttendance.Id, Assert.Single(attendances).Id);
        var invoices = await new ListarFaturamentosQueryHandler(db).Handle(new() { TipoCirurgia = TipoCirurgia.Urgencia,
            CurrentPerfilId = Perfil.AdministradorId }, default);
        Assert.Equal(2, invoices.Count);
        Assert.All(invoices, x => Assert.Equal(TipoCirurgia.Urgencia, x.TipoCirurgia));
        Assert.Equal(300, invoices.Sum(x => x.ValorApresentado));
        var page = await new PesquisarFaturamentosQueryHandler(db).Handle(new(2, 1, null, null, null, null, null, 0, Perfil.AdministradorId)
            { TipoCirurgia = TipoCirurgia.Urgencia }, default);
        Assert.Equal(2, page.TotalItems);
        Assert.Single(page.Items);
        var reportHandler = new ObterFinanceiroResumoQueryHandler(db);
        var report = await reportHandler.Handle(new(null, null, null, null, null) { TipoCirurgia = TipoCirurgia.Urgencia }, default);
        Assert.Equal(300, report.ValorApresentado);
        Assert.Equal(30, report.ValorRecebido);
        Assert.Equal(270, report.SaldoAberto);
        Assert.Equal(30, report.RecebimentosPeriodo);
        Assert.Equal(300, Assert.Single(report.PorCompetencia).Apresentado);
        var byType = Assert.Single(report.PorTipoCirurgia);
        Assert.Equal(TipoCirurgia.Urgencia, byType.TipoCirurgia);
        Assert.Equal(1, byType.QuantidadeCirurgiasFaturadas);
        var all = await reportHandler.Handle(new(null, null, null, null, null), default);
        Assert.Equal(800, all.ValorApresentado);
        Assert.Equal(2, all.PorTipoCirurgia.Count);
        Assert.Equal(2, all.PorTipoCirurgia.Sum(x => x.QuantidadeCirurgiasFaturadas));
        var hidden = await reportHandler.Handle(new(null, null, null, null, remote.Id) { TipoCirurgia = TipoCirurgia.Urgencia }, default);
        Assert.Equal(0, hidden.ValorApresentado);
        Assert.Equal(0, hidden.RecebimentosPeriodo);
        Assert.Empty(hidden.PorTipoCirurgia);
        Assert.Null(await new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance)
            .Handle(new(remote.Id, 0, Perfil.SuperAdministradorId), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => patients.Handle(new() { TipoCirurgia = (TipoCirurgia)999 }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reportHandler.Handle(new(null, null, null, null, null)
            { TipoCirurgia = (TipoCirurgia)999 }, default));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(TipoCirurgia.Eletiva, (await db.Pacientes.AsNoTracking().SingleAsync(x => x.Id == patient.Id)).TipoCirurgia);
    }

    private static User User(int clinic, string name) => new() { ClinicaId = clinic, Nome = name,
        Email = $"{name}@example.invalid", Telefone = "11999990000", Senha = "synthetic-hash", PerfilId = Perfil.MedicosId };
    private static Paciente Patient(int clinic, string name, TipoCirurgia type) => new() { ClinicaId = clinic,
        NomePaciente = name, TipoCirurgia = type, User = new User { ClinicaId = clinic, Nome = name,
            Email = $"patient-{name}@example.invalid", Telefone = "11999990000", Senha = "synthetic-hash", PerfilId = Perfil.PacientesId } };
    private static Faturamento Bill(AtendimentoCirurgico attendance, User doctor, decimal amount)
    {
        var bill = new Faturamento { ClinicaId = attendance.ClinicaId, AtendimentoCirurgico = attendance,
            Competencia = new(2030, 1, 1), ValorApresentado = amount, ValorReconhecido = amount };
        var account = new ContaReceber { ClinicaId = attendance.ClinicaId, Paciente = attendance.Paciente,
            NumeroDocumento = Guid.NewGuid().ToString("N"), Descricao = "Synthetic", Competencia = bill.Competencia,
            DataEmissao = bill.Competencia, DataVencimento = bill.Competencia.AddMonths(1), ValorOriginal = amount,
            ValorAjustado = amount, ValorRecebido = amount / 10, SaldoAberto = amount * .9m };
        account.Recebimentos.Add(new Recebimento { ClinicaId = attendance.ClinicaId,
            UsuarioCadastro = doctor, DataRecebimento = bill.Competencia, ValorRecebido = amount / 10 });
        bill.ContasReceber.Add(account);
        return bill;
    }
}
