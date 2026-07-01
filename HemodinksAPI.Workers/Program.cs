using HemodinksAPI.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.Configure<EmailOptions>(context.Configuration.GetSection("Email"));
        services.Configure<FrontendOptions>(context.Configuration.GetSection("Frontend"));
        services.AddSingleton<SmtpPasswordResetNotificationSender>();
    })
    .Build();

host.Run();
