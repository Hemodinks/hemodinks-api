using Scalar.AspNetCore;

namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
    public static void UseApiDocumentation(this WebApplication app)
    {
        var documentationEnabled = app.Configuration.GetValue<bool?>("ApiDocumentation:Enabled")
            ?? (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"));

        if (!documentationEnabled)
        {
            return;
        }

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Hemodinks API v1");
            options.RoutePrefix = "swagger";
        });
        app.MapSwagger("/openapi/{documentName}.json").AllowAnonymous();
        app.MapScalarApiReference("/scalar", options =>
        {
            options
                .WithTitle("Hemodinks API - Documentacao Interativa")
                .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                .AddPreferredSecuritySchemes("Bearer")
                .DisableAgent();
        }).AllowAnonymous();
    }
}
