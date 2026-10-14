using System.Net;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    [Fact]
    public async Task Billing176_JsonContractDoesNotExposeOtherClinicsFinancialData()
    {
        using var factory = new HemodinksApiFactory();
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ClinicaContext>().SetPlatformScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Clinicas.Add(new Clinica { Id = 176, Nome = "TENANT-176", Slug = "tenant-176", Ativa = true });
            db.Users.Add(new User
            {
                Id = 17600, ClinicaId = 176, Nome = "ACTOR-176", Email = "actor176@example.test", Telefone = string.Empty,
                Senha = new PasswordHasher().HashPassword(TestPasswords.Valid), PerfilId = Perfil.AdministradorId,
                Ativo = true, PrecisaTrocarSenha = false
            });
            db.Pacientes.AddRange(Billing176Record(17601, 1), Billing176Record(17602, 176));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, "tenant-176", "actor176@example.test", TestPasswords.Valid);
        client.DefaultRequestHeaders.Remove("X-Clinica-Slug");
        client.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);

        using var response = await client.GetAsync("/api/faturamentos-medicos/?pageSize=100&clinicaId=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await ReadJsonAsync(response);
        var item = Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(17602, item.GetProperty("id").GetInt32());
        Assert.True(item.GetProperty("statusPago").GetBoolean());
        Assert.Equal(100.50m, item.GetProperty("faturamento").GetProperty("honorariosCirurgiao").GetDecimal());
        Assert.Equal(80.25m, item.GetProperty("faturamento").GetProperty("repasseMedico").GetDecimal());

        using var localDetail = await client.GetAsync("/api/pacientes/17602");
        Assert.Equal(HttpStatusCode.OK, localDetail.StatusCode);
        using var detail = await ReadJsonAsync(localDetail);
        Assert.True(detail.RootElement.GetProperty("statusPago").GetBoolean());
        Assert.Equal(item.GetProperty("faturamento").GetRawText(), detail.RootElement.GetProperty("faturamento").GetRawText());
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            detail.RootElement.GetProperty("faturamento").GetProperty("dataPagamento").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            detail.RootElement.GetProperty("faturamento").GetProperty("guiaInternacaoOuSadt").ValueKind);

        using var remoteDetail = await client.GetAsync("/api/pacientes/17601");
        using var nonexistent = await client.GetAsync("/api/pacientes/2147483647");
        Assert.Equal(HttpStatusCode.NotFound, remoteDetail.StatusCode);
        Assert.Equal(await nonexistent.Content.ReadAsStringAsync(), await remoteDetail.Content.ReadAsStringAsync());

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/faturamentos-medicos/")).StatusCode);
    }

    private static Paciente Billing176Record(int id, int clinic) => new()
    {
        Id = id, ClinicaId = clinic, NomePaciente = string.Empty, StatusPago = true,
        Pagamento = "80.25", RepasseGlosa = "20.25",
        User = new User
        {
            Id = id + 100, ClinicaId = clinic, Nome = string.Empty, Email = $"u{id}@example.test",
            Telefone = string.Empty, Senha = "unused-fixture-hash", PerfilId = Perfil.PacientesId, Ativo = true
        },
        FaturamentoMedico = new FaturamentoMedico
        {
            ClinicaId = clinic, HonorariosCirurgiao = 100.50m, RepasseMedico = 80.25m, ValorGlosa = 20.25m,
            ConferenciaPagamentoRealizada = true
        }
    };
}
