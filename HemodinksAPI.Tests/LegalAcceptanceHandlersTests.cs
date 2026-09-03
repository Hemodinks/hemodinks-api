using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Legal;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

public sealed class LegalAcceptanceHandlersTests
{
    private static readonly DateTimeOffset AcceptanceInstant =
        new(2026, 9, 3, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCurrent_WithoutAcceptance_RequiresTermsAcceptance()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "sem-aceite@example.com");
        var tenant = ClinicaContextFactory.CreateDefaultResolved();
        var handler = new GetLegalAcceptanceStatusQueryHandler(context, tenant);

        var status = await handler.Handle(
            new GetLegalAcceptanceStatusQuery(CurrentUser(user)),
            CancellationToken.None);

        Assert.True(status.RequiresAcceptance);
        Assert.False(status.TermsOfUse.IsCurrent);
        Assert.False(status.PrivacyNotice.IsCurrent);
        Assert.Equal("1.1", status.TermsOfUse.CurrentVersion);
        Assert.Equal("1.1", status.PrivacyNotice.CurrentVersion);
    }

    [Fact]
    public async Task AcceptCurrent_PersistsVersionedUtcRecords_AndIsIdempotent()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "aceite@example.com");
        var tenant = ClinicaContextFactory.CreateDefaultResolved();
        var handler = new AcceptCurrentLegalDocumentsCommandHandler(
            context,
            tenant,
            new FixedTimeProvider(AcceptanceInstant));
        var command = new AcceptCurrentLegalDocumentsCommand(CurrentUser(user), "1.1", "1.1");

        var first = await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);
        var records = await context.UserLegalAcceptances
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.DocumentType)
            .ToListAsync();

        Assert.False(first.RequiresAcceptance);
        Assert.False(second.RequiresAcceptance);
        Assert.All(new[] { first.TermsOfUse, first.PrivacyNotice }, item => Assert.True(item.IsCurrent));
        Assert.Equal(2, records.Count);
        Assert.All(records, item =>
        {
            Assert.Equal("1.1", item.DocumentVersion);
            Assert.Equal(AcceptanceInstant.UtcDateTime, item.AcceptedAtUtc);
            Assert.Equal(DateTimeKind.Utc, item.AcceptedAtUtc.Kind);
            Assert.Equal(Clinica.DefaultId, item.ClinicaId);
        });
    }

    [Fact]
    public async Task OlderTermsVersion_RequiresNewAcceptance_AndPreservesHistory()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "historico@example.com");
        context.UserLegalAcceptances.Add(new UserLegalAcceptance
        {
            UserId = user.Id,
            ClinicaId = Clinica.DefaultId,
            DocumentType = LegalDocumentType.TermsOfUse,
            DocumentVersion = "1.0",
            AcceptedAtUtc = AcceptanceInstant.AddDays(-1).UtcDateTime
        });
        await context.SaveChangesAsync();
        var tenant = ClinicaContextFactory.CreateDefaultResolved();

        var before = await new GetLegalAcceptanceStatusQueryHandler(context, tenant).Handle(
            new GetLegalAcceptanceStatusQuery(CurrentUser(user)),
            CancellationToken.None);
        await new AcceptCurrentLegalDocumentsCommandHandler(context, tenant, new FixedTimeProvider(AcceptanceInstant)).Handle(
            new AcceptCurrentLegalDocumentsCommand(CurrentUser(user), "1.1", "1.1"),
            CancellationToken.None);

        Assert.True(before.RequiresAcceptance);
        Assert.Equal("1.0", before.TermsOfUse.AcceptedVersion);
        Assert.Equal(3, await context.UserLegalAcceptances.CountAsync(item => item.UserId == user.Id));
        Assert.Contains(await context.UserLegalAcceptances.ToListAsync(),
            item => item.DocumentType == LegalDocumentType.TermsOfUse && item.DocumentVersion == "1.0");
    }

    [Fact]
    public async Task Status_IsIsolatedByUser_AndRejectsDifferentResolvedClinic()
    {
        await using var context = TestDbContextFactory.Create();
        var acceptedUser = await AddUserAsync(context, "usuario-a@example.com");
        var otherUser = await AddUserAsync(context, "usuario-b@example.com");
        var tenant = ClinicaContextFactory.CreateDefaultResolved();
        await new AcceptCurrentLegalDocumentsCommandHandler(context, tenant, new FixedTimeProvider(AcceptanceInstant)).Handle(
            new AcceptCurrentLegalDocumentsCommand(CurrentUser(acceptedUser), "1.1", "1.1"),
            CancellationToken.None);

        var otherStatus = await new GetLegalAcceptanceStatusQueryHandler(context, tenant).Handle(
            new GetLegalAcceptanceStatusQuery(CurrentUser(otherUser)),
            CancellationToken.None);
        var mismatchedUser = CurrentUser(otherUser) with { ClinicaId = 2, ClinicaSlug = "outra" };

        Assert.True(otherStatus.RequiresAcceptance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new GetLegalAcceptanceStatusQueryHandler(context, tenant).Handle(
                new GetLegalAcceptanceStatusQuery(mismatchedUser),
                CancellationToken.None));
    }

    [Fact]
    public async Task AcceptCurrent_RejectsStaleClientVersions()
    {
        await using var context = TestDbContextFactory.Create();
        var user = await AddUserAsync(context, "versao-antiga@example.com");
        var handler = new AcceptCurrentLegalDocumentsCommandHandler(
            context,
            ClinicaContextFactory.CreateDefaultResolved(),
            new FixedTimeProvider(AcceptanceInstant));

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new AcceptCurrentLegalDocumentsCommand(CurrentUser(user), "1.0", "1.1"),
            CancellationToken.None));
        Assert.Empty(context.UserLegalAcceptances);
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

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
