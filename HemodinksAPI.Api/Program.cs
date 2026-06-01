using System.Text;
using HemodinksAPI.Api.Authentication;
using HemodinksAPI.Api.Data;
using HemodinksAPI.Api.Features.Users.Commands;
using HemodinksAPI.Api.Features.Users.Queries;
using HemodinksAPI.Api.Seeders;
using HemodinksAPI.Api.Utils;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Enrichers;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

// Configurar Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/hemodinks-api-.txt", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithThreadId()
    .CreateLogger();

builder.Host.UseSerilog();

// Adicionar DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }));

// Configurar JWT
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() 
    ?? throw new InvalidOperationException("JwtSettings não configurado");
builder.Services.AddSingleton(jwtSettings);

// Adicionar serviços de autenticação JWT
var key = Encoding.ASCII.GetBytes(jwtSettings.SecretKey);
builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? new[]
    {
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:8080"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Adicionar serviços
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<UserSeeder>();

// Adicionar MediatR para CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Adicionar Swagger
builder.Services.AddSwaggerGen();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Aplicar migrações automaticamente
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Iniciando migração do banco de dados");
        dbContext.Database.Migrate();
        logger.LogInformation("Migração do banco de dados concluída com sucesso");

        // Verificar se há dados no banco
        if (!dbContext.Users.Any())
        {
            logger.LogInformation("Iniciando seed de dados");
            var seeder = scope.ServiceProvider.GetRequiredService<UserSeeder>();
            var users = seeder.GenerateUsers();
            dbContext.Users.AddRange(users);
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Seed de {Count} usuários concluído com sucesso", users.Count);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao processar migração ou seed do banco de dados");
        throw;
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// Mapear endpoints de usuários
var usersGroup = app.MapGroup("/api/users")
    .WithTags("Users");

// POST /api/users - Criar novo usuário
usersGroup.MapPost("/", async (CreateUserCommand command, IMediator mediator, ILogger<Program> logger) =>
{
    try
    {
        var result = await mediator.Send(command);
        return Results.Created($"/api/users/{result.Id}", result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao criar usuário");
        return Results.BadRequest(new { message = "Erro ao criar usuário", error = ex.Message });
    }
})
.WithName("CreateUser")
.WithSummary("Criar novo usuário")
.WithDescription("Cria um novo usuário no sistema");

// POST /api/users/authenticate - Autenticar usuário
usersGroup.MapPost("/authenticate", async (AuthenticateUserCommand command, IMediator mediator, ILogger<Program> logger) =>
{
    try
    {
        var result = await mediator.Send(command);
        return Results.Ok(result);
    }
    catch (UnauthorizedAccessException ex)
    {
        logger.LogWarning(ex, "Falha na autenticação");
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao autenticar usuário");
        return Results.BadRequest(new { message = "Erro ao autenticar usuário", error = ex.Message });
    }
})
.WithName("AuthenticateUser")
.WithSummary("Autenticar usuário")
.WithDescription("Autentica um usuário e retorna um token JWT");

// GET /api/users - Listar todos os usuários
usersGroup.MapGet("/", async (IMediator mediator, ILogger<Program> logger) =>
{
    try
    {
        var result = await mediator.Send(new GetAllUsersQuery());
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao buscar usuários");
        return Results.BadRequest(new { message = "Erro ao buscar usuários", error = ex.Message });
    }
})
.WithName("GetAllUsers")
.WithSummary("Listar todos os usuários")
.WithDescription("Retorna uma lista de todos os usuários cadastrados")
.RequireAuthorization();

// GET /api/users/{id} - Buscar usuário por ID
usersGroup.MapGet("/{id}", async (int id, IMediator mediator, ILogger<Program> logger) =>
{
    try
    {
        var result = await mediator.Send(new GetUserByIdQuery(id));
        return result == null ? Results.NotFound() : Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao buscar usuário por ID");
        return Results.BadRequest(new { message = "Erro ao buscar usuário", error = ex.Message });
    }
})
.WithName("GetUserById")
.WithSummary("Buscar usuário por ID")
.WithDescription("Retorna os dados de um usuário específico")
.RequireAuthorization();

// GET /api/users/email/{email} - Buscar usuário por email
usersGroup.MapGet("/email/{email}", async (string email, IMediator mediator, ILogger<Program> logger) =>
{
    try
    {
        var result = await mediator.Send(new GetUserByEmailQuery(email));
        return result == null ? Results.NotFound() : Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Erro ao buscar usuário por email");
        return Results.BadRequest(new { message = "Erro ao buscar usuário", error = ex.Message });
    }
})
.WithName("GetUserByEmail")
.WithSummary("Buscar usuário por email")
.WithDescription("Retorna os dados de um usuário pelo email")
.RequireAuthorization();

app.Run();

