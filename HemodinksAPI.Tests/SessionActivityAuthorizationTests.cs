using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HemodinksAPI.Application.Features.Sessions;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HemodinksAPI.Tests;

public sealed partial class SessionRenewalEndpointTests
{
    [Fact]
    public async Task GroupedActivity_ProfileRemovalImmediatelyChangesAuthorization()
    {
        var clock = new Clock();
        using var factory = new HemodinksApiFactory(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(new AuthenticationSessionOptions { ActivityPersistenceIntervalSeconds = 30 });
        });
        using var client = factory.CreateClient();
        var login = await Login(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var session = await db.AuthenticationSessions.Include(s => s.UsuarioClinica).ThenInclude(m => m.User)
                .SingleAsync(s => s.Id == login.Id);
            session.UsuarioClinica.User.PerfilId = Perfil.PacientesId;
            await db.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/session/atividade", new { })).StatusCode);
    }
}
