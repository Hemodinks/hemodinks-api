using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Privacy;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class PrivacyPreferenceHandlersTests
{
    [Fact]
    public async Task GetCurrent_WithoutPreference_ReturnsExplicitOptOutDefaults()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "privacy-empty@example.com");
        var handler = new GetCurrentPrivacyPreferenceQueryHandler(
            context,
            ClinicaContextFactory.CreateDefaultResolved());

        var result = await handler.Handle(
            new GetCurrentPrivacyPreferenceQuery(CurrentUser(user)),
            CancellationToken.None);

        Assert.False(result.HasPreference);
        Assert.False(result.PreferencesEnabled);
        Assert.False(result.AnalyticsEnabled);
        Assert.Equal("1.1", result.CurrentDocumentVersion);
        Assert.Null(result.DocumentVersion);
    }

    [Fact]
    public async Task UpdateCurrent_CreatesAndUpdatesSingleTenantScopedRecord()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "privacy-update@example.com");
        var firstInstant = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(firstInstant);
        var handler = new UpdateCurrentPrivacyPreferenceCommandHandler(
            context,
            ClinicaContextFactory.CreateDefaultResolved(),
            time);

        await handler.Handle(
            new UpdateCurrentPrivacyPreferenceCommand(CurrentUser(user), "1.1", true, false),
            CancellationToken.None);
        time.Instant = firstInstant.AddHours(1);
        var result = await handler.Handle(
            new UpdateCurrentPrivacyPreferenceCommand(CurrentUser(user), "1.1", false, true),
            CancellationToken.None);
        var record = await context.UserPrivacyPreferences.SingleAsync();

        Assert.True(result.HasPreference);
        Assert.False(result.PreferencesEnabled);
        Assert.True(result.AnalyticsEnabled);
        Assert.Equal(firstInstant.UtcDateTime, record.AcceptedAtUtc);
        Assert.Equal(time.Instant.UtcDateTime, record.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, record.UpdatedAtUtc.Kind);
        Assert.Equal(Clinica.DefaultId, record.ClinicaId);
    }

    [Fact]
    public async Task UpdateCurrent_RejectsStaleDocumentVersion()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "privacy-version@example.com");
        var handler = new UpdateCurrentPrivacyPreferenceCommandHandler(
            context,
            ClinicaContextFactory.CreateDefaultResolved(),
            new MutableTimeProvider(DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new UpdateCurrentPrivacyPreferenceCommand(CurrentUser(user), "1.0", true, true),
            CancellationToken.None));
        Assert.Empty(context.UserPrivacyPreferences);
    }

    [Fact]
    public async Task Preference_IsIsolatedByUser_AndRejectsDifferentResolvedClinic()
    {
        await using var context = TestDbContextFactory.Create();
        var firstUser = await AddUserAsync(context, "privacy-a@example.com");
        var secondUser = await AddUserAsync(context, "privacy-b@example.com");
        var tenant = ClinicaContextFactory.CreateDefaultResolved();
        await new UpdateCurrentPrivacyPreferenceCommandHandler(
            context,
            tenant,
            new MutableTimeProvider(DateTimeOffset.UtcNow)).Handle(
                new UpdateCurrentPrivacyPreferenceCommand(CurrentUser(firstUser), "1.1", true, true),
                CancellationToken.None);

        var secondResult = await new GetCurrentPrivacyPreferenceQueryHandler(context, tenant).Handle(
            new GetCurrentPrivacyPreferenceQuery(CurrentUser(secondUser)),
            CancellationToken.None);
        var mismatched = CurrentUser(secondUser) with { ClinicaId = 2, ClinicaSlug = "outra" };

        Assert.False(secondResult.HasPreference);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new GetCurrentPrivacyPreferenceQueryHandler(context, tenant).Handle(
                new GetCurrentPrivacyPreferenceQuery(mismatched),
                CancellationToken.None));
    }

    private static async Task<User> AddUserAsync(DbContext context, string email)
    {
        var user = new User
        {
            ClinicaId = Clinica.DefaultId,
            Nome = email,
            Email = email,
            Telefone = "+5511999999999",
            Senha = "hash",
            PerfilId = Perfil.AdministradorId,
            Ativo = true,
            PrecisaTrocarSenha = false
        };
        context.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static CurrentUserContext CurrentUser(User user) =>
        new(user.Id, user.PerfilId, user.Nome, user.ClinicaId, Clinica.DefaultSlug);

    private sealed class MutableTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public DateTimeOffset Instant { get; set; } = instant;

        public override DateTimeOffset GetUtcNow() => Instant;
    }
}
