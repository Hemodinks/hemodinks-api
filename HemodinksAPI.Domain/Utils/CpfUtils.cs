namespace HemodinksAPI.Domain.Utils;

public static class CpfUtils
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new string(value.Where(char.IsDigit).ToArray());
    }

    public static bool IsValid(string? value)
    {
        var cpf = Normalize(value);

        if (cpf is null || cpf.Length != 11 || cpf.Distinct().Count() == 1)
        {
            return false;
        }

        return GetDigit(cpf, 9) == cpf[9] - '0'
            && GetDigit(cpf, 10) == cpf[10] - '0';
    }

    private static int GetDigit(string cpf, int length)
    {
        var sum = 0;

        for (var i = 0; i < length; i++)
        {
            sum += (cpf[i] - '0') * (length + 1 - i);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
