namespace HemodinksAPI.Domain.Models;

public enum TipoCirurgia { Eletiva = 0, Urgencia = 1 }

public static class TipoCirurgiaRules
{
    public static void Validate(TipoCirurgia? value)
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw new InvalidOperationException("Tipo de cirurgia invalido. Use Eletiva ou Urgencia.");
    }
}
