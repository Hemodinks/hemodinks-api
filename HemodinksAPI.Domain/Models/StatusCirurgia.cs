namespace HemodinksAPI.Domain.Models;

// Derivado da data da consolidação (Paciente.DataAtendimento); nunca editado diretamente.
public enum StatusCirurgia { NaoConsolidada = 0, Consolidada = 1 }

public static class StatusCirurgiaRules
{
    public static StatusCirurgia FromDataConsolidacao(DateTime? dataConsolidacao) =>
        dataConsolidacao.HasValue ? StatusCirurgia.Consolidada : StatusCirurgia.NaoConsolidada;

    public static void Validate(StatusCirurgia? value)
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw new InvalidOperationException("Status da cirurgia invalido. Use Consolidada ou NaoConsolidada.");
    }
}
