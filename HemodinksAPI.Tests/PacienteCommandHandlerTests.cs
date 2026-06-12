using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public class PacienteCommandHandlerTests
{
    [Fact]
    public async Task CreatePaciente_CreatesLinkedUserWithPacienteProfile()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.01.01-2",
            Procedimento = "Em consultorio",
            Porte = "2B",
            ValorReferencia = 120m
        });
        context.CbhpmGeral.Add(new CbhpmGeral
        {
            Codigo = "1.01.02.01-9",
            Procedimento = "Visita hospitalar a paciente internado",
            Porte = "2A",
            ValorReferencia = 180m
        });
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var hasher = new PasswordHasher();
        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            hasher,
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Novo",
            Email = "paciente.novo@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            ConvenioId = 7,
            Convenio = "Particular",
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "1.01.01.01-2" },
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "1.01.02.01-9" }
            ],
            Autorizacao = "AUT-123",
            Pagamento = "Pix",
            RepasseGlosa = "Sem glosa",
            StatusPago = true,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedUser = await context.Users.SingleAsync(user => user.PerfilId == Perfil.PacientesId);
        var storedPaciente = await context.Pacientes.SingleAsync();

        Assert.Equal(storedUser.Id, storedPaciente.UserId);
        Assert.Equal(Perfil.PacientesId, storedUser.PerfilId);
        Assert.Equal("Paciente Novo", storedUser.Nome);
        Assert.Equal("52998224725", storedUser.Cpf);
        Assert.True(hasher.VerifyPassword(DefaultUserPassword.Value, storedUser.Senha));
        Assert.Equal(1, storedPaciente.HospitalId);
        Assert.Equal("Santa Clara - Mater Dei", storedPaciente.Hospital);
        Assert.Equal(doctor.Id, storedPaciente.MedicoUserId);
        Assert.Equal(doctor.Nome, storedPaciente.Medico);
        Assert.Equal(7, storedPaciente.ConvenioId);
        Assert.Equal("Particular", storedPaciente.Convenio);
        Assert.Equal("1.01.01.01-2", storedPaciente.CbhpmCodigo);
        Assert.Equal("Em consultorio", storedPaciente.Procedimento);
        Assert.Equal("2B", storedPaciente.CbhpmPorte);
        Assert.True(storedPaciente.StatusPago);
        Assert.Equal(storedPaciente.Id, response.Id);
        Assert.Equal(storedUser.Id, response.UserId);
        Assert.Equal(7, response.ConvenioId);
        Assert.Equal("Particular", response.Convenio);
        Assert.Equal(["Em consultorio", "Visita hospitalar a paciente internado"], response.Procedimentos.Select(item => item.Procedimento));

        var storedProcedimentos = await context.PacienteProcedimentos
            .OrderBy(item => item.Ordem)
            .ToListAsync();
        Assert.Equal(2, storedProcedimentos.Count);
        Assert.Equal(storedPaciente.Id, storedProcedimentos[0].PacienteId);
        Assert.Equal("1.01.01.01-2", storedProcedimentos[0].CbhpmCodigo);
        Assert.Equal(120m, storedProcedimentos[0].ValorReferencia);
        Assert.Equal("1.01.02.01-9", storedProcedimentos[1].CbhpmCodigo);
        Assert.Equal(180m, storedProcedimentos[1].ValorReferencia);
    }

    [Fact]
    public async Task CreatePaciente_WhenCbhpmCodeDoesNotExist_StoresManualProcedure()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.manual@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Manual",
            Email = "paciente.manual@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = "9.99.99.99-9",
                    Procedimento = "Procedimento manual Hemodinks",
                    CbhpmPorte = "1A",
                    ValorReferencia = 250m
                }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedPaciente = await context.Pacientes
            .Include(paciente => paciente.Procedimentos)
            .SingleAsync();
        var storedProcedimento = Assert.Single(storedPaciente.Procedimentos);

        Assert.Equal("9.99.99.99-9", storedPaciente.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", storedPaciente.Procedimento);
        Assert.Equal("1A", storedPaciente.CbhpmPorte);
        Assert.Equal("9.99.99.99-9", response.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", response.Procedimento);
        Assert.Equal("9.99.99.99-9", storedProcedimento.CbhpmCodigo);
        Assert.Equal("Procedimento manual Hemodinks", storedProcedimento.Procedimento);
        Assert.Equal("1A", storedProcedimento.CbhpmPorte);
        Assert.Equal(250m, storedProcedimento.ValorReferencia);
        Assert.Empty(await context.CbhpmGeral.Where(item => item.Codigo == "9.99.99.99-9").ToListAsync());
    }

    [Fact]
    public async Task CreatePaciente_WhenCbhpmCodeDoesNotExistWithoutDescription_ThrowsInvalidOperationException()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.sem.descricao@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        context.Users.Add(doctor);
        await context.SaveChangesAsync();

        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new CreatePacienteCommand
        {
            NomePaciente = "Paciente Manual",
            Email = "paciente.manual.sem.descricao@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "52998224725",
            DataNascimento = new DateTime(1990, 1, 1),
            Data = new DateTime(2026, 6, 1),
            HospitalId = 1,
            MedicoUserId = doctor.Id,
            Medico = doctor.Nome,
            Procedimentos =
            [
                new PacienteProcedimentoCommandDto { CbhpmCodigo = "9.99.99.99-9" }
            ],
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None));

        Assert.Equal("Informe a descricao do procedimento para o codigo CBHPM nao cadastrado", exception.Message);
        Assert.Empty(await context.Pacientes.ToListAsync());
        Assert.Empty(await context.Users.Where(user => user.PerfilId == Perfil.PacientesId).ToListAsync());
    }

    [Fact]
    public async Task CreatePaciente_WhenLoggedDoctor_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var handler = new CreatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new PasswordHasher(),
            new FakeProfilePhotoStorage(),
            NullLogger<CreatePacienteCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new CreatePacienteCommand
        {
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserId = 10,
            CurrentUserName = "Dra. Ana"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UploadPacienteArquivo_WhenPacienteExists_StoresMetadata()
    {
        await using var context = TestDbContextFactory.Create();
        var user = new User
        {
            Nome = "Paciente Upload",
            Email = "paciente.upload@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UploadPacienteArquivoCommandHandler(
            context,
            new FakePatientFileStorage(),
            NullLogger<UploadPacienteArquivoCommandHandler>.Instance);

        var file = new FormFile(new MemoryStream("conteudo"u8.ToArray()), 0, 8, "file", "laudo.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var response = await handler.Handle(new UploadPacienteArquivoCommand
        {
            PacienteId = paciente.Id,
            File = file,
            CurrentPerfilId = Perfil.AdministradorId
        }, CancellationToken.None);

        var storedArquivo = await context.PacienteArquivos.SingleAsync();
        Assert.Equal(paciente.Id, storedArquivo.PacienteId);
        Assert.Equal("laudo.pdf", storedArquivo.NomeOriginal);
        Assert.Equal("https://storage.example/laudo.pdf", storedArquivo.Url);
        Assert.Equal(storedArquivo.Id, response.Id);
    }

    [Fact]
    public async Task UploadPacienteArquivo_WhenLoggedDoctorIsRelated_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var doctor = new User
        {
            Nome = "Dra. Ana",
            Email = "dra.ana.upload@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Upload Bloqueado",
            Email = "paciente.upload.bloqueado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctor.Nome
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UploadPacienteArquivoCommandHandler(
            context,
            new FakePatientFileStorage(),
            NullLogger<UploadPacienteArquivoCommandHandler>.Instance);

        var file = new FormFile(new MemoryStream("conteudo"u8.ToArray()), 0, 8, "file", "laudo.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UploadPacienteArquivoCommand
        {
            PacienteId = paciente.Id,
            File = file,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId
        }, CancellationToken.None));

        Assert.Empty(await context.PacienteArquivos.ToListAsync());
    }

    [Fact]
    public async Task UpdatePaciente_WhenLoggedUserIsPatient_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var user = new User
        {
            Nome = "Paciente Bloqueado",
            Email = "paciente.bloqueado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "39053344705",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            Medico = "Dra. Ana"
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Editado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento,
            Ativo = true,
            CurrentUserId = user.Id,
            CurrentPerfilId = Perfil.PacientesId,
            CurrentUserName = user.Nome
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdatePaciente_WhenAdministrator_UpdatesPaciente()
    {
        await using var context = TestDbContextFactory.Create();
        var doctorName = "Dra. Ana";
        var doctor = new User
        {
            Nome = doctorName,
            Email = "dra.ana@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Relacionado",
            Email = "paciente.relacionado@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctorName
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        var response = await handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Atualizado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento,
            Ativo = true,
            HospitalId = 2,
            MedicoUserId = doctor.Id,
            Medico = doctorName,
            CurrentUserId = 99,
            CurrentPerfilId = Perfil.AdministradorId,
            CurrentUserName = "Admin"
        }, CancellationToken.None);

        Assert.Equal("Paciente Atualizado", response.NomePaciente);
        Assert.Equal(2, response.HospitalId);
        Assert.Equal("Santa Genoveva - Mater Dei", response.Hospital);
        Assert.Equal(doctorName, response.Medico);
        var storedUser = await context.Users.SingleAsync(storedUser => storedUser.Id == user.Id);
        Assert.NotNull(storedUser.DataAtualizacao);
        Assert.Equal(storedUser.DataAtualizacao, response.DataAtualizacao);
    }

    [Fact]
    public async Task UpdatePaciente_WhenLoggedDoctorIsRelated_ThrowsUnauthorizedAccessException()
    {
        await using var context = TestDbContextFactory.Create();
        var doctorName = "Dra. Ana";
        var doctor = new User
        {
            Nome = doctorName,
            Email = "dra.ana.relacionada@hemodinks.com",
            Telefone = "+5581999887766",
            Cpf = "52998224725",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1985, 1, 1),
            PerfilId = Perfil.MedicosId
        };
        var user = new User
        {
            Nome = "Paciente Relacionado",
            Email = "paciente.relacionado.medico@hemodinks.com",
            Telefone = "+5581999999999",
            Cpf = "11144477735",
            Senha = new PasswordHasher().HashPassword(DefaultUserPassword.Value),
            DataNascimento = new DateTime(1990, 1, 1),
            PerfilId = Perfil.PacientesId
        };
        var paciente = new Paciente
        {
            User = user,
            NomePaciente = user.Nome,
            MedicoUser = doctor,
            Medico = doctorName
        };
        context.Pacientes.Add(paciente);
        await context.SaveChangesAsync();

        var handler = new UpdatePacienteCommandHandler(
            context,
            CreateCbhpmCache(context),
            new FakeProfilePhotoStorage(),
            NullLogger<UpdatePacienteCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new UpdatePacienteCommand
        {
            Id = paciente.Id,
            NomePaciente = "Paciente Atualizado",
            Email = user.Email,
            Telefone = user.Telefone,
            Cpf = user.Cpf!,
            DataNascimento = user.DataNascimento,
            Ativo = true,
            HospitalId = 2,
            CurrentUserId = doctor.Id,
            CurrentPerfilId = Perfil.MedicosId,
            CurrentUserName = doctorName
        }, CancellationToken.None));
    }

    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        public Task<string?> SaveAsync(string? fotoPerfil, string? currentFotoPerfil, CancellationToken cancellationToken)
        {
            return Task.FromResult(fotoPerfil);
        }

        public Task<ProfilePhotoFile?> GetAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.FromResult<ProfilePhotoFile?>(null);
        }

        public Task DeleteAsync(string? fotoPerfil, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private static ICbhpmCache CreateCbhpmCache(AppDbContext context)
    {
        return new CbhpmCache(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CbhpmCache>.Instance);
    }

    private sealed class FakePatientFileStorage : IPatientFileStorage
    {
        public Task<StoredPatientFile> SaveAsync(IFormFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult(new StoredPatientFile(
                file.FileName,
                file.ContentType,
                file.Length,
                $"https://storage.example/{file.FileName}"));
        }

        public Task DeleteAsync(string? fileUrl, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
