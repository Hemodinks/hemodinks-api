using HemodinksAPI.Api.Features.Events.Commands;
using HemodinksAPI.Api.Features.Pacientes.Commands;
using HemodinksAPI.Api.Features.Users.Commands;
using HemodinksAPI.Api.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Api;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceCollectionExtensions).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddTransient<IRequestValidator<CreateEventCommand>, CreateEventCommandValidator>();
        services.AddTransient<IRequestValidator<UpdateEventCommand>, UpdateEventCommandValidator>();
        services.AddTransient<IRequestValidator<CreatePacienteCommand>, CreatePacienteCommandValidator>();
        services.AddTransient<IRequestValidator<UpdatePacienteCommand>, UpdatePacienteCommandValidator>();
        services.AddTransient<IRequestValidator<CreateUserCommand>, CreateUserCommandValidator>();
        services.AddTransient<IRequestValidator<UpdateUserCommand>, UpdateUserCommandValidator>();
        services.AddTransient<IRequestValidator<ChangePasswordCommand>, ChangePasswordCommandValidator>();

        return services;
    }
}
