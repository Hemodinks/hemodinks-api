namespace HemodinksAPI.Domain.Models;

public enum TipoCirurgia { Eletiva = 0, Urgencia = 1, EmAutorizacao = 2 }

public static class TipoCirurgiaRules
{
    public static void Validate(TipoCirurgia? value)
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
            throw new InvalidOperationException("Tipo de cirurgia invalido. Use Eletiva, Urgencia ou EmAutorizacao.");
    }
}
