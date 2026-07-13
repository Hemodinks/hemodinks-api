using System.Linq.Expressions;
using HemodinksAPI.Domain.Models;

namespace HemodinksAPI.Application.Features.Pacientes.Observacoes;

internal static class PacienteObservacaoMapper
{
    public static Observacao ToEntity(
        CreatePacienteObservacaoCommand request,
        PacienteObservacaoContext paciente,
        int destinatarioId)
    {
        return new Observacao
        {
            ClinicaId = paciente.ClinicaId,
            PacienteId = paciente.Id,
            AutorUserId = request.CurrentUserId,
            DestinatarioUserId = destinatarioId,
            ObservacaoPaiId = request.ObservacaoPaiId,
            Texto = request.Texto.Trim(),
            MedicoUserId = paciente.MedicoUserId,
            Medico = paciente.Medico,
            MedicoAuxiliar1UserId = paciente.MedicoAuxiliar1UserId,
            MedicoAuxiliar1 = paciente.MedicoAuxiliar1,
            MedicoAuxiliar2UserId = paciente.MedicoAuxiliar2UserId,
            MedicoAuxiliar2 = paciente.MedicoAuxiliar2
        };
    }

    public static Expression<Func<Observacao, PacienteObservacaoDto>> ToDtoProjection(int currentUserId)
    {
        return observacao => new PacienteObservacaoDto
        {
            Id = observacao.Id,
            PacienteId = observacao.PacienteId,
            ObservacaoPaiId = observacao.ObservacaoPaiId,
            Texto = observacao.Texto,
            DataCadastro = observacao.DataCadastro,
            DataLeitura = observacao.DataLeitura,
            AutorUserId = observacao.AutorUserId,
            AutorNome = observacao.AutorUser.Nome,
            AutorPerfilId = observacao.AutorUser.PerfilId,
            AutorPerfilNome = observacao.AutorUser.Perfil.Nome,
            DestinatarioUserId = observacao.DestinatarioUserId,
            DestinatarioNome = observacao.DestinatarioUser.Nome,
            DestinatarioPerfilId = observacao.DestinatarioUser.PerfilId,
            DestinatarioPerfilNome = observacao.DestinatarioUser.Perfil.Nome,
            NomePaciente = observacao.Paciente.NomePaciente,
            MedicoUserId = observacao.MedicoUserId,
            Medico = observacao.Medico,
            MedicoAuxiliar1UserId = observacao.MedicoAuxiliar1UserId,
            MedicoAuxiliar1 = observacao.MedicoAuxiliar1,
            MedicoAuxiliar2UserId = observacao.MedicoAuxiliar2UserId,
            MedicoAuxiliar2 = observacao.MedicoAuxiliar2,
            FoiLida = observacao.DataLeitura.HasValue,
            EnviadaPorMim = observacao.AutorUserId == currentUserId
        };
    }
}
