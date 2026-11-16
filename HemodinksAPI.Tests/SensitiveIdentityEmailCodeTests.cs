using System.Security.Cryptography;
using HemodinksAPI.Application.Authorization;
using HemodinksAPI.Application.Features.Users.Commands;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HemodinksAPI.Tests;

// #322: código curto de 6 dígitos, HMAC com chave derivada e limite de tentativas.
public sealed class SensitiveIdentityEmailCodeTests
{
    private const int MaxAttempts = 5;

    private static async Task<CurrentUserContext> ActorAsync(PasswordResetSecurityDbContext db) =>
        SensitiveIdentityTestSupport.Actor(await db.AuthenticationSessions.Include(x => x.UsuarioClinica).ThenInclude(x => x.User).SingleAsync());

    private static string WrongCode(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

    [Fact]
    public async Task ShortCode_PastedWithSpaces_ConfirmsEmail()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);

        await service.ConfirmEmailAsync(actor, pending.RequestId, $" {sender.Code[..3]} {sender.Code[3..]} ", default);

        Assert.Equal("new@example.com", (await db.UsuariosGlobais.SingleAsync()).Email);
        Assert.Equal(10, sender.ValidMinutes);
    }

    [Fact]
    public async Task WrongCodes_InvalidateRequestAtLimit_AndCorrectCodeNoLongerWorks()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        var wrong = WrongCode(sender.Code);

        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            var failure = await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, wrong, default));
            Assert.Equal("identity_revalidation_failed", failure.Code);
        }
        var exceeded = await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, wrong, default));
        Assert.Equal("email_confirmation_attempts_exceeded", exceeded.Code);

        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default));
        await using var verify = database.Open();
        var stored = await verify.EmailChangeRequests.SingleAsync();
        Assert.Equal(MaxAttempts, stored.ConfirmationAttempts);
        Assert.NotNull(stored.UsedAt);
        Assert.Equal("recovery@example.com", (await verify.UsuariosGlobais.SingleAsync()).Email);
        Assert.Null((await verify.AuthenticationSessions.SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task CorrectCode_OnLastAllowedAttempt_StillConfirms()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        var service = SensitiveIdentityTestSupport.Service(db, sender);
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        for (var attempt = 1; attempt < MaxAttempts; attempt++)
            await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, WrongCode(sender.Code), default));

        await service.ConfirmEmailAsync(actor, pending.RequestId, sender.Code, default);

        Assert.Equal("new@example.com", (await db.UsuariosGlobais.SingleAsync()).Email);
    }

    // Revisão de segurança: com a comparação antes do incremento, requisições paralelas somavam palpites.
    // Agora toda tentativa reserva vaga antes de comparar; com as vagas tomadas, nem o código certo passa.
    [Fact]
    public async Task CorrectCode_WhenAllAttemptsAreReservedInFlight_IsRejectedWithoutComparison()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        Guid id; CurrentUserContext actor; var sender = new EmailConfirmationRecorder();
        await using (var request = database.Open())
        {
            actor = await ActorAsync(request);
            id = (await SensitiveIdentityTestSupport.Service(request, sender).RequestEmailAsync(actor,
                PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default)).RequestId;
        }
        await using var guesser = database.Open();
        await guesser.EmailChangeRequests.LoadAsync(); // carregou a solicitação ainda com vagas e sem UsedAt
        await using (var inFlight = database.Open())
        {
            // Outras requisições já reservaram todas as vagas e ainda não terminaram (UsedAt nulo).
            var stored = await inFlight.EmailChangeRequests.SingleAsync();
            stored.ConfirmationAttempts = MaxAttempts;
            await inFlight.SaveChangesAsync();
        }

        var failure = await Assert.ThrowsAsync<SensitiveIdentityException>(() =>
            SensitiveIdentityTestSupport.Service(guesser).ConfirmEmailAsync(actor, id, sender.Code, default));

        Assert.Equal("email_confirmation_attempts_exceeded", failure.Code);
        await using var verify = database.Open();
        Assert.Equal("recovery@example.com", (await verify.UsuariosGlobais.SingleAsync()).Email);
        Assert.NotNull((await verify.EmailChangeRequests.SingleAsync()).UsedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("１２３４５６")] // dígitos não ASCII
    [InlineData("000000000000000000000000000000000")]
    public async Task MalformedCode_CountsAsWrongAttempt(string code)
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var service = SensitiveIdentityTestSupport.Service(db, new EmailConfirmationRecorder());
        var pending = await service.RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);

        await Assert.ThrowsAsync<SensitiveIdentityException>(() => service.ConfirmEmailAsync(actor, pending.RequestId, code, default));

        await using var verify = database.Open();
        Assert.Equal(1, (await verify.EmailChangeRequests.SingleAsync()).ConfirmationAttempts);
    }

    [Fact]
    public async Task StaleConcurrentConsumers_NeverExceedAttemptLimit()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        Guid id; CurrentUserContext actor; var sender = new EmailConfirmationRecorder();
        await using (var request = database.Open())
        {
            actor = await ActorAsync(request);
            id = (await SensitiveIdentityTestSupport.Service(request, sender).RequestEmailAsync(actor,
                PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default)).RequestId;
        }
        // Todos carregam a solicitação ainda válida antes de qualquer erro ser gravado.
        var consumers = new List<PasswordResetSecurityDbContext>();
        for (var i = 0; i < MaxAttempts + 3; i++)
        {
            var consumer = database.Open();
            await consumer.EmailChangeRequests.LoadAsync();
            consumers.Add(consumer);
        }
        var codes = new List<string>();
        foreach (var consumer in consumers)
        {
            var failure = await Assert.ThrowsAsync<SensitiveIdentityException>(() =>
                SensitiveIdentityTestSupport.Service(consumer).ConfirmEmailAsync(actor, id, WrongCode(sender.Code), default));
            codes.Add(failure.Code);
            await consumer.DisposeAsync();
        }

        await using var verify = database.Open();
        var stored = await verify.EmailChangeRequests.SingleAsync();
        Assert.Equal(MaxAttempts, stored.ConfirmationAttempts);
        Assert.NotNull(stored.UsedAt);
        Assert.Contains("email_confirmation_attempts_exceeded", codes);
        Assert.Equal("recovery@example.com", (await verify.UsuariosGlobais.SingleAsync()).Email);
    }

    [Fact]
    public async Task StoredHash_IsKeyedAndBoundToRequest()
    {
        await using var database = await PasswordResetSecurityTestDatabase.CreateAsync();
        await using var db = database.Open();
        var actor = await ActorAsync(db);
        var sender = new EmailConfirmationRecorder();
        await SensitiveIdentityTestSupport.Service(db, sender).RequestEmailAsync(actor, PasswordResetSecurityTestDatabase.OldPassword, "new@example.com", default);
        var stored = await db.EmailChangeRequests.SingleAsync();

        Assert.Equal(64, stored.CodeHash.Length);
        Assert.NotEqual(PasswordResetRules.HashToken("email-change:" + sender.Code), stored.CodeHash);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sender.Code))), stored.CodeHash);
        Assert.NotEqual(SensitiveIdentityTestSupport.CodeHasher.Hash(Guid.NewGuid(), sender.Code), stored.CodeHash);
        using var otherKey = HmacEmailChangeCodeHasher.CreateEphemeral();
        Assert.NotEqual(otherKey.Hash(stored.Id, sender.Code), stored.CodeHash);
    }

    [Fact]
    public void DerivedKey_IsDeterministic_AndIndependentFromLookupKey()
    {
        var lookupKey = RandomNumberGenerator.GetBytes(32);
        using var first = HmacEmailChangeCodeHasher.FromLookupHashKey(lookupKey);
        using var second = HmacEmailChangeCodeHasher.FromLookupHashKey(lookupKey);
        var id = Guid.NewGuid();

        Assert.Equal(first.Hash(id, "123456"), second.Hash(id, "123456"));
        var rawKeyHmac = Convert.ToHexString(HMACSHA256.HashData(lookupKey, System.Text.Encoding.UTF8.GetBytes($"{id:N}:123456")));
        Assert.NotEqual(rawKeyHmac, first.Hash(id, "123456"));
        Assert.Throws<ArgumentException>(() => HmacEmailChangeCodeHasher.FromLookupHashKey(new byte[16]));
    }
}
