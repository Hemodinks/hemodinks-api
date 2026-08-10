using HemodinksAPI.Application.Features.Cbhpm.Queries;
using HemodinksAPI.Application.Features.Common;
using HemodinksAPI.Application.Features.Faturamentos.Queries;
using HemodinksAPI.Application.Features.GruposMedicos.Queries;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class FullTextSearchTermBuilderTests
{
    [Theory]
    [InlineData(" cirurgia   cardiaca ", "\"cirurgia*\" AND \"cardiaca*\"")]
    [InlineData("cirurg\" OR FORMSOF(INFLECTIONAL, ataque)", "\"cirurg*\" AND \"OR*\" AND \"FORMSOF*\" AND \"INFLECTIONAL*\" AND \"ataque*\"")]
    [InlineData("coração; válvula", "\"coração*\" AND \"válvula*\"")]
    [InlineData("cirurgia cirurgia", "\"cirurgia*\"")]
    public void BuildPrefixCondition_SanitizesAndCombinesTerms(string input, string expected)
    {
        Assert.Equal(expected, FullTextSearchTermBuilder.BuildPrefixCondition(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a !")]
    public void BuildPrefixCondition_ReturnsNullWhenNoSearchableTerm(string? input)
    {
        Assert.Null(FullTextSearchTermBuilder.BuildPrefixCondition(input));
    }
}

public class FullTextSearchFallbackTests
{
    [Theory]
    [InlineData("1.01.01.01-2")]
    [InlineData("10101012")]
    public async Task CbhpmSearch_PreservesFormattedAndNormalizedCode(string codigo)
    {
        await using var lease = TestDbContextFactory.CreateRelationalCbhpm();
        lease.Context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Consulta cardiologica",
            Grupo = "CONSULTAS",
            Porte = "2B"
        });
        await lease.Context.SaveChangesAsync();

        var handler = new GetCbhpmGeralQueryHandler(
            lease.AppContext,
            NullLogger<GetCbhpmGeralQueryHandler>.Instance);
        var result = await handler.Handle(new GetCbhpmGeralQuery { Search = codigo }, CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task PacienteSearch_FallbackCoversTextAndStructuredFieldsWithPagination()
    {
        await using var context = TestDbContextFactory.Create();
        var medico = CreateUser("Dra Celina", "celina.search@hemodinks.test", "11144477735");
        var auxiliar = CreateUser("Dr Auxilio", "auxilio.search@hemodinks.test", "52998224725");
        var pacienteUser = CreateUser(
            "Paciente Aurora",
            "aurora.search@hemodinks.test",
            "93541134780",
            Perfil.PacientesId,
            "+5511987654321");
        var paciente = new Paciente
        {
            User = pacienteUser,
            NomePaciente = "Paciente Aurora",
            Diagnostico = "Cardiopatia congenita",
            HospitalReferencia = new Hospital { Nome = "Hospital Esperanca" },
            Hospital = "Hospital Esperanca",
            MedicoUser = medico,
            Medico = medico.Nome,
            MedicoAuxiliar1User = auxiliar,
            MedicoAuxiliar1 = auxiliar.Nome,
            ConvenioReferencia = new Convenio { DescricaoConvenio = "Saude Premium" },
            Convenio = "Saude Premium",
            OpmeFornecedorReferencia = new Opme { Fornecedor = "Fornecedor Ortopedico" },
            OpmeFornecedor = "Fornecedor Ortopedico",
            Procedimento = "Cirurgia valvar",
            CbhpmCodigo = "3.01.01.10-0",
            CbhpmPorte = "4A",
            Procedimentos =
            [
                new PacienteProcedimento
                {
                    Procedimento = "Revascularizacao miocardica",
                    CbhpmCodigo = "4.02.01.01-0",
                    CbhpmPorte = "5B"
                }
            ]
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new GetAllPacientesQueryHandler(
            context,
            NullLogger<GetAllPacientesQueryHandler>.Instance);

        var searches = new[]
        {
            "Aurora", "Cardiopatia", "Esperanca", "Celina", "Auxilio", "Premium",
            "Ortopedico", "Cirurgia", "Revascularizacao", "aurora.search@hemodinks.test",
            "93541134780", "11987654321", "30101100", "40201010", "5B"
        };

        foreach (var search in searches)
        {
            var result = await handler.Handle(new GetAllPacientesQuery
            {
                Search = search,
                Page = 1,
                PageSize = 1,
                SortBy = "nome",
                SortDirection = "asc",
                CurrentPerfilId = Perfil.AdministradorId,
                CurrentUserId = medico.Id
            }, CancellationToken.None);

            Assert.Equal(1, result.TotalItems);
            Assert.Single(result.Items);
            Assert.Equal("Paciente Aurora", result.Items[0].NomePaciente);
        }
    }

    [Fact]
    public async Task FaturamentoSearch_FallbackCoversTextAndBillingIdentifiers()
    {
        await using var context = TestDbContextFactory.Create();
        var medico = CreateUser("Dra Helena", "helena.billing@hemodinks.test", "11144477735");
        var user = CreateUser("Paciente Billing", "billing.patient@hemodinks.test", "52998224725", Perfil.PacientesId);
        context.Pacientes.Add(new Paciente
        {
            User = user,
            NomePaciente = "Paciente Billing",
            Hospital = "Hospital Central",
            MedicoUser = medico,
            Medico = medico.Nome,
            Convenio = "Convenio Diamante",
            OpmeFornecedor = "Fornecedor Titanio",
            Procedimento = "Cirurgia vascular",
            CbhpmCodigo = "3.01.01.10-0",
            CbhpmPorte = "4A",
            Autorizacao = "AUT-7788",
            Procedimentos = [new PacienteProcedimento { Procedimento = "Angioplastia periferica", CbhpmCodigo = "4.02.01.01-0" }],
            FaturamentoMedico = new FaturamentoMedico
            {
                GuiaAutorizacaoConvenio = "GUIA-4455",
                CodigoTussCbhpmAmb = "TUSS-9988",
                GlosaStatus = "PENDENTE"
            }
        });
        await context.SaveChangesAsync();

        var handler = new GetAllFaturamentosMedicosQueryHandler(
            context,
            NullLogger<GetAllFaturamentosMedicosQueryHandler>.Instance);

        foreach (var search in new[]
        {
            "Billing", "Central", "Helena", "Diamante", "Titanio", "vascular", "Angioplastia",
            "billing.patient@hemodinks.test", "30101100", "AUT-7788", "GUIA-4455", "TUSS-9988", "PENDENTE"
        })
        {
            var result = await handler.Handle(new GetAllFaturamentosMedicosQuery
            {
                Search = search,
                CurrentPerfilId = Perfil.AdministradorId,
                CurrentUserId = medico.Id
            }, CancellationToken.None);

            Assert.Equal(1, result.TotalItems);
            Assert.Single(result.Items);
        }
    }

    [Fact]
    public async Task UserSearch_FallbackPreservesNameEmailPhoneAndCpf()
    {
        await using var context = TestDbContextFactory.Create();
        context.Users.Add(CreateUser(
            "Dra Beatriz Nogueira",
            "beatriz.search@hemodinks.test",
            "39053344705",
            telefone: "+553499887766"));
        await context.SaveChangesAsync();

        var handler = new GetAllUsersQueryHandler(context, NullLogger<GetAllUsersQueryHandler>.Instance);
        foreach (var search in new[] { "Beatriz", "beatriz.search@hemodinks.test", "3499887766", "39053344705" })
        {
            var result = await handler.Handle(new GetAllUsersQuery { Search = search }, CancellationToken.None);
            Assert.Single(result.Items);
        }
    }

    [Fact]
    public async Task GrupoMedicoSearch_FallbackCoversGroupMemberNameAndEmail()
    {
        await using var context = TestDbContextFactory.Create();
        var member = CreateUser("Dr Otavio Lima", "otavio.group@hemodinks.test", "39053344705");
        context.GruposMedicos.Add(new GrupoMedico
        {
            Nome = "Equipe Cardiovascular",
            Membros = [new GrupoMedicoUsuario { User = member }]
        });
        await context.SaveChangesAsync();

        var handler = new GetAllGruposMedicosQueryHandler(context);
        foreach (var search in new[] { "Cardiovascular", "Otavio", "otavio.group@hemodinks.test" })
        {
            var result = await handler.Handle(new GetAllGruposMedicosQuery { Search = search }, CancellationToken.None);
            Assert.Single(result.Items);
        }
    }

    private static User CreateUser(
        string nome,
        string email,
        string cpf,
        int perfilId = Perfil.MedicosId,
        string telefone = "+5511999999999")
    {
        return new User
        {
            Nome = nome,
            Email = email,
            Telefone = telefone,
            Cpf = cpf,
            Senha = "test-only-hash",
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = perfilId
        };
    }
}
