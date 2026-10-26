using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Features.Clinics.Platform;
using HemodinksAPI.Application.Features.Events.Queries;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.Financeiro;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class CriticalQuerySqlServerTests(ITestOutputHelper output)
{
    private static readonly DateTime Period = new(2030, 1, 1);

    // Captures only commands from this synthetic, disposable database. Never uses app configuration.
    private sealed class Commands : DbCommandInterceptor, IDisposable
    {
        public List<SqlCommand> Statements { get; } = [];
        public int Writes { get; private set; }
        private void Capture(DbCommand command)
        {
            if (command.CommandText.Contains("UPDATE [", StringComparison.Ordinal)) { Writes++; return; }
            var copy = new SqlCommand(command.CommandText);
            foreach (SqlParameter parameter in command.Parameters)
                copy.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
            Statements.Add(copy);
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result) { Capture(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Writes++; return ValueTask.FromResult(result); }
        public void Dispose()
        {
            foreach (var command in Statements) command.Dispose();
            Statements.Clear();
            Writes = 0;
        }
    }

    private sealed class NoReminders : IEventReminderProcessor
    {
        // Measure the read query separately from reminder processing and external delivery.
        public Task<int> ProcessDueRemindersAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class Notifications : INotificationService
    {
        public List<int> Recipients { get; } = [];
        public Task SendNotificationToUserAsync(int userId, string title, string message)
        { Recipients.Add(userId); return Task.CompletedTask; }
        public Task SendNotificationToMedicalProfileAsync(int profile, string title, string message)
            => throw new InvalidOperationException("Unscoped broadcast is forbidden.");
    }

    [Theory]
    [Trait("Category", "SqlServer")]
    [InlineData(6)]
    [InlineData(24)]
    public async Task CriticalReads_HaveBoundedCommands_IsolateClinics_AndAvoidUnusedBillingCollections(int size)
    {
        if (Environment.GetEnvironmentVariable("HEMODINKS_TEST_LOCALDB") != "1"
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING")))
            Assert.Skip("Requires isolated SQL Server; set HEMODINKS_TEST_LOCALDB=1.");

        var connection = SqlServerTestConnection.Create($"HemodinksQueryAudit_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            await Seed(setup, size);
            using var commands = new Commands();
            var measuredOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connection).AddInterceptors(commands).Options;
            var tenant = new ClinicaContext();
            tenant.SetCurrent(1, Clinica.DefaultSlug);
            await using var db = new AppDbContext(measuredOptions, tenant);

            async Task Measure(string name, int expectedCommands, Func<Task> read)
            {
                await read(); // Query compilation, JIT and connection warm-up are outside the sample.
                var samples = new List<double>();
                for (var i = 0; i < 20; i++)
                {
                    commands.Dispose();
                    var start = Stopwatch.GetTimestamp();
                    await read();
                    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    Assert.Equal(expectedCommands, commands.Statements.Count);
                    Assert.Equal(0, commands.Writes);
                    Assert.Empty(db.ChangeTracker.Entries());
                }
                samples.Sort();
                var (rows, indexes) = await Analyze(db, commands.Statements);
                output.WriteLine($"size={size}; {name}; samples=20; commands={expectedCommands}; wireRows={rows}; " +
                    $"p50Ms={samples[9]:F2}; p95Ms={samples[18]:F2}; indexes={indexes}");
            }

            var patients = new GetAllPacientesQueryHandler(db, NullLogger<GetAllPacientesQueryHandler>.Instance);
            await Measure("patients-page", 2, async () =>
            {
                var page = await patients.Handle(new GetAllPacientesQuery { CurrentPerfilId = Perfil.AdministradorId,
                    Page = 2, PageSize = 5 }, default);
                Assert.Equal(size, page.TotalItems);
                Assert.Equal(Math.Min(5, size - 5), page.Items.Count());
                Assert.All(page.Items, x => { Assert.StartsWith("Clinic1", x.NomePaciente); Assert.Equal(3, x.Procedimentos.Count); });
            });
            var medicalBilling = new GetAllFaturamentosMedicosQueryHandler(db, NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);
            await Measure("medical-billing-page", 2, async () =>
            {
                var page = await medicalBilling.Handle(new GetAllFaturamentosMedicosQuery { CurrentPerfilId = Perfil.AdministradorId,
                    PageSize = 5 }, default);
                Assert.Equal(size, page.TotalItems);
                Assert.All(page.Items, x => Assert.StartsWith("Clinic1", x.NomePaciente));
            });
            var doctor = setup.Users.Local.Single(x => x.ClinicaId == 1 && x.PerfilId == Perfil.MedicosId);
            var agenda = new GetEventsQueryHandler(db, new NoReminders());
            await Measure("agenda-read-only", 1, async () =>
            {
                var events = await agenda.Handle(new GetEventsQuery { CurrentUser = new(doctor.Id,
                    Perfil.AdministradorId, "Synthetic", 1), From = Period, To = Period.AddDays(1) }, default);
                Assert.Equal(size, events.Count);
                Assert.All(events, x => Assert.StartsWith("Clinic1", x.Title));
            });
            var report = new ObterFinanceiroResumoQueryHandler(db);
            await Measure("financial-report", 4, async () =>
            {
                var result = await report.Handle(new(null, null, null, null, null), default);
                Assert.Equal(size * 300m, result.ValorApresentado);
                Assert.Equal(size * 90m, result.RecebimentosPeriodo);
                Assert.Single(result.PorCompetencia);
            });
            await using var platform = new PlatformDbContext(measuredOptions);
            var platformClinics = new ClinicaPlatformRequestHandler(null!, platform, null!, null!, null!, null!, null!, null!, null!);
            await Measure("platform-clinics", 2, async () =>
            {
                var result = await platformClinics.ListClinicas(new(null, null, Perfil.SuperAdministradorId,
                    null, null, null, null, null, null), default);
                Assert.Equal(PlatformResultKind.Ok, result.Kind);
                Assert.Equal(2, Assert.IsType<List<ClinicaPlatformResponse>>(result.Value).Count);
            });
            var scopedClinics = new ClinicaPlatformRequestHandler(null!, db, null!, null!, null!, null!, null!, null!, null!);
            var clinicResult = await scopedClinics.ListClinicas(new(null, null, Perfil.AdministradorId,
                1, null, null, null, null, null), default);
            Assert.Equal(1, Assert.Single(Assert.IsType<List<ClinicaPlatformResponse>>(clinicResult.Value)).Id);
            List<FaturamentoDto> baseline = [];
            await Measure("billing-baseline-full-graph", 1, async () =>
            {
                var graph = await ListarFaturamentosQueryHandler.Full(db.Faturamentos.AsNoTracking())
                    .OrderByDescending(x => x.DataAtualizacao ?? x.DataCadastro).ThenByDescending(x => x.Id).ToListAsync();
                baseline = graph.Select(FinanceiroMapper.ToDto).ToList();
                Assert.Equal(size, baseline.Count);
            });
            var baselineRows = (await Analyze(db, commands.Statements)).Rows;
            var billing = new ListarFaturamentosQueryHandler(db);
            await Measure("billing-response", 1, async () =>
            {
                var result = await billing.Handle(new(doctor.Id, Perfil.AdministradorId), default);
                Assert.Equal(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(result));
            });
            var responseRows = (await Analyze(db, commands.Statements)).Rows;
            // These DTOs do not contain accounts/receipts. Loading them multiplies result rows needlessly.
            Assert.DoesNotContain(commands.Statements, x => x.CommandText.Contains("[Recebimentos]"));
            Assert.True(responseRows < baselineRows, $"Expected less row amplification: baseline={baselineRows}, response={responseRows}");

            var searchBilling = new PesquisarFaturamentosQueryHandler(db);
            await Measure("billing-search-page", 2, async () =>
            {
                var result = await searchBilling.Handle(new(2, 5, null, null, null, null, null, doctor.Id, Perfil.AdministradorId), default);
                Assert.Equal(size, result.TotalItems);
                Assert.Equal(JsonSerializer.Serialize(baseline.Skip(5).Take(5)), JsonSerializer.Serialize(result.Items));
            });
            Assert.DoesNotContain(commands.Statements, x => x.CommandText.Contains("[Recebimentos]"));
            var details = new ObterFaturamentoQueryHandler(db);
            commands.Dispose();
            var detail = await details.Handle(new(baseline[0].Id, doctor.Id, Perfil.AdministradorId), default);
            Assert.Equal(JsonSerializer.Serialize(baseline[0]), JsonSerializer.Serialize(detail));
            Assert.Single(commands.Statements);
            Assert.DoesNotContain(commands.Statements, x => x.CommandText.Contains("[Recebimentos]"));
            Assert.Empty(db.ChangeTracker.Entries());

            // Real SQL filters must fail closed, even for admin roles and explicit foreign IDs.
            var foreignPatient = setup.Pacientes.Local.First(x => x.ClinicaId == 2);
            var foreignBill = setup.Faturamentos.Local.First(x => x.ClinicaId == 2);
            var foreignEvent = setup.Events.Local.First(x => x.ClinicaId == 2);
            Assert.Null(await new GetPacienteByIdQueryHandler(db, NullLogger<GetPacienteByIdQueryHandler>.Instance)
                .Handle(new(foreignPatient.Id, doctor.Id, Perfil.AdministradorId), default));
            Assert.Null(await agenda.Handle(new GetEventByIdQuery { Id = foreignEvent.Id,
                CurrentUser = new(doctor.Id, Perfil.AdministradorId, "Synthetic", 1) }, default));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => details.Handle(new(foreignBill.Id, doctor.Id, Perfil.AdministradorId), default));
            var hiddenReport = await report.Handle(new(null, null, null, null, foreignPatient.Id), default);
            Assert.Equal(0m, hiddenReport.ValorApresentado);
            Assert.Equal(0m, hiddenReport.RecebimentosPeriodo);
            Assert.Empty(hiddenReport.PorCompetencia);
            // Changing the resolved tenant on the same model must not reuse a clinic's cached result.
            tenant.SetCurrent(2, "clinic2");
            var second = await billing.Handle(new(0, Perfil.SuperAdministradorId), default);
            Assert.Equal(size, second.Count);
            Assert.All(second, x => Assert.StartsWith("Clinic2", x.Paciente));
            Assert.Empty(await billing.Handle(new(doctor.Id, Perfil.MedicosId), default));
            await using var unresolved = new AppDbContext(measuredOptions, new ClinicaContext());
            Assert.Empty(await new ListarFaturamentosQueryHandler(unresolved).Handle(new(0, Perfil.SuperAdministradorId), default));

            tenant.SetCurrent(1, Clinica.DefaultSlug);
            var notifications = new Notifications();
            var actualAgenda = new GetEventsQueryHandler(db, new EventReminderProcessor(db, notifications,
                NullLogger<EventReminderProcessor>.Instance));
            var agendaRequest = new GetEventsQuery { CurrentUser = new(doctor.Id, Perfil.AdministradorId, "Synthetic", 1) };
            await Measure("agenda-reminders-idle", 2, async () =>
                Assert.Equal(size, (await actualAgenda.Handle(agendaRequest, default)).Count));

            // Measure the real GET side effect as well; every attempt re-arms only synthetic reminders.
            var reminderSamples = new List<double>();
            for (var attempt = -1; attempt < 20; attempt++)
            {
                db.ChangeTracker.Clear();
                await setup.Events.ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.NotifyUser, true).SetProperty(x => x.NextReminderAt, DateTime.UtcNow.AddMinutes(-1)));
                notifications.Recipients.Clear();
                commands.Dispose();
                var start = Stopwatch.GetTimestamp();
                Assert.Equal(size, (await actualAgenda.Handle(agendaRequest, default)).Count);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.Equal(size, notifications.Recipients.Count);
                Assert.All(notifications.Recipients, x => Assert.Equal(doctor.Id, x));
                Assert.Equal(2 + 2 * size, commands.Statements.Count); // candidates + per-event load/eligibility + GET
                Assert.Equal(size + 1, commands.Writes); // per-event lease + one SaveChanges batch
                if (attempt >= 0) reminderSamples.Add(elapsed);
            }
            reminderSamples.Sort();
            output.WriteLine($"size={size}; agenda-reminders-due; samples=20; reads={commands.Statements.Count}; " +
                $"writes={commands.Writes}; p95Ms={reminderSamples[18]:F2}");
            Assert.Equal(size, await setup.Events.CountAsync(x => x.ClinicaId == 2 && x.NextReminderAt < DateTime.UtcNow));
            Assert.Empty(await db.Events.AsNoTracking().Where(x => x.NextReminderAt < DateTime.UtcNow).ToListAsync());
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task OptimizedBillingReads_RequireDoctorRelationshipWithinTheSameClinic()
    {
        SqlServerTestConnection.RequireConfigured();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestConnection.Create($"HemodinksQueryAudit_{Guid.NewGuid():N}")).Options;
        await using var setup = new PlatformDbContext(options);
        try
        {
            await setup.Database.EnsureCreatedAsync();
            await Seed(setup, 1);
            var principal = setup.Users.Local.Single(x => x.ClinicaId == 1 && x.PerfilId == Perfil.MedicosId);
            var firstAssistant = User(1, "FirstAssistant", Perfil.MedicosId);
            var secondAssistant = User(1, "SecondAssistant", Perfil.MedicosId);
            var unrelated = User(1, "UnrelatedDoctor", Perfil.MedicosId);
            setup.Users.AddRange(firstAssistant, secondAssistant, unrelated);
            var bill = setup.Faturamentos.Local.Single(x => x.ClinicaId == 1);
            bill.AtendimentoCirurgico.MedicoAuxiliar1 = firstAssistant;
            bill.AtendimentoCirurgico.MedicoAuxiliar2 = secondAssistant;
            await setup.SaveChangesAsync();

            var tenant = new ClinicaContext();
            tenant.SetCurrent(1, Clinica.DefaultSlug);
            await using var db = new AppDbContext(options, tenant);
            var list = new ListarFaturamentosQueryHandler(db);
            var search = new PesquisarFaturamentosQueryHandler(db);
            var detail = new ObterFaturamentoQueryHandler(db);
            foreach (var doctor in new[] { principal, firstAssistant, secondAssistant })
            {
                var listed = Assert.Single(await list.Handle(new(doctor.Id, Perfil.MedicosId), default));
                Assert.Equal(bill.Id, listed.Id);
                var page = await search.Handle(new(1, 5, null, null, null, null, null, doctor.Id, Perfil.MedicosId), default);
                Assert.Equal(1, page.TotalItems);
                Assert.Equal(bill.Id, Assert.Single(page.Items).Id);
                var found = await detail.Handle(new(bill.Id, doctor.Id, Perfil.MedicosId), default);
                Assert.Equal(JsonSerializer.Serialize(listed), JsonSerializer.Serialize(found));
            }

            Assert.Empty(await list.Handle(new(unrelated.Id, Perfil.MedicosId), default));
            var hidden = await search.Handle(new(1, 5, null, null, null, null, null, unrelated.Id, Perfil.MedicosId), default);
            Assert.Equal(0, hidden.TotalItems);
            Assert.Empty(hidden.Items);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                detail.Handle(new(bill.Id, unrelated.Id, Perfil.MedicosId), default));
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<(long Rows, string Indexes)> Analyze(AppDbContext db, List<SqlCommand> statements)
    {
        // Re-execute synthetic SELECTs outside timing to measure actual wire rows before EF de-duplication.
        // Actual plans reveal the indexes chosen for this fixture, not production cardinalities.
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            long rows = 0;
            var indexes = new SortedSet<string>();
            foreach (var statement in statements)
            {
                statement.Connection = connection;
                await using (var reader = await statement.ExecuteReaderAsync())
                    while (await reader.ReadAsync()) rows++;
                using var toggle = new SqlCommand("SET STATISTICS XML ON", connection);
                await toggle.ExecuteNonQueryAsync();
                try
                {
                    var foundPlan = false;
                    await using var reader = await statement.ExecuteReaderAsync();
                    do
                    {
                        if (reader.FieldCount != 1 || !reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase)) continue;
                        while (await reader.ReadAsync())
                        {
                            var plan = XDocument.Parse(reader.GetString(0));
                            foreach (var index in plan.Descendants().Attributes("Index")) indexes.Add(index.Value);
                            foundPlan = true;
                        }
                    } while (await reader.NextResultAsync());
                    Assert.True(foundPlan, "SQL Server must return an actual execution plan for the measured SELECT.");
                }
                finally
                {
                    toggle.CommandText = "SET STATISTICS XML OFF";
                    await toggle.ExecuteNonQueryAsync();
                }
            }
            return (rows, string.Join(",", indexes));
        }
        finally { await connection.CloseAsync(); }
    }

    private static async Task Seed(PlatformDbContext db, int size)
    {
        var otherClinic = new Clinica { Nome = "Clinic2", Slug = "clinic2" };
        db.Clinicas.Add(otherClinic);
        await db.SaveChangesAsync();
        Assert.Equal(2, otherClinic.Id);
        foreach (var clinic in new[] { 1, 2 })
        {
            var doctor = User(clinic, "Doctor", Perfil.MedicosId);
            db.Users.Add(doctor);
            for (var n = 0; n < size; n++)
            {
                var patient = new Paciente { ClinicaId = clinic, NomePaciente = $"Clinic{clinic} Patient{n:D3}",
                    User = User(clinic, $"Patient{n:D3}", Perfil.PacientesId), MedicoUser = doctor, Data = Period,
                    FaturamentoMedico = new FaturamentoMedico { ClinicaId = clinic, CompetenciaInicio = Period, CompetenciaFinal = Period } };
                for (var j = 0; j < 3; j++) patient.Procedimentos.Add(new PacienteProcedimento {
                    ClinicaId = clinic, Procedimento = "Synthetic procedure", Ordem = j, CbhpmCodigo = "12345678" });
                var attendance = new AtendimentoCirurgico { ClinicaId = clinic, Paciente = patient,
                    MedicoResponsavel = doctor, DataProcedimento = Period };
                var bill = new Faturamento { ClinicaId = clinic, AtendimentoCirurgico = attendance,
                    Competencia = Period, ValorApresentado = 300, ValorGlosado = 30, ValorReconhecido = 270, DataCadastro = Period };
                for (var j = 0; j < 3; j++)
                {
                    bill.Itens.Add(new FaturamentoItem { ClinicaId = clinic, Descricao = "Synthetic item",
                        Ordem = j, ValorApresentado = 100, ValorAprovado = 90, ValorGlosado = 10, ValorUnitario = 100 });
                    var denial = new Glosa { ClinicaId = clinic, DescricaoMotivo = "Synthetic denial", ValorGlosado = 10, DataGlosa = Period };
                    for (var k = 0; k < 3; k++) denial.Recursos.Add(new RecursoGlosa { ClinicaId = clinic,
                        Justificativa = "Synthetic appeal", ValorRecorrido = 1, DataCadastro = Period.AddDays(k) });
                    bill.Glosas.Add(denial);
                    var account = new ContaReceber { ClinicaId = clinic, Paciente = patient,
                        NumeroDocumento = $"{clinic}-{n}-{j}", Descricao = "Synthetic account", Competencia = Period,
                        DataEmissao = Period, DataVencimento = Period.AddMonths(1), ValorOriginal = 100,
                        ValorAjustado = 90, ValorRecebido = 30, SaldoAberto = 60 };
                    for (var k = 0; k < 3; k++) account.Recebimentos.Add(new Recebimento { ClinicaId = clinic,
                        UsuarioCadastro = doctor, DataRecebimento = Period, ValorRecebido = 10 });
                    bill.ContasReceber.Add(account);
                }
                db.Faturamentos.Add(bill);
                db.Events.Add(new Event { ClinicaId = clinic, User = doctor, Title = $"Clinic{clinic} Event{n}",
                    Start = Period.AddHours(10), End = Period.AddHours(11) });
            }
        }
        await db.SaveChangesAsync();
        foreach (var doctor in db.Users.Local.Where(x => x.PerfilId == Perfil.MedicosId).ToList())
            await GlobalIdentityService.EnsureForUserAsync(db, doctor, default);
    }

    private static User User(int clinic, string name, int profile) => new() { ClinicaId = clinic,
        Nome = $"Clinic{clinic} {name}", Email = $"c{clinic}-{name}@example.invalid", Telefone = "11999990000",
        Senha = "synthetic-test-hash", PerfilId = profile, DataCadastro = Period };
}
