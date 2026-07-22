namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapMethods("/", ["GET", "HEAD"], HealthCheckAsync)
            .WithName("RootHealthCheck")
            .AllowAnonymous();

        app.MapMethods("/healthz", ["GET", "HEAD"], HealthCheckAsync)
            .WithName("HealthCheck")
            .AllowAnonymous();

        app.MapDashboardEndpoints();
        app.MapCbhpmEndpoints();
        app.MapHospitalEndpoints();
        app.MapConvenioEndpoints();
        app.MapOpmeEndpoints();
        app.MapGrupoMedicoEndpoints();
        app.MapConfiguracaoSistemaEndpoints();
        app.MapUserEndpoints();
        app.MapPacienteEndpoints();
        app.MapFaturamentoMedicoEndpoints();
        app.MapFinanceiroEndpoints();
        app.MapLicencaEndpoints();
        app.MapEventEndpoints();
        app.MapExportEndpoints();
        app.MapPublicClinicaEndpoints();
        app.MapSessionEndpoints();
        app.MapClinicaPlatformEndpoints();
    }
}
