using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Pacientes.Queries;
using HemodinksAPI.Api.Models;
using HemodinksAPI.Api.Storage;
using HemodinksAPI.Api.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Api.Features.Pacientes.Commands;

public class CreatePacienteCommandHandler : IRequestHandler<CreatePacienteCommand, PacienteDto>
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<CreatePacienteCommandHandler> _logger;

    public CreatePacienteCommandHandler(
        AppDbContext context,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<CreatePacienteCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _logger = logger;
    }

    public async Task<PacienteDto> Handle(CreatePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            PacienteRules.ValidateNome(request.NomePaciente);
            var cpf = await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, null, cancellationToken);
            await PacienteRules.ValidateEmailAsync(_context, request.Email, null, cancellationToken);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);

            var user = new User
            {
                Nome = request.NomePaciente.Trim(),
                Email = request.Email.Trim(),
                Telefone = request.Telefone,
                Cpf = cpf,
                FotoPerfil = fotoPerfil,
                Senha = _passwordHasher.HashPassword(DefaultUserPassword.Value),
                DataCadastro = DateTime.UtcNow,
                DataNascimento = request.DataNascimento,
                Ativo = request.Ativo,
                PrecisaTrocarSenha = true,
                PerfilId = Perfil.PacientesId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            var paciente = new Paciente
            {
                UserId = user.Id,
                User = user,
                Data = request.Data,
                NomePaciente = user.Nome,
                Hospital = PacienteRules.TrimOptional(request.Hospital),
                Medico = PacienteRules.TrimOptional(request.Medico),
                Convenio = PacienteRules.TrimOptional(request.Convenio),
                Procedimento = PacienteRules.TrimOptional(request.Procedimento),
                Autorizacao = PacienteRules.TrimOptional(request.Autorizacao),
                Pagamento = PacienteRules.TrimOptional(request.Pagamento),
                RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa),
                StatusPago = request.StatusPago
            };

            _context.Pacientes.Add(paciente);
            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToDto(paciente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar paciente: {NomePaciente}", request.NomePaciente);
            throw;
        }
    }
}

public class UpdatePacienteCommandHandler : IRequestHandler<UpdatePacienteCommand, PacienteDto>
{
    private readonly AppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<UpdatePacienteCommandHandler> _logger;

    public UpdatePacienteCommandHandler(
        AppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<UpdatePacienteCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _logger = logger;
    }

    public async Task<PacienteDto> Handle(UpdatePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            PacienteRules.ValidateNome(request.NomePaciente);

            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            var cpf = await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, paciente.UserId, cancellationToken);
            await PacienteRules.ValidateEmailAsync(_context, request.Email, paciente.UserId, cancellationToken);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, paciente.User.FotoPerfil, cancellationToken);

            paciente.User.Nome = request.NomePaciente.Trim();
            paciente.User.Email = request.Email.Trim();
            paciente.User.Telefone = request.Telefone;
            paciente.User.Cpf = cpf;
            paciente.User.FotoPerfil = fotoPerfil;
            paciente.User.DataNascimento = request.DataNascimento;
            paciente.User.Ativo = request.Ativo;
            paciente.User.PerfilId = Perfil.PacientesId;

            paciente.Data = request.Data;
            paciente.NomePaciente = paciente.User.Nome;
            paciente.Hospital = PacienteRules.TrimOptional(request.Hospital);
            paciente.Medico = PacienteRules.TrimOptional(request.Medico);
            paciente.Convenio = PacienteRules.TrimOptional(request.Convenio);
            paciente.Procedimento = PacienteRules.TrimOptional(request.Procedimento);
            paciente.Autorizacao = PacienteRules.TrimOptional(request.Autorizacao);
            paciente.Pagamento = PacienteRules.TrimOptional(request.Pagamento);
            paciente.RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa);
            paciente.StatusPago = request.StatusPago;

            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToDto(paciente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}

public class DeletePacienteCommandHandler : IRequestHandler<DeletePacienteCommand>
{
    private readonly AppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteCommandHandler> _logger;

    public DeletePacienteCommandHandler(
        AppDbContext context,
        IProfilePhotoStorage profilePhotoStorage,
        IPatientFileStorage patientFileStorage,
        ILogger<DeletePacienteCommandHandler> logger)
    {
        _context = context;
        _profilePhotoStorage = profilePhotoStorage;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeletePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .Include(p => p.Arquivos)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            var fotoPerfil = paciente.User.FotoPerfil;
            var fileUrls = paciente.Arquivos.Select(arquivo => arquivo.Url).ToList();

            _context.Users.Remove(paciente.User);
            await _context.SaveChangesAsync(cancellationToken);

            await _profilePhotoStorage.DeleteAsync(fotoPerfil, cancellationToken);

            foreach (var fileUrl in fileUrls)
            {
                await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir paciente: {PacienteId}", request.Id);
            throw;
        }
    }
}

public class UploadPacienteArquivoCommandHandler : IRequestHandler<UploadPacienteArquivoCommand, PacienteArquivoDto>
{
    private readonly AppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<UploadPacienteArquivoCommandHandler> _logger;

    public UploadPacienteArquivoCommandHandler(
        AppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<UploadPacienteArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task<PacienteArquivoDto> Handle(UploadPacienteArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var pacienteExists = await _context.Pacientes
                .AnyAsync(p => p.Id == request.PacienteId, cancellationToken);

            if (!pacienteExists)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            var storedFile = await _patientFileStorage.SaveAsync(request.File, cancellationToken);
            var arquivo = new PacienteArquivo
            {
                PacienteId = request.PacienteId,
                NomeOriginal = storedFile.OriginalName,
                ContentType = storedFile.ContentType,
                TamanhoBytes = storedFile.SizeBytes,
                Url = storedFile.Url,
                DataUpload = DateTime.UtcNow
            };

            _context.PacienteArquivos.Add(arquivo);
            await _context.SaveChangesAsync(cancellationToken);

            return PacienteMapper.ToArquivoDto(arquivo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao enviar arquivo do paciente: {PacienteId}", request.PacienteId);
            throw;
        }
    }
}

public class DeletePacienteArquivoCommandHandler : IRequestHandler<DeletePacienteArquivoCommand>
{
    private readonly AppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteArquivoCommandHandler> _logger;

    public DeletePacienteArquivoCommandHandler(
        AppDbContext context,
        IPatientFileStorage patientFileStorage,
        ILogger<DeletePacienteArquivoCommandHandler> logger)
    {
        _context = context;
        _patientFileStorage = patientFileStorage;
        _logger = logger;
    }

    public async Task Handle(DeletePacienteArquivoCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var arquivo = await _context.PacienteArquivos
                .FirstOrDefaultAsync(a => a.Id == request.ArquivoId && a.PacienteId == request.PacienteId, cancellationToken);

            if (arquivo == null)
            {
                throw new KeyNotFoundException("Arquivo nao encontrado");
            }

            var fileUrl = arquivo.Url;
            _context.PacienteArquivos.Remove(arquivo);
            await _context.SaveChangesAsync(cancellationToken);
            await _patientFileStorage.DeleteAsync(fileUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao excluir arquivo {ArquivoId} do paciente {PacienteId}", request.ArquivoId, request.PacienteId);
            throw;
        }
    }
}

internal static class PacienteRules
{
    public static void ValidateNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new InvalidOperationException("Nome do paciente obrigatorio");
        }
    }

    public static async Task<string> NormalizeAndValidateCpfAsync(
        AppDbContext context,
        string? cpf,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (!CpfUtils.IsValid(cpf))
        {
            throw new InvalidOperationException("CPF invalido");
        }

        var normalizedCpf = CpfUtils.Normalize(cpf)!;
        var cpfAlreadyExists = await context.Users
            .AnyAsync(u => u.Cpf == normalizedCpf && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (cpfAlreadyExists)
        {
            throw new InvalidOperationException("CPF ja cadastrado");
        }

        return normalizedCpf;
    }

    public static async Task ValidateEmailAsync(
        AppDbContext context,
        string email,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email obrigatorio");
        }

        var trimmedEmail = email.Trim();
        var emailAlreadyExists = await context.Users
            .AnyAsync(u => u.Email == trimmedEmail && (!currentUserId.HasValue || u.Id != currentUserId.Value), cancellationToken);

        if (emailAlreadyExists)
        {
            throw new InvalidOperationException("Email ja cadastrado");
        }
    }

    public static string? TrimOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
