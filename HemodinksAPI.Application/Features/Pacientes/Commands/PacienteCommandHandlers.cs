using HemodinksAPI.Application.Data;
using HemodinksAPI.Application.Features.Cbhpm;
using HemodinksAPI.Application.Features.Pacientes.Queries;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Application.Storage;
using HemodinksAPI.Application.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Application.Features.Pacientes.Commands;

public class CreatePacienteCommandHandler : IRequestHandler<CreatePacienteCommand, PacienteDto>
{
    private readonly IAppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<CreatePacienteCommandHandler> _logger;

    public CreatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IPasswordHasher passwordHasher,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<CreatePacienteCommandHandler> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
        _passwordHasher = passwordHasher;
        _profilePhotoStorage = profilePhotoStorage;
        _logger = logger;
    }

    public async Task<PacienteDto> Handle(CreatePacienteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (!PacienteCommandAccess.CanCreate(request.CurrentPerfilId))
            {
                throw new UnauthorizedAccessException("Sem permissao para criar paciente");
            }

            PacienteRules.ValidateNome(request.NomePaciente);
            var cpf = await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, null, cancellationToken);
            await PacienteRules.ValidateEmailAsync(_context, request.Email, null, cancellationToken);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, null, cancellationToken);
            var medico = await PacienteRules.ResolveMedicoAsync(
                _context,
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentUserName,
                request.MedicoUserId,
                request.Medico,
                cancellationToken);
            var hospital = await PacienteRules.ResolveHospitalAsync(
                _context,
                request.HospitalId,
                request.Hospital,
                cancellationToken);
            var convenio = await PacienteRules.ResolveConvenioAsync(
                _context,
                request.ConvenioId,
                request.Convenio,
                cancellationToken);
            var procedimentos = await PacienteRules.ResolveProcedimentosAsync(
                _cbhpmCache,
                request.Procedimentos,
                request.CbhpmCodigo,
                request.Procedimento,
                request.CbhpmPorte,
                cancellationToken);
            var procedimentoPrincipal = procedimentos.FirstOrDefault();

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
                HospitalId = hospital.Id,
                Hospital = hospital.Nome,
                MedicoUserId = medico.UserId,
                Medico = medico.Nome,
                ConvenioId = convenio?.Id,
                Convenio = convenio?.Descricao,
                CbhpmCodigo = procedimentoPrincipal?.Codigo,
                CbhpmPorte = procedimentoPrincipal?.Porte,
                Procedimento = procedimentoPrincipal?.Nome,
                Autorizacao = PacienteRules.TrimOptional(request.Autorizacao),
                Pagamento = PacienteRules.TrimOptional(request.Pagamento),
                RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa),
                StatusPago = request.StatusPago,
                Procedimentos = PacienteRules.ToPacienteProcedimentos(procedimentos)
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
    private readonly IAppDbContext _context;
    private readonly ICbhpmCache _cbhpmCache;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly ILogger<UpdatePacienteCommandHandler> _logger;

    public UpdatePacienteCommandHandler(
        IAppDbContext context,
        ICbhpmCache cbhpmCache,
        IProfilePhotoStorage profilePhotoStorage,
        ILogger<UpdatePacienteCommandHandler> logger)
    {
        _context = context;
        _cbhpmCache = cbhpmCache;
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
                .Include(p => p.Procedimentos)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            if (!PacienteCommandAccess.CanManage(request.CurrentPerfilId))
            {
                throw new UnauthorizedAccessException("Sem permissao para atualizar paciente");
            }

            var cpf = await PacienteRules.NormalizeAndValidateCpfAsync(_context, request.Cpf, paciente.UserId, cancellationToken);
            await PacienteRules.ValidateEmailAsync(_context, request.Email, paciente.UserId, cancellationToken);
            var fotoPerfil = await _profilePhotoStorage.SaveAsync(request.FotoPerfil, paciente.User.FotoPerfil, cancellationToken);
            var medico = await PacienteRules.ResolveMedicoAsync(
                _context,
                request.CurrentPerfilId,
                request.CurrentUserId,
                request.CurrentUserName,
                request.MedicoUserId,
                request.Medico,
                cancellationToken);
            var hospital = await PacienteRules.ResolveHospitalAsync(
                _context,
                request.HospitalId,
                request.Hospital,
                cancellationToken);
            var convenio = await PacienteRules.ResolveConvenioAsync(
                _context,
                request.ConvenioId,
                request.Convenio,
                cancellationToken);
            var procedimentos = await PacienteRules.ResolveProcedimentosAsync(
                _cbhpmCache,
                request.Procedimentos,
                request.CbhpmCodigo,
                request.Procedimento,
                request.CbhpmPorte,
                cancellationToken);
            var procedimentoPrincipal = procedimentos.FirstOrDefault();

            paciente.User.Nome = request.NomePaciente.Trim();
            paciente.User.Email = request.Email.Trim();
            paciente.User.Telefone = request.Telefone;
            paciente.User.Cpf = cpf;
            paciente.User.FotoPerfil = fotoPerfil;
            paciente.User.DataNascimento = request.DataNascimento;
            paciente.User.Ativo = request.Ativo;
            paciente.User.PerfilId = Perfil.PacientesId;
            paciente.User.DataAtualizacao = DateTime.UtcNow;

            paciente.Data = request.Data;
            paciente.NomePaciente = paciente.User.Nome;
            paciente.HospitalId = hospital.Id;
            paciente.Hospital = hospital.Nome;
            paciente.MedicoUserId = medico.UserId;
            paciente.Medico = medico.Nome;
            paciente.ConvenioId = convenio?.Id;
            paciente.Convenio = convenio?.Descricao;
            paciente.CbhpmCodigo = procedimentoPrincipal?.Codigo;
            paciente.CbhpmPorte = procedimentoPrincipal?.Porte;
            paciente.Procedimento = procedimentoPrincipal?.Nome;
            paciente.Autorizacao = PacienteRules.TrimOptional(request.Autorizacao);
            paciente.Pagamento = PacienteRules.TrimOptional(request.Pagamento);
            paciente.RepasseGlosa = PacienteRules.TrimOptional(request.RepasseGlosa);
            paciente.StatusPago = request.StatusPago;
            paciente.Procedimentos.Clear();
            foreach (var procedimentoItem in PacienteRules.ToPacienteProcedimentos(procedimentos))
            {
                paciente.Procedimentos.Add(procedimentoItem);
            }

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
    private readonly IAppDbContext _context;
    private readonly IProfilePhotoStorage _profilePhotoStorage;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteCommandHandler> _logger;

    public DeletePacienteCommandHandler(
        IAppDbContext context,
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
            if (request.CurrentPerfilId != Perfil.AdministradorId)
            {
                throw new UnauthorizedAccessException("Sem permissao para excluir paciente");
            }

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
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<UploadPacienteArquivoCommandHandler> _logger;

    public UploadPacienteArquivoCommandHandler(
        IAppDbContext context,
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
            var paciente = await _context.Pacientes
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == request.PacienteId, cancellationToken);

            if (paciente == null)
            {
                throw new KeyNotFoundException("Paciente nao encontrado");
            }

            if (!PacienteCommandAccess.CanManage(request.CurrentPerfilId))
            {
                throw new UnauthorizedAccessException("Sem permissao para enviar arquivo do paciente");
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
            paciente.User.DataAtualizacao = DateTime.UtcNow;
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
    private readonly IAppDbContext _context;
    private readonly IPatientFileStorage _patientFileStorage;
    private readonly ILogger<DeletePacienteArquivoCommandHandler> _logger;

    public DeletePacienteArquivoCommandHandler(
        IAppDbContext context,
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
                .Include(a => a.Paciente)
                .ThenInclude(p => p.User)
                .FirstOrDefaultAsync(a => a.Id == request.ArquivoId && a.PacienteId == request.PacienteId, cancellationToken);

            if (arquivo == null)
            {
                throw new KeyNotFoundException("Arquivo nao encontrado");
            }

            if (!PacienteCommandAccess.CanManage(request.CurrentPerfilId))
            {
                throw new UnauthorizedAccessException("Sem permissao para excluir arquivo do paciente");
            }

            var fileUrl = arquivo.Url;
            arquivo.Paciente.User.DataAtualizacao = DateTime.UtcNow;
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

internal static class PacienteCommandAccess
{
    public static bool CanCreate(int perfilId)
    {
        return perfilId == Perfil.AdministradorId;
    }

    public static bool CanManage(int perfilId)
    {
        return perfilId == Perfil.AdministradorId;
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
        IAppDbContext context,
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
        IAppDbContext context,
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

    public static async Task<ResolvedHospital> ResolveHospitalAsync(
        IAppDbContext context,
        int? hospitalId,
        string? hospitalNome,
        CancellationToken cancellationToken)
    {
        Hospital? hospital = null;

        if (hospitalId.HasValue)
        {
            hospital = await context.Hospitais
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == hospitalId.Value, cancellationToken);
        }
        else
        {
            var nome = TrimOptional(hospitalNome);
            if (nome != null)
            {
                hospital = await context.Hospitais
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Nome == nome, cancellationToken);
            }
        }

        if (hospital == null)
        {
            throw new InvalidOperationException("Hospital invalido");
        }

        return new ResolvedHospital(hospital.Id, hospital.Nome);
    }

    public static async Task<ResolvedConvenio?> ResolveConvenioAsync(
        IAppDbContext context,
        int? convenioId,
        string? convenioDescricao,
        CancellationToken cancellationToken)
    {
        Convenio? convenio = null;

        if (convenioId.HasValue)
        {
            convenio = await context.Convenios
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.IdConvenio == convenioId.Value, cancellationToken);
        }
        else
        {
            var descricao = TrimOptional(convenioDescricao);
            if (descricao == null)
            {
                return null;
            }

            convenio = await context.Convenios
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.DescricaoConvenio == descricao, cancellationToken);
        }

        if (convenio == null)
        {
            throw new InvalidOperationException("Convenio invalido");
        }

        return new ResolvedConvenio(convenio.IdConvenio, convenio.DescricaoConvenio);
    }

    public static async Task<ResolvedMedico> ResolveMedicoAsync(
        IAppDbContext context,
        int currentPerfilId,
        int currentUserId,
        string currentUserName,
        int? medicoUserId,
        string? medicoNome,
        CancellationToken cancellationToken)
    {
        if (currentPerfilId == Perfil.MedicosId)
        {
            return new ResolvedMedico(currentUserId, currentUserName);
        }

        var nome = TrimOptional(medicoNome);

        if (medicoUserId.HasValue)
        {
            var medico = await context.Users
                .AsNoTracking()
                .Where(user => user.Id == medicoUserId.Value && user.PerfilId == Perfil.MedicosId)
                .Select(user => new { user.Id, user.Nome })
                .FirstOrDefaultAsync(cancellationToken);

            if (medico == null)
            {
                throw new InvalidOperationException("Medico invalido");
            }

            return new ResolvedMedico(medico.Id, medico.Nome);
        }

        if (nome == null)
        {
            return new ResolvedMedico(null, null);
        }

        var medicoPorNome = await context.Users
            .AsNoTracking()
            .Where(user => user.Nome == nome && user.PerfilId == Perfil.MedicosId)
            .Select(user => new { user.Id, user.Nome })
            .FirstOrDefaultAsync(cancellationToken);

        if (medicoPorNome == null)
        {
            throw new InvalidOperationException("Medico invalido");
        }

        return new ResolvedMedico(medicoPorNome.Id, medicoPorNome.Nome);
    }

    public static async Task<List<ResolvedProcedimento>> ResolveProcedimentosAsync(
        ICbhpmCache cbhpmCache,
        IEnumerable<PacienteProcedimentoCommandDto>? procedimentos,
        string? cbhpmCodigo,
        string? procedimento,
        string? cbhpmPorte,
        CancellationToken cancellationToken)
    {
        var requestedItems = procedimentos?
            .Where(item => item != null)
            .ToList() ?? [];

        if (requestedItems.Count == 0)
        {
            requestedItems =
            [
                new PacienteProcedimentoCommandDto
                {
                    CbhpmCodigo = cbhpmCodigo,
                    CbhpmPorte = cbhpmPorte,
                    Procedimento = procedimento
                }
            ];
        }

        var resolvedItems = new List<ResolvedProcedimento>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in requestedItems)
        {
            var resolved = await ResolveProcedimentoItemAsync(cbhpmCache, item, cancellationToken);
            if (resolved == null)
            {
                continue;
            }

            var key = resolved.Codigo != null
                ? $"codigo:{resolved.Codigo}"
                : $"livre:{resolved.Nome}|{resolved.Porte}";

            if (seenKeys.Add(key))
            {
                resolvedItems.Add(resolved);
            }
        }

        return resolvedItems;
    }

    public static List<PacienteProcedimento> ToPacienteProcedimentos(IReadOnlyList<ResolvedProcedimento> procedimentos)
    {
        return procedimentos
            .Select((procedimento, index) => new PacienteProcedimento
            {
                CbhpmCodigo = procedimento.Codigo,
                CbhpmPorte = procedimento.Porte,
                Procedimento = procedimento.Nome,
                ValorReferencia = procedimento.ValorReferencia,
                Ordem = index + 1
            })
            .ToList();
    }

    private static async Task<ResolvedProcedimento?> ResolveProcedimentoItemAsync(
        ICbhpmCache cbhpmCache,
        PacienteProcedimentoCommandDto item,
        CancellationToken cancellationToken)
    {
        var codigo = TrimOptional(item.CbhpmCodigo);
        if (codigo == null)
        {
            var procedimento = TrimOptional(item.Procedimento);
            if (procedimento == null)
            {
                return null;
            }

            if (procedimento.Length > 1000)
            {
                throw new InvalidOperationException("Procedimento excede 1000 caracteres");
            }

            var porte = TrimOptional(item.CbhpmPorte);
            if (porte?.Length > 10)
            {
                throw new InvalidOperationException("Porte CBHPM invalido");
            }

            return new ResolvedProcedimento(null, procedimento, porte, item.ValorReferencia);
        }

        var cbhpm = await cbhpmCache.GetByCodigoAsync(codigo, cancellationToken);

        if (cbhpm == null)
        {
            throw new InvalidOperationException("Procedimento CBHPM nao encontrado");
        }

        return new ResolvedProcedimento(cbhpm.Codigo, cbhpm.Procedimento, cbhpm.Porte, cbhpm.ValorReferencia);
    }
}

internal sealed record ResolvedHospital(int Id, string Nome);

internal sealed record ResolvedConvenio(int Id, string Descricao);

internal sealed record ResolvedMedico(int? UserId, string? Nome);

internal sealed record ResolvedProcedimento(string? Codigo, string Nome, string? Porte, decimal? ValorReferencia);
