using System.Net;
using System.Text.Json;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

// #373: listas do Financeiro paginadas no servidor e isoladas por clinica.
public sealed class FinanceiroListPaginationTests : ApiEndpointTestBase
{
    public static TheoryData<string> Lists => ["/api/atendimentos-cirurgicos/", "/api/faturamentos/", "/api/financeiro/contas-receber/"];

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Lista_PaginaNoServidor_LimitaA100_E_IsolaPorClinica(string route)
    {
        using var factory = new HemodinksApiFactory();
        using var startup = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await SeedAsync(factory, Clinica.DefaultId, 25);
        await SeedAsync(factory, beta.Id, 3);

        using var admin = factory.CreateClient();
        await AuthenticateAsync(admin, Clinica.DefaultSlug);

        using (var first = await ReadAsync(admin, route))
        {
            Assert.Equal(25, first.RootElement.GetProperty("totalItems").GetInt32());
            Assert.Equal(20, first.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(2, first.RootElement.GetProperty("totalPages").GetInt32());
        }
        using (var second = await ReadAsync(admin, $"{route}?page=2&pageSize=20"))
            Assert.Equal(5, second.RootElement.GetProperty("items").GetArrayLength());
        using (var clamped = await ReadAsync(admin, $"{route}?page=1&pageSize=500"))
        {
            Assert.Equal(100, clamped.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(25, clamped.RootElement.GetProperty("items").GetArrayLength());
        }
        using (var minimum = await ReadAsync(admin, $"{route}?page=0&pageSize=0"))
        {
            Assert.Equal(1, minimum.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(1, minimum.RootElement.GetProperty("items").GetArrayLength());
        }

        using var betaAdmin = factory.CreateClient();
        await AuthenticateAsync(betaAdmin, beta.Slug);
        using var betaPage = await ReadAsync(betaAdmin, $"{route}?pageSize=100");
        Assert.Equal(3, betaPage.RootElement.GetProperty("totalItems").GetInt32());
        Assert.DoesNotContain("Paciente Pag A", betaPage.RootElement.GetRawText());
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode}");
        return await ReadJsonAsync(response);
    }

    private static async Task SeedAsync(HemodinksApiFactory factory, int clinicId, int count)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var label = clinicId == Clinica.DefaultId ? "A" : "B";
        for (var index = 0; index < count; index++)
        {
            var user = new User
            {
                ClinicaId = clinicId, Nome = $"Paciente Pag {label} {index}", Email = $"pag-{Guid.NewGuid():N}@teste.local",
                Telefone = "+5511955550000", Senha = "hash", PerfilId = Perfil.PacientesId, Ativo = true
            };
            var patient = new Paciente { ClinicaId = clinicId, User = user, NomePaciente = user.Nome };
            var attendance = new AtendimentoCirurgico { ClinicaId = clinicId, Paciente = patient, DataProcedimento = new DateTime(2026, 7, 1) };
            var billing = new Faturamento
            {
                ClinicaId = clinicId, AtendimentoCirurgico = attendance, Competencia = new DateTime(2026, 7, 1),
                ValorApresentado = 100m, ValorReconhecido = 100m
            };
            db.AddRange(patient, attendance, billing, new ContaReceber
            {
                ClinicaId = clinicId, Paciente = patient, Faturamento = billing, NumeroDocumento = $"PAG-{label}-{index}",
                Descricao = "Titulo paginado", Competencia = billing.Competencia, DataEmissao = billing.Competencia,
                DataVencimento = new DateTime(2030, 1, 1), ValorOriginal = 100m, ValorAjustado = 100m, SaldoAberto = 100m,
                Status = ContaReceberStatus.Aberto, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
            });
        }
        await db.SaveChangesAsync();
    }
}
