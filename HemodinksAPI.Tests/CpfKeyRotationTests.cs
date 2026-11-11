using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using HemodinksAPI.Application.Features.Users.Queries;
using HemodinksAPI.Application.Security;
using HemodinksAPI.Application.Tenancy;
using HemodinksAPI.Domain.Models;
using HemodinksAPI.Infrastructure.Data;
using HemodinksAPI.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

/// <summary>Rotacao do certificado do anel de chaves e da chave de hash do CPF (#227).</summary>
public sealed class CpfKeyRotationTests
{
    private const string CpfA = "52998224725";
    private const string CpfB = "11144477735";

    [Fact]
    public void Certificate_rotation_reads_old_keys_creates_new_default_key_and_lets_old_certificate_go()
    {
        using var oldCertificate = CreateCertificate();
        using var newCertificate = CreateCertificate();
        var repository = new InMemoryXmlRepository();
        var hashKey = RandomNumberGenerator.GetBytes(32);

        using var before = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            oldCertificate, hashKey, repository, NullLoggerFactory.Instance);
        var oldPayload = before.Protect(CpfA);
        Assert.Equal(0, before.PrepareCertificateRotation().RetiringKeys);

        using var rotating = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            newCertificate, hashKey, repository, NullLoggerFactory.Instance, [oldCertificate]);
        Assert.Equal(CpfA, rotating.TryUnprotect(oldPayload));

        var prepared = rotating.PrepareCertificateRotation();
        Assert.Equal(1, prepared.RetiringKeys);
        Assert.True(prepared.CreatedKey);
        Assert.True(rotating.RequiresReprotection(oldPayload));

        var newPayload = rotating.Protect(CpfA);
        Assert.False(rotating.RequiresReprotection(newPayload));
        Assert.Equal(2, repository.Elements.Count);
        Assert.All(repository.Elements, element => Assert.DoesNotContain("<masterKey", element.ToString(), StringComparison.Ordinal));

        // Idempotente: a chave padrao ja e do certificado atual.
        Assert.False(rotating.PrepareCertificateRotation().CreatedKey);
        Assert.Equal(2, repository.Elements.Count);

        // Sem o certificado anterior, o que foi recifrado continua legivel; o antigo nao.
        using var after = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            newCertificate, hashKey, repository, NullLoggerFactory.Instance);
        Assert.Equal(CpfA, after.TryUnprotect(newPayload));
        Assert.Null(after.TryUnprotect(oldPayload));
        Assert.False(after.PrepareCertificateRotation().CreatedKey);
    }

    [Fact]
    public async Task Reconciler_reprotects_rows_from_old_certificate_after_rotation_is_prepared()
    {
        using var oldCertificate = CreateCertificate();
        using var newCertificate = CreateCertificate();
        var repository = new InMemoryXmlRepository();
        var hashKey = RandomNumberGenerator.GetBytes(32);
        using var before = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            oldCertificate, hashKey, repository, NullLoggerFactory.Instance);

        await using var connection = await OpenDatabase();
        await InsertRow(connection, 1, CpfA, before.Protect(CpfA), before.ComputeLookupHash(CpfA));

        using var rotating = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            newCertificate, hashKey, repository, NullLoggerFactory.Instance, [oldCertificate]);

        // Sem preparar a rotacao nenhuma chave e marcada como antiga: nada e regravado.
        await using (var context = SqliteContext(connection, rotating))
        {
            var unprepared = await Reconcile(context, rotating);
            Assert.Equal(0, unprepared.Updated);
        }

        rotating.PrepareCertificateRotation();
        await using (var context = SqliteContext(connection, rotating))
        {
            var result = await Reconcile(context, rotating);
            Assert.Equal(1, result.Updated);
            Assert.Equal(0, result.PendingReprotection);
            Assert.Equal(0, (await Reconcile(context, rotating)).Updated);
        }

        using var after = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            newCertificate, hashKey, repository, NullLoggerFactory.Instance);
        await using var reader = SqliteContext(connection, after);
        var stored = await reader.Users.AsNoTracking().Select(user => user.CpfCifrado).SingleAsync();
        Assert.Equal(CpfA, after.TryUnprotect(stored!));
    }

    [Fact]
    public void Only_payloads_from_old_certificate_keys_require_reprotection()
    {
        using var oldCertificate = CreateCertificate();
        using var newCertificate = CreateCertificate();
        var repository = new InMemoryXmlRepository();
        var hashKey = RandomNumberGenerator.GetBytes(32);
        using var before = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            oldCertificate, hashKey, repository, NullLoggerFactory.Instance);
        var oldPayload = before.Protect(CpfA);

        using var rotating = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            newCertificate, hashKey, repository, NullLoggerFactory.Instance, [oldCertificate]);
        rotating.PrepareCertificateRotation();
        Assert.True(rotating.RequiresReprotection(oldPayload));
        Assert.False(rotating.RequiresReprotection("nao-e-payload"));
        Assert.False(TestCpfProtection.Instance.RequiresReprotection(oldPayload));
    }

    [Fact]
    public async Task Hash_key_rotation_keeps_search_and_uniqueness_until_reconciler_rewrites_hashes()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var ephemeral = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(NullLoggerFactory.Instance);
        using var oldProtector = new DataProtectionCpfProtector(ephemeral, oldKey);
        using var rotating = new DataProtectionCpfProtector(ephemeral, newKey, null, [oldKey]);

        Assert.Equal([rotating.ComputeLookupHash(CpfA)!, oldProtector.ComputeLookupHash(CpfA)!], rotating.ComputeLookupHashCandidates(CpfA));
        Assert.Empty(rotating.ComputeLookupHashCandidates(" "));

        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var platform = new AppDbContext(options, ClinicaContextFactory.CreatePlatform(), oldProtector))
        {
            await platform.Database.EnsureCreatedAsync();
            platform.Clinicas.Add(new Clinica { Id = 2, Nome = "Clinica B", Slug = "clinica-b" });
            platform.Users.Add(NewUser(Clinica.DefaultId, "a@example.com", CpfA));
            platform.Users.Add(NewUser(2, "b@example.com", CpfB));
            await platform.SaveChangesAsync();
        }

        await using var clinicA = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved(), rotating);
        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(2, "clinica-b");
        await using var clinicB = new AppDbContext(options, tenantB, rotating);

        // Hash ainda com a chave anterior: a busca encontra pela lista de candidatos.
        Assert.Equal(["a@example.com"], await SearchUsers(clinicA, CpfA));
        Assert.Empty(await SearchUsers(clinicB, CpfA));
        Assert.True(await clinicA.Users.WhereCpfMatches(clinicA, CpfA).AnyAsync());
        Assert.False(await clinicB.Users.WhereCpfMatches(clinicB, CpfA).AnyAsync());

        // O reconciliador regrava o hash com a chave atual; depois disso a anterior pode sair da configuracao.
        await using var connection = await OpenDatabase();
        await InsertRow(connection, Clinica.DefaultId, CpfA, oldProtector.Protect(CpfA), oldProtector.ComputeLookupHash(CpfA));
        await using var sqlite = SqliteContext(connection, rotating);
        Assert.Equal(1, (await Reconcile(sqlite, rotating)).Updated);
        using var afterRotation = new DataProtectionCpfProtector(ephemeral, newKey);
        Assert.Equal(
            afterRotation.ComputeLookupHash(CpfA),
            await sqlite.Users.AsNoTracking().Select(user => user.CpfHash).SingleAsync());
    }

    [Fact]
    public void Rotation_settings_are_validated_without_echoing_values()
    {
        using var current = CreateCertificate();
        using var previous = CreateCertificate();
        var currentPfx = Convert.ToBase64String(current.Export(X509ContentType.Pkcs12));
        var hashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var services = Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = currentPfx,
            ["CpfProtection:LookupHashKey"] = hashKey,
            ["CpfProtection:PreviousCertificates:0:CertificateBase64"] = Convert.ToBase64String(previous.Export(X509ContentType.Pkcs12, "anterior")),
            ["CpfProtection:PreviousCertificates:0:CertificatePassword"] = "anterior",
            ["CpfProtection:PreviousLookupHashKeys:0"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICpfProtector));

        var weakPrevious = Convert.ToBase64String(new byte[16]);
        var weak = Assert.Throws<InvalidOperationException>(() => Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = currentPfx,
            ["CpfProtection:LookupHashKey"] = hashKey,
            ["CpfProtection:PreviousLookupHashKeys:0"] = weakPrevious
        }));
        Assert.Contains("PreviousLookupHashKeys:0", weak.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(weakPrevious, weak.Message, StringComparison.Ordinal);

        var wrongPassword = Assert.Throws<InvalidOperationException>(() => Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = currentPfx,
            ["CpfProtection:LookupHashKey"] = hashKey,
            ["CpfProtection:PreviousCertificates:0:CertificateBase64"] = Convert.ToBase64String(previous.Export(X509ContentType.Pkcs12, "certa")),
            ["CpfProtection:PreviousCertificates:0:CertificatePassword"] = "errada"
        }));
        Assert.Contains("PreviousCertificates:0", wrongPassword.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("errada", wrongPassword.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => Register("Development", new()
        {
            ["CpfProtection:PreviousLookupHashKeys:0"] = hashKey
        }));
    }

    private static Task<CpfReconciliationResult> Reconcile(
        AppDbContext context, ICpfProtector protector) =>
        CpfProtectionReconciliationService.ReconcileAsync(context, protector, NullLogger.Instance, CancellationToken.None);

    private static async Task<string[]> SearchUsers(AppDbContext context, string search)
    {
        var handler = new GetAllUsersQueryHandler(context, NullLogger<GetAllUsersQueryHandler>.Instance);
        var result = await handler.Handle(new GetAllUsersQuery { Search = search, PageSize = 50 }, CancellationToken.None);
        return result.Items.Select(item => item.Email).Order().ToArray();
    }

    private static User NewUser(int clinicaId, string email, string? cpf) => new()
    {
        ClinicaId = clinicaId,
        Nome = "Usuario CPF",
        Email = email,
        Telefone = "+5511999990000",
        Cpf = cpf,
        Senha = "hash",
        PerfilId = Perfil.MedicosId
    };

    private static async Task<SqliteConnection> OpenDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, ClinicaId INTEGER NOT NULL,
                Nome TEXT, Email TEXT, Telefone TEXT, Senha TEXT, PerfilId INTEGER NOT NULL DEFAULT 2,
                Ativo INTEGER NOT NULL DEFAULT 1, PrecisaTrocarSenha INTEGER NOT NULL DEFAULT 1,
                DataCadastro TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, DataAtualizacao TEXT, DataNascimento TEXT,
                Crm TEXT, CrmUf TEXT, FotoPerfil TEXT, Cpf TEXT, CpfCifrado TEXT, CpfHash TEXT);
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task InsertRow(SqliteConnection connection, int clinicaId, string cpf, string cifrado, string? hash)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Users (ClinicaId, Cpf, CpfCifrado, CpfHash) VALUES ($clinica, $cpf, $cifrado, $hash)";
        command.Parameters.AddWithValue("$clinica", clinicaId);
        command.Parameters.AddWithValue("$cpf", cpf);
        command.Parameters.AddWithValue("$cifrado", cifrado);
        command.Parameters.AddWithValue("$hash", (object?)hash ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static AppDbContext SqliteContext(SqliteConnection connection, ICpfProtector protector) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, ClinicaContextFactory.CreatePlatform(), protector);

    private static IServiceCollection Register(string environment, Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddCpfProtection(configuration, new FakeHostEnvironment(environment));
        return services;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=hemodinks-cpf-rotation-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private sealed class InMemoryXmlRepository : IXmlRepository
    {
        public List<XElement> Elements { get; } = [];

        public IReadOnlyCollection<XElement> GetAllElements() => Elements.Select(element => new XElement(element)).ToList();

        public void StoreElement(XElement element, string friendlyName) => Elements.Add(new XElement(element));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "HemodinksAPI.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
