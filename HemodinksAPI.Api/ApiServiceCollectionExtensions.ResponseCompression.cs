using System.IO.Compression;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.ResponseCompression;

namespace HemodinksAPI.Api;

public static partial class ApiServiceCollectionExtensions
{
    // Rotas cujas respostas carregam segredo (token de sessão, senha temporária, PIN temporário)
    // junto de dado ecoado da requisição: comprimir sob HTTPS abre espaço para BREACH (#223).
    private static readonly string[] CompressionExcludedPrefixes =
    [
        "/api/session",
        "/api/equipe-auth",
        "/api/users/authenticate",
        "/api/users/login-context"
    ];

    // Segmentos que, em qualquer rota, indicam fluxo de senha/PIN ou associação que devolve PIN temporário.
    private static readonly HashSet<string> CompressionExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pin",
        "membros"
    };

    public static IServiceCollection AddApiResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            // TLS termina no ingress; após ForwardedHeaders a requisição é HTTPS. Rotas sensíveis
            // são excluídas por requisição em UseApiResponseCompression.
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            // Padrão do ASP.NET (JSON/texto) + problem details. PDF, imagens e planilhas já vêm comprimidos.
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }

    public static IApplicationBuilder UseApiResponseCompression(this IApplicationBuilder app)
    {
        // Antes da compressão e independente do scheme: IHttpsCompressionFeature só vale quando
        // Request.IsHttps, o que falha se o proxy não for reconhecido pelos forwarded headers.
        app.Use((context, next) =>
        {
            if (IsCompressionExcluded(context.Request.Path))
                context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

            return next();
        });
        app.UseResponseCompression();
        return app;
    }

    internal static bool IsCompressionExcluded(PathString path)
    {
        foreach (var prefix in CompressionExcludedPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CompressionExcludedSegments.Contains(segment))
                return true;
        }

        return false;
    }
}
