using HemodinksAPI.Domain.Exceptions;

namespace HemodinksAPI.Domain.Services;

public static class SurgicalTeamRules
{
    public static void Validate(int? principal, int? firstAssistant, int? secondAssistant)
    {
        if (principal is <= 0 || firstAssistant is <= 0 || secondAssistant is <= 0)
            throw new BusinessRuleException("Selecione medicos validos.");
        if (!principal.HasValue && !firstAssistant.HasValue && !secondAssistant.HasValue)
            throw new BusinessRuleException("Selecione ao menos um medico para a equipe cirurgica.");
        if (firstAssistant.HasValue && firstAssistant == secondAssistant)
            throw new BusinessRuleException("Os medicos auxiliares devem ser diferentes.");
    }
}
