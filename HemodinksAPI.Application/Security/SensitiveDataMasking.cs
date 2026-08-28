namespace HemodinksAPI.Application.Security;

public static class SensitiveDataMasking
{
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var normalized = email.Trim();
        var separator = normalized.IndexOf('@');
        if (separator <= 0 || separator == normalized.Length - 1)
        {
            return "***";
        }

        return $"{normalized[0]}***@{normalized[(separator + 1)..]}";
    }
}
