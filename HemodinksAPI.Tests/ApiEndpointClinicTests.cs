using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using HemodinksAPI.Api;
using HemodinksAPI.Application.Authentication;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Domain.Utils;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public partial class ApiEndpointIntegrationTests
{
    private const string ValidCnpj = "11.222.333/0001-81";

    [Fact]
    public async Task PublicClinics_ReturnsActiveClinicsFromDatabase()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Clinicas.AddRange(
                new Clinica
                {
                    Nome = "Clinica Ativa do Banco",
                    Slug = $"ativa-{Guid.NewGuid():N}",
                    Ativa = true
                },
                new Clinica
                {
                    Nome = "Clinica Inativa do Banco",
                    Slug = $"inativa-{Guid.NewGuid():N}",
                    Ativa = false
                });
            await context.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/public/clinicas");

        response.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(response);
        Assert.Contains(
            json.RootElement.EnumerateArray(),
            item => item.GetProperty("nome").GetString() == "Clinica Ativa do Banco");
        Assert.DoesNotContain(
            json.RootElement.EnumerateArray(),
            item => item.GetProperty("nome").GetString() == "Clinica Inativa do Banco");
    }

    [Fact]
    public async Task AvailableProfiles_WhenCurrentUserIsSuperAdministrador_IncludesSuperAdministrador()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var response = await client.GetAsync("/api/users/perfis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var profileIds = json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
        Assert.Contains(Perfil.SuperAdministradorId, profileIds);
    }

    [Fact]
    public async Task AvailableProfiles_WhenCurrentUserIsAdministrador_HidesSuperAdministrador()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var response = await client.GetAsync("/api/users/perfis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var profileIds = json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
        Assert.DoesNotContain(Perfil.SuperAdministradorId, profileIds);
    }

    [Fact]
    public async Task AuthenticateUser_WhenMultipleClinicasAndNoHintIsProvided_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await SeedClinicaBetaAsync(factory);

        var response = await client.PostAsJsonAsync("/api/users/authenticate", new
        {
            Email = "gmarcone@gmail.com",
            Senha = TestPasswords.Valid
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(
            "Clinica nao resolvida. Envie X-Clinica-Slug ou use um subdominio configurado.",
            json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AuthenticateUser_WhenClinicHeaderTargetsDuplicateEmail_ReturnsClinicScopedPayload()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        var response = await PostAsJsonWithClinicHeaderAsync(client, beta.Slug, "/api/users/authenticate", new
        {
            Email = beta.AdminEmail,
            Senha = beta.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        Assert.Equal(beta.AdminEmail, json.RootElement.GetProperty("email").GetString());
        Assert.Equal(beta.AdminName, json.RootElement.GetProperty("nome").GetString());
        Assert.Equal(beta.Id, json.RootElement.GetProperty("clinicaId").GetInt32());
        Assert.Equal(beta.Slug, json.RootElement.GetProperty("clinicaSlug").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task AuthenticatedSession_WhenClinicHeaderDiverges_KeepsTokenClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/");
        request.Headers.Add(ClinicaResolutionService.ClinicaSlugHeaderName, beta.Slug);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var names = json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("nome").GetString())
            .ToList();
        Assert.Contains("George Marcone Morais dos Santos", names);
        Assert.DoesNotContain(beta.AdminName, names);
    }

    [Fact]
    public async Task SelectClinic_WhenAdministratorDoesNotBelongToClinic_ReturnsForbiddenAndAuditsDenial()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        var administratorEmail = $"admin-isolado-{Guid.NewGuid():N}@example.com";
        var administratorPassword = TemporaryPasswordGenerator.Generate();
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedScope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>()
                .SetPlatformScope();
            var administrator = new User
            {
                ClinicaId = beta.Id,
                Nome = "Administrador Isolado",
                Email = administratorEmail,
                Telefone = "+5511990000099",
                Senha = new PasswordHasher().HashPassword(administratorPassword),
                Ativo = true,
                PrecisaTrocarSenha = false,
                PerfilId = Perfil.AdministradorId
            };
            seedContext.Users.Add(administrator);
            await seedContext.SaveChangesAsync();
            await GlobalIdentityService.EnsureForUserAsync(seedContext, administrator, CancellationToken.None);
        }
        await AuthenticateAsync(client, beta.Slug, administratorEmail, administratorPassword);

        var response = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new
        {
            clinicaId = Clinica.DefaultId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "session.clinic.switch.denied"
            && item.EntidadeId == Clinica.DefaultId.ToString()
            && !item.Sucesso));
    }

    [Fact]
    public async Task SelectClinic_WhenSuperAdministratorHasNoMembership_ProvisionsAccessAndSwitchesClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var response = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new
        {
            clinicaId = beta.Id
        });

        response.EnsureSuccessStatusCode();
        using var json = await ReadJsonAsync(response);
        Assert.Equal(beta.Id, json.RootElement.GetProperty("clinica").GetProperty("clinicaId").GetInt32());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        Assert.True(await context.UsuariosClinicas.AnyAsync(item =>
            item.ClinicaId == beta.Id
            && item.PerfilId == Perfil.SuperAdministradorId
            && item.Ativo
            && item.User.Ativo));
    }

    [Fact]
    public async Task UsersEndpoint_WhenAuthenticatedInSecondClinic_ReturnsOnlyItsOwnUsers()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var response = await client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await ReadJsonAsync(response);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var names = items
            .Select(item => item.GetProperty("nome").GetString())
            .Where(value => value != null)
            .Cast<string>()
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal([beta.DoctorName, beta.AdminName], names);
        Assert.DoesNotContain("George Marcone Morais dos Santos", names);
    }

    [Fact]
    public async Task AgendaEndpoint_WhenSameIdempotencyKeyIsUsedAcrossClinicas_CreatesOneEventPerClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        await AuthenticateAsync(clientA, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);
        await AuthenticateAsync(clientB, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var key = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow.AddDays(3);
        var payload = new
        {
            title = "Evento multi-clinica",
            description = "Mesmo idempotency key em clinicas diferentes",
            start,
            end = start.AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = true,
            reminderPeriodMinutes = 20
        };

        var responseA = await PostAsJsonWithIdempotencyKeyAsync(clientA, "/api/events/", key, payload);
        var responseB = await PostAsJsonWithIdempotencyKeyAsync(clientB, "/api/events/", key, payload);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
        Assert.Equal("stored", responseA.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
        Assert.Equal("stored", responseB.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
        Assert.Equal(2, dbContext.Events.Count(item => item.Title == "Evento multi-clinica"));
        Assert.Equal(2, dbContext.IdempotencyRequests.Count(item => item.Operation == "events.create"));
    }

    [Fact]
    public async Task PlatformClinics_ValidatesNormalizesAndReturnsCnpjWithoutBreakingLegacyClinics()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var legacyResponse = await client.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}");
        legacyResponse.EnsureSuccessStatusCode();
        using (var legacyJson = await ReadJsonAsync(legacyResponse))
        {
            Assert.Equal(JsonValueKind.Null, legacyJson.RootElement.GetProperty("cnpj").ValueKind);
        }

        var missingResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica sem CNPJ",
            slug = $"clinica-{Guid.NewGuid():N}",
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);

        var invalidResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica com CNPJ invalido",
            slug = $"clinica-{Guid.NewGuid():N}",
            cnpj = "11.111.111/1111-11",
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var slug = $"clinica-cnpj-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica com CNPJ",
            slug,
            cnpj = ValidCnpj,
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createJson = await ReadJsonAsync(createResponse);
        var clinicId = createJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal("11222333000181", createJson.RootElement.GetProperty("cnpj").GetString());

        var duplicateCreateResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica com CNPJ repetido",
            slug = $"clinica-cnpj-repetido-{Guid.NewGuid():N}",
            cnpj = "11222333000181",
            administradorNome = "Administradora Duplicada",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreateResponse.StatusCode);
        using (var duplicateCreateJson = await ReadJsonAsync(duplicateCreateResponse))
        {
            Assert.Contains("CNPJ", duplicateCreateJson.RootElement.GetProperty("message").GetString());
        }

        var invalidUpdateResponse = await client.PutAsJsonAsync($"/api/platform/clinicas/{clinicId}", new
        {
            cnpj = "12.345.678/0001-00"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync($"/api/platform/clinicas/{clinicId}", new
        {
            cnpj = "04.252.011/0001-10"
        });
        updateResponse.EnsureSuccessStatusCode();
        using var updateJson = await ReadJsonAsync(updateResponse);
        Assert.Equal("04252011000110", updateJson.RootElement.GetProperty("cnpj").GetString());

        var secondCreateResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Segunda clinica com CNPJ",
            slug = $"segunda-clinica-cnpj-{Guid.NewGuid():N}",
            cnpj = ValidCnpj,
            administradorNome = "Segunda Administradora",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123"
        });
        Assert.Equal(HttpStatusCode.Created, secondCreateResponse.StatusCode);
        using var secondCreateJson = await ReadJsonAsync(secondCreateResponse);
        var secondClinicId = secondCreateJson.RootElement.GetProperty("id").GetInt32();

        var duplicateUpdateResponse = await client.PutAsJsonAsync($"/api/platform/clinicas/{secondClinicId}", new
        {
            cnpj = "04.252.011/0001-10"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateUpdateResponse.StatusCode);
        using var duplicateUpdateJson = await ReadJsonAsync(duplicateUpdateResponse);
        Assert.Contains("CNPJ", duplicateUpdateJson.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SuperAdministrador_CanProvisionListAndNavigateNewClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var slug = $"clinica-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Provisionada",
            slug,
            cnpj = ValidCnpj,
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123",
            fotoClinica = "data:image/png;base64,Zm90by1kYS1jbGluaWNh",
            plano = "Completa",
            assinaturaStatus = "Ativa",
            limiteUsuarios = 25,
            equipeInicial = new
            {
                nome = "Equipe Inicial",
                email = $"equipe-{Guid.NewGuid():N}@example.com",
                senha = "EquipeInicial@123",
                modoIdentificacao = "Pin"
            }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var publicListResponse = await client.GetAsync("/api/public/clinicas");
        Assert.Equal(HttpStatusCode.OK, publicListResponse.StatusCode);
        using (var publicListJson = await ReadJsonAsync(publicListResponse))
        {
            var publicClinic = publicListJson.RootElement.EnumerateArray()
                .Single(item => item.GetProperty("slug").GetString() == slug);
            Assert.NotEqual(JsonValueKind.Null, publicClinic.GetProperty("fotoUrl").ValueKind);
        }

        var publicPhotoResponse = await client.GetAsync($"/api/public/clinicas/{slug}/foto");
        Assert.Equal(HttpStatusCode.OK, publicPhotoResponse.StatusCode);
        Assert.Equal("foto-da-clinica", await publicPhotoResponse.Content.ReadAsStringAsync());

        var listResponse = await client.GetAsync("/api/platform/clinicas");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listJson = await ReadJsonAsync(listResponse))
        {
            Assert.Contains(listJson.RootElement.EnumerateArray(), item => item.GetProperty("slug").GetString() == slug);
        }

        var clinicsResponse = await client.GetAsync("/api/session/clinicas");
        clinicsResponse.EnsureSuccessStatusCode();
        using var clinicsJson = await ReadJsonAsync(clinicsResponse);
        var targetClinic = clinicsJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("slug").GetString() == slug);
        var targetClinicId = targetClinic.GetProperty("clinicaId").GetInt32();

        var switchResponse = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new
        {
            clinicaId = targetClinicId
        });
        switchResponse.EnsureSuccessStatusCode();
        using var switchJson = await ReadJsonAsync(switchResponse);
        var switchedToken = switchJson.RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switchedToken);

        var scopedResponse = await client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.OK, scopedResponse.StatusCode);
        using var scopedJson = await ReadJsonAsync(scopedResponse);
        Assert.Equal(3, scopedJson.RootElement.GetProperty("items").GetArrayLength());

        var teamsResponse = await client.GetAsync("/api/equipes/");
        teamsResponse.EnsureSuccessStatusCode();
        using var teamsJson = await ReadJsonAsync(teamsResponse);
        var initialTeam = Assert.Single(teamsJson.RootElement.EnumerateArray());
        Assert.Equal("Equipe Inicial", initialTeam.GetProperty("nome").GetString());
        Assert.Equal("Pin", initialTeam.GetProperty("modoIdentificacao").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "clinic.create" && item.ClinicaId == targetClinicId && item.Sucesso));
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "team.create" && item.ClinicaId == targetClinicId && item.Sucesso));
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "session.clinic.switch" && item.ClinicaId == targetClinicId && item.Sucesso));
    }

    [Fact]
    public async Task PasswordResetConfirm_WhenMultipleClinicasAndNoHintIsProvided_ResolvesClinicFromToken()
    {
        var passwordResetSender = new RecordingPasswordResetNotificationSender();
        using var factory = CreateFactoryWithPasswordResetSender(passwordResetSender);
        using var client = factory.CreateClient();
        await SeedClinicaBetaAsync(factory);

        var requestResponse = await PostAsJsonWithClinicHeaderAsync(
            client,
            Clinica.DefaultSlug,
            "/api/users/password/reset",
            new { email = "gmarcone@gmail.com" });

        Assert.Equal(HttpStatusCode.OK, requestResponse.StatusCode);
        var token = passwordResetSender.Notifications.Single().Token;

        var confirmResponse = await PostAsJsonWithIdempotencyKeyAsync(
            client,
            "/api/users/password/reset/confirm",
            Guid.NewGuid().ToString("N"),
            new
            {
                token,
                novaSenha = "NovaSenhaSemSlug@123"
            });

        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Equal(
            "stored",
            confirmResponse.Headers.GetValues(RequestIdempotencyService.IdempotencyStatusHeaderName).Single());
    }

    [Fact]
    public async Task SelectClinic_PreservesSessionAndRejectsPreviousClinicToken()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);

        var authResponse = await PostAsJsonWithClinicHeaderAsync(
            client,
            Clinica.DefaultSlug,
            "/api/users/authenticate",
            new
            {
                Email = "gmarcone@gmail.com",
                Senha = TestPasswords.Valid
            });
        authResponse.EnsureSuccessStatusCode();
        using var authJson = await ReadJsonAsync(authResponse);
        var previousToken = authJson.RootElement.GetProperty("token").GetString()!;
        var tokenHandler = new JwtSecurityTokenHandler();
        var previousJwt = tokenHandler.ReadJwtToken(previousToken);
        var sessionId = Guid.Parse(previousJwt.Claims.Single(claim =>
            claim.Type == AuthenticationSessionClaimTypes.SessionId).Value);
        var previousMembershipId = int.Parse(previousJwt.Claims.Single(claim =>
            claim.Type == GlobalIdentityClaimTypes.UsuarioClinicaId).Value);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", previousToken);

        var switchResponse = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new
        {
            clinicaId = beta.Id
        });
        switchResponse.EnsureSuccessStatusCode();
        using var switchJson = await ReadJsonAsync(switchResponse);
        var switchedToken = switchJson.RootElement.GetProperty("token").GetString()!;
        var switchedJwt = tokenHandler.ReadJwtToken(switchedToken);
        var switchedSessionId = Guid.Parse(switchedJwt.Claims.Single(claim =>
            claim.Type == AuthenticationSessionClaimTypes.SessionId).Value);
        var switchedMembershipId = int.Parse(switchedJwt.Claims.Single(claim =>
            claim.Type == GlobalIdentityClaimTypes.UsuarioClinicaId).Value);

        Assert.Equal(sessionId, switchedSessionId);
        Assert.NotEqual(previousMembershipId, switchedMembershipId);
        Assert.Equal(beta.Id.ToString(), switchedJwt.Claims.Single(claim =>
            claim.Type == ClinicaClaimTypes.ClinicaId).Value);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await context.AuthenticationSessions
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == sessionId);
            Assert.Equal(switchedMembershipId, session.UsuarioClinicaId);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", previousToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", switchedToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/")).StatusCode);
    }

    [Fact]
    public async Task PlatformClinicUpdate_CanResetPrincipalAdministratorPasswordWithoutExposingIt()
    {
        using var factory = new HemodinksApiFactory();
        using var platformClient = factory.CreateClient();
        await AuthenticateAsync(platformClient, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var slug = $"clinica-password-{Guid.NewGuid():N}";
        var email = $"admin-password-{Guid.NewGuid():N}@example.com";
        var initialPassword = TemporaryPasswordGenerator.Generate();
        var newPassword = TemporaryPasswordGenerator.Generate();
        var createResponse = await platformClient.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Senha Segura",
            slug,
            cnpj = ValidCnpj,
            administradorNome = "Administradora Principal",
            administradorEmail = email,
            administradorSenha = initialPassword,
            plano = "Completa"
        });
        createResponse.EnsureSuccessStatusCode();
        using var createJson = await ReadJsonAsync(createResponse);
        var clinicId = createJson.RootElement.GetProperty("id").GetInt32();

        var updateResponse = await platformClient.PutAsJsonAsync($"/api/platform/clinicas/{clinicId}", new
        {
            administradorNovaSenha = newPassword
        });
        updateResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(newPassword, await updateResponse.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
            var administrator = await context.Users.SingleAsync(item => item.ClinicaId == clinicId && item.PerfilId == Perfil.AdministradorId);
            Assert.True(administrator.PrecisaTrocarSenha);
            Assert.True(new PasswordHasher().VerifyPassword(newPassword, administrator.Senha));
            Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
                item.Acao == "clinic.administrator-password-reset"
                && item.ClinicaId == clinicId
                && item.EntidadeId == administrator.Id.ToString()
                && item.Sucesso));
        }

        using var clinicClient = factory.CreateClient();
        var loginResponse = await PostAsJsonWithClinicHeaderAsync(
            clinicClient,
            slug,
            "/api/users/authenticate",
            new { Email = email, Senha = newPassword });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task PlatformClinics_WhenPlanIsInvalid_ReturnsBadRequest()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var response = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Plano Invalido",
            slug = $"clinica-{Guid.NewGuid():N}",
            cnpj = ValidCnpj,
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123",
            plano = "Profissional"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Trial, Parcial ou Completa", await response.Content.ReadAsStringAsync());

        var updateResponse = await client.PutAsJsonAsync($"/api/platform/clinicas/{Clinica.DefaultId}", new
        {
            plano = "Profissional",
            cnpj = ValidCnpj
        });

        Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        Assert.Contains("Trial, Parcial ou Completa", await updateResponse.Content.ReadAsStringAsync());

        var partialWithoutModulesResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Parcial Sem Modulos",
            slug = $"clinica-{Guid.NewGuid():N}",
            cnpj = ValidCnpj,
            administradorNome = "Administradora Local",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLocal@123",
            plano = "Parcial"
        });

        Assert.Equal(HttpStatusCode.BadRequest, partialWithoutModulesResponse.StatusCode);
        Assert.Contains("ao menos um modulo", await partialWithoutModulesResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuperAdministrador_CanAddTeamWhenEditingExistingClinic()
    {
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);
        var betaDoctorGlobalId = 0;
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedScope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
            var betaDoctor = await seedContext.Users.SingleAsync(item =>
                item.ClinicaId == beta.Id && item.Email == "dra.beta@hemodinks.com");
            var betaMembership = await GlobalIdentityService.EnsureForUserAsync(
                seedContext, betaDoctor, CancellationToken.None);
            betaDoctorGlobalId = betaMembership.UsuarioGlobalId;
        }
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);
        var teamEmail = $"equipe-{Guid.NewGuid():N}@example.com";

        var updateResponse = await client.PutAsJsonAsync($"/api/platform/clinicas/{Clinica.DefaultId}", new
        {
            novaEquipe = new
            {
                nome = "Equipe da Clinica Existente",
                email = teamEmail,
                senha = "EquipeExistente@123",
                modoIdentificacao = "Selecao"
            }
        });

        updateResponse.EnsureSuccessStatusCode();
        var teamsResponse = await client.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}/equipes");
        teamsResponse.EnsureSuccessStatusCode();
        using var teamsJson = await ReadJsonAsync(teamsResponse);
        var team = teamsJson.RootElement.EnumerateArray().Single(item =>
            item.GetProperty("email").GetString() == teamEmail);
        var teamId = team.GetProperty("id").GetInt32();
        Assert.Equal("Selecao", team.GetProperty("modoIdentificacao").GetString());

        var usersResponse = await client.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}/equipes/usuarios");
        usersResponse.EnsureSuccessStatusCode();
        using var usersJson = await ReadJsonAsync(usersResponse);
        var candidates = usersJson.RootElement.EnumerateArray().ToArray();
        Assert.All(candidates, item => Assert.Contains(item.GetProperty("perfilId").GetInt32(), new[] { Perfil.MedicosId, Perfil.ControllerId, Perfil.EquipeId }));
        Assert.DoesNotContain(candidates, item => item.GetProperty("email").GetString() == "dra.beta@hemodinks.com");

        var crossClinicAssociationResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = new[] { betaDoctorGlobalId },
                novosUsuarios = Array.Empty<object>(),
                gerarPin = false
            });
        Assert.Equal(HttpStatusCode.BadRequest, crossClinicAssociationResponse.StatusCode);

        var localCandidate = candidates.First(item =>
            item.GetProperty("cadastradoNaClinica").GetBoolean()
            && item.GetProperty("usuarioGlobalId").ValueKind == JsonValueKind.Number
            && item.GetProperty("perfilId").GetInt32() is Perfil.MedicosId or Perfil.ControllerId);
        var selectedGlobalIds = new[] { localCandidate.GetProperty("usuarioGlobalId").GetInt32() };
        var selectedUserId = localCandidate.GetProperty("userIdNaClinica").GetInt32();
        var selectedName = localCandidate.GetProperty("nome").GetString();

        var modeResponse = await client.PutAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}",
            new { modoIdentificacao = "Pin" });
        Assert.Equal(HttpStatusCode.NoContent, modeResponse.StatusCode);

        var addMemberResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = selectedGlobalIds,
                novosUsuarios = new[] { new { nome = "Raquel Fernandes", telefone = (string?)null } },
                gerarPin = true
            });
        addMemberResponse.EnsureSuccessStatusCode();
        using var addMemberJson = await ReadJsonAsync(addMemberResponse);
        var associations = addMemberJson.RootElement.GetProperty("associados").EnumerateArray().ToArray();
        Assert.Equal(2, associations.Length);
        Assert.All(associations, item => Assert.Equal(6, item.GetProperty("pinTemporario").GetString()!.Length));
        var selectedAssociation = associations.Single(item => item.GetProperty("nome").GetString() == selectedName);
        var createdAssociation = associations.Single(item => item.GetProperty("nome").GetString() == "Raquel Fernandes");
        var operatorId = selectedAssociation.GetProperty("operadorId").GetInt32();
        var createdUserId = createdAssociation.GetProperty("userId").GetInt32();

        var candidatesAfterCreationResponse = await client.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}/equipes/usuarios");
        candidatesAfterCreationResponse.EnsureSuccessStatusCode();
        using var candidatesAfterCreationJson = await ReadJsonAsync(candidatesAfterCreationResponse);
        var createdCandidate = candidatesAfterCreationJson.RootElement.EnumerateArray().Single(item =>
            item.GetProperty("userIdNaClinica").GetInt32() == createdUserId);
        Assert.Equal(Perfil.EquipeId, createdCandidate.GetProperty("perfilId").GetInt32());
        Assert.Equal(JsonValueKind.Null, createdCandidate.GetProperty("usuarioGlobalId").ValueKind);

        var reassociateLocalUserResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = Array.Empty<int>(),
                userIds = new[] { createdUserId },
                novosUsuarios = Array.Empty<object>(),
                gerarPin = false
            });
        reassociateLocalUserResponse.EnsureSuccessStatusCode();

        var editCreatedUserResponse = await client.PutAsJsonAsync($"/api/users/{createdUserId}", new
        {
            nome = "Raquel Fernandes de Lima",
            email = teamEmail,
            telefone = "",
            ativo = true,
            perfilId = Perfil.EquipeId
        });
        editCreatedUserResponse.EnsureSuccessStatusCode();
        using var editCreatedUserJson = await ReadJsonAsync(editCreatedUserResponse);
        Assert.Equal(Perfil.EquipeId, editCreatedUserJson.RootElement.GetProperty("perfilId").GetInt32());
        Assert.Equal(string.Empty, editCreatedUserJson.RootElement.GetProperty("telefone").GetString());

        using var teamClient = factory.CreateClient();
        teamClient.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        var teamLoginResponse = await teamClient.PostAsJsonAsync("/api/users/authenticate", new
        {
            email = teamEmail,
            senha = "EquipeExistente@123"
        });
        teamLoginResponse.EnsureSuccessStatusCode();
        using var teamLoginJson = await ReadJsonAsync(teamLoginResponse);
        var teamOperators = teamLoginJson.RootElement.GetProperty("equipeDesafio").GetProperty("operadores").EnumerateArray();
        Assert.Contains(teamOperators, item => item.GetProperty("nome").GetString() == "Raquel Fernandes de Lima");

        var resetPinResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/operadores/{operatorId}/pin",
            new { });
        resetPinResponse.EnsureSuccessStatusCode();
        using var resetPinJson = await ReadJsonAsync(resetPinResponse);
        Assert.Equal(6, resetPinJson.RootElement.GetProperty("pinTemporario").GetString()!.Length);

        var removeMemberResponse = await client.DeleteAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/membros/{selectedUserId}");
        Assert.Equal(HttpStatusCode.NoContent, removeMemberResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var selectedUser = await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == selectedUserId);
        Assert.Equal(Clinica.DefaultId, selectedUser.ClinicaId);
        Assert.Contains(selectedUser.PerfilId, new[] { Perfil.MedicosId, Perfil.ControllerId });
        var createdUser = await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == createdUserId);
        Assert.Equal(Clinica.DefaultId, createdUser.ClinicaId);
        Assert.Equal(Perfil.EquipeId, createdUser.PerfilId);
        Assert.Equal(teamEmail, createdUser.Email);
        Assert.Equal(string.Empty, createdUser.Telefone);
        var teamLoginUser = await context.Equipes.IgnoreQueryFilters()
            .Where(item => item.Id == teamId)
            .Select(item => item.UsuarioLogin)
            .SingleAsync();
        Assert.Equal(teamLoginUser.Senha, createdUser.Senha);
        Assert.False(await context.UsuariosClinicas.IgnoreQueryFilters().AnyAsync(item => item.UserId == createdUserId));
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "team.create" && item.ClinicaId == Clinica.DefaultId && item.Sucesso));
        Assert.True(await context.AuditoriasPlataforma.AnyAsync(item =>
            item.Acao == "team.member.add" && item.ClinicaId == Clinica.DefaultId && item.Sucesso));
    }

    [Fact]
    public async Task ClinicEmployeeLimit_DoesNotCountTeamLoginOrPlatformShadowUser()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(client, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var slug = $"clinica-limite-{Guid.NewGuid():N}";
        var createResponse = await client.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica com Limite",
            slug,
            cnpj = ValidCnpj,
            administradorNome = "Administradora Limite",
            administradorEmail = $"admin-{Guid.NewGuid():N}@example.com",
            administradorSenha = "AdminLimite@123",
            limiteUsuarios = 3,
            equipeInicial = new
            {
                nome = "Equipe Limite",
                email = $"equipe-{Guid.NewGuid():N}@example.com",
                senha = "EquipeLimite@123",
                modoIdentificacao = "Selecao"
            }
        });
        createResponse.EnsureSuccessStatusCode();
        using var createJson = await ReadJsonAsync(createResponse);
        var clinicId = createJson.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(1, createJson.RootElement.GetProperty("usuarios").GetInt32());

        var switchResponse = await client.PostAsJsonAsync("/api/session/selecionar-clinica", new
        {
            clinicaId = clinicId
        });
        switchResponse.EnsureSuccessStatusCode();
        using var switchJson = await ReadJsonAsync(switchResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            switchJson.RootElement.GetProperty("token").GetString());

        var teamsResponse = await client.GetAsync($"/api/platform/clinicas/{clinicId}/equipes");
        teamsResponse.EnsureSuccessStatusCode();
        using var teamsJson = await ReadJsonAsync(teamsResponse);
        var teamId = teamsJson.RootElement.EnumerateArray().Single().GetProperty("id").GetInt32();

        var fillLimitResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{clinicId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = Array.Empty<int>(),
                novosUsuarios = new[]
                {
                    new { nome = "Funcionario Um", telefone = (string?)null },
                    new { nome = "Funcionario Dois", telefone = (string?)null }
                },
                gerarPin = false
            });
        Assert.True(
            fillLimitResponse.IsSuccessStatusCode,
            await fillLimitResponse.Content.ReadAsStringAsync());

        var exceedLimitResponse = await client.PostAsJsonAsync(
            $"/api/platform/clinicas/{clinicId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = Array.Empty<int>(),
                novosUsuarios = new[] { new { nome = "Funcionario Excedente", telefone = (string?)null } },
                gerarPin = false
            });
        Assert.Equal(HttpStatusCode.Conflict, exceedLimitResponse.StatusCode);
    }

    [Fact]
    public async Task TeamWithoutNominalPin_CanReachSensitiveOperations()
    {
        using var factory = new HemodinksApiFactory();
        using var platformClient = factory.CreateClient();
        await AuthenticateAsync(platformClient, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);
        var teamEmail = $"equipe-sem-pin-{Guid.NewGuid():N}@example.com";

        var createTeamResponse = await platformClient.PutAsJsonAsync($"/api/platform/clinicas/{Clinica.DefaultId}", new
        {
            novaEquipe = new
            {
                nome = "Equipe sem PIN nominal",
                email = teamEmail,
                senha = "EquipeSemPin@123",
                modoIdentificacao = "Nenhuma"
            }
        });
        createTeamResponse.EnsureSuccessStatusCode();

        var teamsResponse = await platformClient.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}/equipes");
        teamsResponse.EnsureSuccessStatusCode();
        using var teamsJson = await ReadJsonAsync(teamsResponse);
        var teamId = teamsJson.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("email").GetString() == teamEmail)
            .GetProperty("id").GetInt32();

        var candidatesResponse = await platformClient.GetAsync($"/api/platform/clinicas/{Clinica.DefaultId}/equipes/usuarios");
        candidatesResponse.EnsureSuccessStatusCode();
        using var candidatesJson = await ReadJsonAsync(candidatesResponse);
        var doctorGlobalId = candidatesJson.RootElement.EnumerateArray()
            .First(item => item.GetProperty("perfilId").GetInt32() == Perfil.MedicosId
                && item.GetProperty("usuarioGlobalId").ValueKind == JsonValueKind.Number)
            .GetProperty("usuarioGlobalId").GetInt32();

        (await platformClient.PutAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}",
            new { modoIdentificacao = "Pin" })).EnsureSuccessStatusCode();
        var associationResponse = await platformClient.PostAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}/membros",
            new
            {
                usuarioGlobalIds = new[] { doctorGlobalId },
                novosUsuarios = Array.Empty<object>(),
                gerarPin = true
            });
        associationResponse.EnsureSuccessStatusCode();
        using var associationJson = await ReadJsonAsync(associationResponse);
        var operatorId = associationJson.RootElement.GetProperty("associados")[0].GetProperty("operadorId").GetInt32();

        (await platformClient.PutAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}/equipes/{teamId}",
            new { modoIdentificacao = "Selecao" })).EnsureSuccessStatusCode();

        using var teamClient = factory.CreateClient();
        teamClient.DefaultRequestHeaders.Add("X-Clinica-Slug", Clinica.DefaultSlug);
        var teamLoginResponse = await teamClient.PostAsJsonAsync("/api/users/authenticate", new
        {
            email = teamEmail,
            senha = "EquipeSemPin@123"
        });
        teamLoginResponse.EnsureSuccessStatusCode();
        using var teamLoginJson = await ReadJsonAsync(teamLoginResponse);
        var challengeToken = teamLoginJson.RootElement.GetProperty("equipeDesafio").GetProperty("token").GetString();
        var identificationResponse = await teamClient.PostAsJsonAsync("/api/equipe-auth/identificar", new
        {
            token = challengeToken,
            operadorId = operatorId,
            pin = (string?)null
        });
        identificationResponse.EnsureSuccessStatusCode();
        using var identificationJson = await ReadJsonAsync(identificationResponse);
        Assert.False(identificationJson.RootElement.GetProperty("precisaTrocarPin").GetBoolean());
        teamClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            identificationJson.RootElement.GetProperty("token").GetString());
        var recipientsResponse = await teamClient.GetAsync("/api/events/notification-recipients");
        recipientsResponse.EnsureSuccessStatusCode();
        using var recipientsJson = await ReadJsonAsync(recipientsResponse);
        Assert.Equal(
            "Todos os membros ativos desta equipe",
            recipientsJson.RootElement.GetProperty("allRecipientsLabel").GetString());

        var teamEventTitle = $"Evento privado da equipe {Guid.NewGuid():N}";
        var sensitiveResponse = await teamClient.PostAsJsonAsync("/api/events/", new
        {
            title = teamEventTitle,
            start = DateTime.UtcNow.AddDays(1),
            end = DateTime.UtcNow.AddDays(1).AddHours(1),
            notifyMedicalProfile = false,
            notifyUser = false
        });
        sensitiveResponse.EnsureSuccessStatusCode();

        var teamEventsResponse = await teamClient.GetAsync("/api/events/");
        teamEventsResponse.EnsureSuccessStatusCode();
        Assert.Contains(teamEventTitle, await teamEventsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var outsiderEventsResponse = await platformClient.GetAsync("/api/events/");
        outsiderEventsResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(teamEventTitle, await outsiderEventsResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialClinicPlan_DoesNotRestrictAdministratorEndpoints()
    {
        using var factory = new HemodinksApiFactory();
        using var platformClient = factory.CreateClient();
        await AuthenticateAsync(platformClient, Clinica.DefaultSlug, "gmarcone@gmail.com", TestPasswords.Valid);

        var slug = $"clinica-{Guid.NewGuid():N}";
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        const string adminPassword = "AdminLocal@123";
        var createResponse = await platformClient.PostAsJsonAsync("/api/platform/clinicas", new
        {
            nome = "Clinica Parcial",
            slug,
            cnpj = ValidCnpj,
            administradorNome = "Administradora Parcial",
            administradorEmail = adminEmail,
            administradorSenha = adminPassword,
            plano = "Parcial",
            modulosLiberados = new[] { ClinicaModulos.Pacientes },
            assinaturaStatus = "Ativa",
            assinaturaValidaAte = DateTime.UtcNow.AddYears(1)
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using (var createJson = await ReadJsonAsync(createResponse))
        {
            var clinicId = createJson.RootElement.GetProperty("id").GetInt32();
            var switchResponse = await platformClient.PostAsJsonAsync("/api/session/selecionar-clinica", new
            {
                clinicaId = clinicId
            });
            switchResponse.EnsureSuccessStatusCode();
            using var switchJson = await ReadJsonAsync(switchResponse);
            platformClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                switchJson.RootElement.GetProperty("token").GetString());
            Assert.Equal(HttpStatusCode.OK, (await platformClient.GetAsync("/api/users/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await platformClient.GetAsync("/api/faturamentos-medicos/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await platformClient.GetAsync("/api/grupos-medicos/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await platformClient.GetAsync("/api/events/")).StatusCode);
        }

        using var clinicClient = factory.CreateClient();
        var authResponse = await PostAsJsonWithClinicHeaderAsync(
            clinicClient,
            slug,
            "/api/users/authenticate",
            new { Email = adminEmail, Senha = adminPassword });
        authResponse.EnsureSuccessStatusCode();
        using var authJson = await ReadJsonAsync(authResponse);
        var modules = authJson.RootElement.GetProperty("modulosLiberados").EnumerateArray().ToList();
        Assert.Single(modules);
        Assert.Equal(ClinicaModulos.Pacientes, modules[0].GetString());
        clinicClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            authJson.RootElement.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await clinicClient.GetAsync("/api/users/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clinicClient.GetAsync("/api/faturamentos-medicos/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clinicClient.GetAsync("/api/grupos-medicos/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clinicClient.GetAsync("/api/events/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clinicClient.GetAsync("/api/pacientes/")).StatusCode);
    }

    [Fact]
    public async Task PlatformClinics_WhenUserIsCommonAdministrator_ReturnsOnlyOwnClinic()
    {
        using var factory = new HemodinksApiFactory();
        using var client = factory.CreateClient();
        var beta = await SeedClinicaBetaAsync(factory);
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var response = await client.GetAsync("/api/platform/clinicas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadJsonAsync(response);
        var clinic = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(beta.Id, clinic.GetProperty("id").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/platform/clinicas/{beta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(
            $"/api/platform/clinicas/{beta.Id}",
            new { nome = "Clinica Beta Administrada" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/platform/clinicas/{beta.Id}",
            new { nome = "Clinica Beta Administrada", cnpj = ValidCnpj })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/platform/clinicas",
            new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(
            $"/api/platform/clinicas/{beta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            $"/api/platform/clinicas/{Clinica.DefaultId}",
            new { administradorNovaSenha = TemporaryPasswordGenerator.Generate() })).StatusCode);
    }

    [Fact]
    public async Task ClinicScopedEndpoints_DoNotExposeRecordsFromAnotherClinic()
    {
        const string marker = "TENANT-A-ONLY-MARKER";
        using var factory = new HemodinksApiFactory();
        var beta = await SeedClinicaBetaAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<HemodinksAPI.Application.Tenancy.ClinicaContext>().SetPlatformScope();
            var admin = new User
            {
                ClinicaId = Clinica.DefaultId,
                Nome = marker,
                Email = $"{Guid.NewGuid():N}@example.com",
                Telefone = "+5511999999901",
                Senha = "hash",
                PerfilId = Perfil.AdministradorId,
                Ativo = true
            };
            var patientUser = new User
            {
                ClinicaId = Clinica.DefaultId,
                Nome = marker,
                Email = $"{Guid.NewGuid():N}@example.com",
                Telefone = "+5511999999902",
                Senha = "hash",
                PerfilId = Perfil.PacientesId,
                Ativo = true
            };
            var patient = new Paciente
            {
                ClinicaId = Clinica.DefaultId,
                User = patientUser,
                NomePaciente = marker
            };

            context.Users.Add(admin);
            context.Pacientes.Add(patient);
            context.FaturamentosMedicos.Add(new FaturamentoMedico
            {
                ClinicaId = Clinica.DefaultId,
                Paciente = patient,
                Observacoes = marker
            });
            context.Events.Add(new Event
            {
                ClinicaId = Clinica.DefaultId,
                User = admin,
                Title = marker,
                Start = DateTime.UtcNow.AddDays(1),
                End = DateTime.UtcNow.AddDays(1).AddHours(1)
            });
            context.GruposMedicos.Add(new GrupoMedico { ClinicaId = Clinica.DefaultId, Nome = marker });
            context.Hospitais.Add(new Hospital { ClinicaId = Clinica.DefaultId, Nome = marker });
            context.Convenios.Add(new Convenio { ClinicaId = Clinica.DefaultId, DescricaoConvenio = marker });
            context.OPME.Add(new Opme { ClinicaId = Clinica.DefaultId, Fornecedor = marker });
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        await AuthenticateAsync(client, beta.Slug, beta.AdminEmail, beta.AdminPassword);

        var scopedUrls = new[]
        {
            "/api/users/",
            "/api/pacientes/",
            "/api/faturamentos-medicos/",
            "/api/events/",
            "/api/grupos-medicos/",
            "/api/hospitais/",
            "/api/convenios/",
            "/api/opme/",
            "/api/dashboard/summary",
            "/api/dashboard/notifications"
        };

        foreach (var url in scopedUrls)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.False(
                content.Contains(marker, StringComparison.Ordinal),
                $"A rota {url} expos dados de outra clinica: {content}");
        }
    }

}
