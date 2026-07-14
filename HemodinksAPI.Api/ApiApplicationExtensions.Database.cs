namespace HemodinksAPI.Api;

public static partial class ApiApplicationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await DatabaseStartupInitializer.InitializeAsync(app);
    }
}
