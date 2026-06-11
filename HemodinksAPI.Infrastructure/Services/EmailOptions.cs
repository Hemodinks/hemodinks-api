namespace HemodinksAPI.Infrastructure.Services;

public class EmailOptions
{
    public string? Provider { get; set; }

    public SmtpEmailOptions Smtp { get; set; } = new();

    public string? FromEmail { get; set; }

    public string? FromName { get; set; }
}

public class SmtpEmailOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool EnableSsl { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 10;
}

public class FrontendOptions
{
    public string? ResetPasswordUrl { get; set; }
}
