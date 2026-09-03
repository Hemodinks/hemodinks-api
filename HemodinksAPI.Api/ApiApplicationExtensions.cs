namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapMethods("/", ["GET", "HEAD"], ReadinessCheckAsync)
            .WithName("RootHealthCheck")
            .AllowAnonymous();

        app.MapMethods("/healthz", ["GET", "HEAD"], ReadinessCheckAsync)
            .WithName("HealthCheck")
            .AllowAnonymous();

        app.MapMethods("/readyz", ["GET", "HEAD"], ReadinessCheckAsync)
            .WithName("ReadinessCheck")
            .AllowAnonymous();

        app.MapMethods("/livez", ["GET", "HEAD"], LivenessCheckAsync)
            .WithName("LivenessCheck")
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
        app.MapLegalAcceptanceEndpoints();
        app.MapPrivacyPreferenceEndpoints();
        app.MapClinicaPlatformEndpoints();
        app.MapEquipeEndpoints();
        app.MapMonitoringEndpoints();
    }
}
