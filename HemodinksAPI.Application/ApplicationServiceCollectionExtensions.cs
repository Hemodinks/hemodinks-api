using HemodinksAPI.Application.Features.Events.Commands;
using HemodinksAPI.Application.Features.Pacientes.Commands;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Application;

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
