namespace HemodinksAPI.Domain.Services;

public static class SurgicalTeamRules
{
    public static void Validate(int? principal, int? firstAssistant, int? secondAssistant)
    {
        if (principal is <= 0 || firstAssistant is <= 0 || secondAssistant is <= 0)
            throw new InvalidOperationException("Selecione medicos validos.");
        if (!principal.HasValue && !firstAssistant.HasValue && !secondAssistant.HasValue)
            throw new InvalidOperationException("Selecione ao menos um medico para a equipe cirurgica.");
        if (firstAssistant.HasValue && firstAssistant == secondAssistant)
            throw new InvalidOperationException("Os medicos auxiliares devem ser diferentes.");
    }
}
