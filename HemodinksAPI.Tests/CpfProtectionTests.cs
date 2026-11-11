using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
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

/// <summary>Protecao de CPF em repouso (#227).</summary>
public sealed partial class CpfProtectionTests
{
    private const string CpfA = "52998224725";
    private const string CpfB = "11144477735";

    [Fact]
    public void Persistent_protector_encrypts_key_ring_and_other_replica_can_decrypt()
    {
        using var certificate = CreateCertificate();
        var repository = new InMemoryXmlRepository();
        var hashKey = RandomNumberGenerator.GetBytes(32);

        using var replicaA = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            certificate, hashKey, repository, NullLoggerFactory.Instance);
        var protectedCpf = replicaA.Protect(CpfA);

        Assert.DoesNotContain(CpfA, protectedCpf, StringComparison.Ordinal);
        var storedKey = Assert.Single(repository.Elements).ToString();
        Assert.Contains("EncryptedData", storedKey, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", storedKey, StringComparison.Ordinal);

        using var replicaB = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            certificate, hashKey, repository, NullLoggerFactory.Instance);
        Assert.Equal(CpfA, replicaB.TryUnprotect(protectedCpf));
        Assert.Equal(replicaA.ComputeLookupHash(CpfA), replicaB.ComputeLookupHash(CpfA));
    }

    [Fact]
    public void Certificate_loaded_from_configuration_round_trips_cpf()
    {
        using var generated = CreateCertificate();
        var options = new CpfProtectionOptions
        {
            CertificateBase64 = Convert.ToBase64String(generated.Export(X509ContentType.Pkcs12, "senha-teste")),
            CertificatePassword = "senha-teste",
            LookupHashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        using var certificate = CpfProtectionKeyMaterial.LoadCertificate(options);
        var repository = new InMemoryXmlRepository();

        using var protector = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            certificate, CpfProtectionKeyMaterial.LoadLookupHashKey(options), repository, NullLoggerFactory.Instance);
        using var restarted = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            certificate, CpfProtectionKeyMaterial.LoadLookupHashKey(options), repository, NullLoggerFactory.Instance);

        Assert.Equal(CpfA, restarted.TryUnprotect(protector.Protect(CpfA)));
    }

    [Fact]
    public void Protector_with_other_certificate_cannot_read_key_ring()
    {
        using var certificate = CreateCertificate();
        using var otherCertificate = CreateCertificate();
        var repository = new InMemoryXmlRepository();
        var hashKey = RandomNumberGenerator.GetBytes(32);
        using var owner = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            certificate, hashKey, repository, NullLoggerFactory.Instance);
        var protectedCpf = owner.Protect(CpfA);

        using var intruder = CpfProtectionServiceCollectionExtensions.CreatePersistent(
            otherCertificate, hashKey, repository, NullLoggerFactory.Instance);

        Assert.Null(intruder.TryUnprotect(protectedCpf));
    }

    [Fact]
    public void Lookup_hash_is_keyed_deterministic_and_ignores_formatting()
    {
        var protector = TestCpfProtection.Instance;
        var hash = protector.ComputeLookupHash(CpfA);

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, protector.ComputeLookupHash("529.982.247-25"));
        Assert.NotEqual(hash, protector.ComputeLookupHash(CpfB));
        Assert.Null(protector.ComputeLookupHash(null));
        Assert.Null(protector.ComputeLookupHash("  "));

        var otherKey = CpfProtectionServiceCollectionExtensions.CreateEphemeral(NullLoggerFactory.Instance);
        Assert.NotEqual(hash, otherKey.ComputeLookupHash(CpfA));
        Assert.Null(protector.TryUnprotect("payload-invalido"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Confirmation")]
    public void Missing_configuration_fails_startup_outside_development(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(environment, new Dictionary<string, string?>()));
        Assert.Contains("CpfProtection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_or_weak_configuration_fails_even_in_development()
    {
        using var certificate = CreateCertificate();
        var pfx = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));

        Assert.Throws<InvalidOperationException>(() => Register("Development", new()
        {
            ["CpfProtection:CertificateBase64"] = pfx
        }));
        var weak = Assert.Throws<InvalidOperationException>(() => Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = pfx,
            ["CpfProtection:LookupHashKey"] = Convert.ToBase64String(new byte[16])
        }));
        Assert.DoesNotContain(Convert.ToBase64String(new byte[16]), weak.Message, StringComparison.Ordinal);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        Assert.Throws<InvalidOperationException>(() => Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = Convert.ToBase64String(publicOnly.Export(X509ContentType.Cert)),
            ["CpfProtection:LookupHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));
    }

    [Fact]
    public void Complete_configuration_registers_protector_in_production()
    {
        using var certificate = CreateCertificate();
        var services = Register("Production", new()
        {
            ["CpfProtection:CertificateBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12)),
            ["CpfProtection:LookupHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICpfProtector));
        var runtime = (CpfProtectionRuntimeInfo)services.Single(d => d.ServiceType == typeof(CpfProtectionRuntimeInfo)).ImplementationInstance!;
        Assert.False(runtime.UsesEphemeralKeys);
        Assert.True(runtime.ReconcileOnStartup);
    }

    [Fact]
    public async Task Saving_user_writes_cipher_and_hash_and_clearing_cpf_clears_both()
    {
        await using var context = TestDbContextFactory.Create();
        var user = NewUser(Clinica.DefaultId, "cifra@example.com", CpfA);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        Assert.NotNull(user.CpfCifrado);
        Assert.DoesNotContain(CpfA, user.CpfCifrado, StringComparison.Ordinal);
        Assert.Equal(CpfA, TestCpfProtection.Instance.TryUnprotect(user.CpfCifrado));
        Assert.Equal(TestCpfProtection.Instance.ComputeLookupHash(CpfA), user.CpfHash);

        user.Cpf = CpfB;
        await context.SaveChangesAsync();
        Assert.Equal(CpfB, TestCpfProtection.Instance.TryUnprotect(user.CpfCifrado!));
        Assert.Equal(TestCpfProtection.Instance.ComputeLookupHash(CpfB), user.CpfHash);

        user.Cpf = null;
        await context.SaveChangesAsync();
        Assert.Null(user.CpfCifrado);
        Assert.Null(user.CpfHash);
    }

    [Fact]
    public async Task Context_without_protector_refuses_to_store_cpf()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved());
        context.Users.Add(NewUser(Clinica.DefaultId, "sem-protetor@example.com", CpfA));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Throws<InvalidOperationException>(() => context.ComputeCpfLookupHash(CpfA));
    }

    [Fact]
    public async Task User_search_matches_only_full_cpf_and_never_other_clinic()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var platform = new AppDbContext(options, ClinicaContextFactory.CreatePlatform(), TestCpfProtection.Instance))
        {
            await platform.Database.EnsureCreatedAsync();
            platform.Clinicas.Add(new Clinica { Id = 2, Nome = "Clinica B", Slug = "clinica-b" });
            platform.Users.Add(NewUser(Clinica.DefaultId, "a@example.com", CpfA));
            platform.Users.Add(NewUser(2, "b@example.com", CpfB));
            await platform.SaveChangesAsync();
        }

        await using var clinicA = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved(), TestCpfProtection.Instance);
        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(2, "clinica-b");
        await using var clinicB = new AppDbContext(options, tenantB, TestCpfProtection.Instance);

        Assert.Equal(["a@example.com"], await SearchUsers(clinicA, "529.982.247-25"));
        Assert.Empty(await SearchUsers(clinicA, "52998"));
        // Clinica B nao encontra o CPF da Clinica A, nem com o numero completo.
        Assert.Empty(await SearchUsers(clinicB, CpfA));
        Assert.Equal(["b@example.com"], await SearchUsers(clinicB, CpfB));
    }

    [Fact]
    public async Task Same_cpf_is_unique_per_clinic_on_hash_index()
    {
        await using var connection = await OpenReconciliationDatabase();
        await using var context = SqliteContext(connection);
        context.Users.Add(NewUser(1, "a1@example.com", CpfA));
        context.Users.Add(NewUser(2, "b1@example.com", CpfA));
        await context.SaveChangesAsync();

        await using var duplicateContext = SqliteContext(connection);
        duplicateContext.Users.Add(NewUser(1, "a2@example.com", CpfA));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Reconciliation_fixes_legacy_stale_and_unreadable_rows_and_is_idempotent()
    {
        await using var connection = await OpenReconciliationDatabase();
        var protector = TestCpfProtection.Instance;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Users (Id, ClinicaId, Cpf, CpfCifrado, CpfHash) VALUES
                    (1, 1, '52998224725', NULL, NULL),
                    (2, 1, '11144477735', 'cifra-ilegivel', $staleHash),
                    (3, 1, NULL, 'cifra-antiga', 'hash-antigo'),
                    (4, 2, NULL, NULL, NULL);
                """;
            command.Parameters.AddWithValue("$staleHash", protector.ComputeLookupHash("39053344705"));
            await command.ExecuteNonQueryAsync();
        }

        await using var context = SqliteContext(connection);
        var first = await CpfProtectionReconciliationService.ReconcileAsync(
            context, protector, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(3, first.Updated);
        Assert.Equal(1, first.Unreadable);
        Assert.Equal(0, first.Failed);
        var rows = await context.Users.AsNoTracking().OrderBy(user => user.Id)
            .Select(user => new { user.Cpf, user.CpfCifrado, user.CpfHash }).ToListAsync();
        Assert.Equal(CpfA, protector.TryUnprotect(rows[0].CpfCifrado!));
        Assert.Equal(protector.ComputeLookupHash(CpfA), rows[0].CpfHash);
        Assert.Equal(CpfB, protector.TryUnprotect(rows[1].CpfCifrado!));
        Assert.Equal(protector.ComputeLookupHash(CpfB), rows[1].CpfHash);
        Assert.Null(rows[2].CpfCifrado);
        Assert.Null(rows[2].CpfHash);

        var second = await CpfProtectionReconciliationService.ReconcileAsync(
            context, protector, NullLogger.Instance, CancellationToken.None);
        Assert.Equal(0, second.Updated);
    }

    [Fact]
    public async Task Uniqueness_rejects_legacy_row_without_hash_and_allows_same_cpf_in_other_clinic()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var platform = new AppDbContext(options, ClinicaContextFactory.CreatePlatform(), TestCpfProtection.Instance))
        {
            await platform.Database.EnsureCreatedAsync();
            platform.Clinicas.Add(new Clinica { Id = 2, Nome = "Clinica B", Slug = "clinica-b" });
            var legacy = NewUser(Clinica.DefaultId, "legado@example.com", CpfA);
            platform.Users.Add(legacy);
            await platform.SaveChangesAsync();
            // Linha gravada por uma imagem anterior ao #227: so a coluna legada.
            legacy.CpfCifrado = null;
            legacy.CpfHash = null;
            platform.Entry(legacy).Property(user => user.Cpf).IsModified = false;
            await platform.SaveChangesAsync();
            Assert.Null((await platform.Users.AsNoTracking().SingleAsync()).CpfHash);
        }

        await using var clinicA = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved(), TestCpfProtection.Instance);
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HemodinksAPI.Application.Features.Pacientes.Commands.PacienteRules.NormalizeAndValidateCpfAsync(
                clinicA, "529.982.247-25", null, CancellationToken.None));
        Assert.Equal("CPF ja cadastrado", duplicate.Message);

        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(2, "clinica-b");
        await using var clinicB = new AppDbContext(options, tenantB, TestCpfProtection.Instance);
        Assert.Equal(CpfA, await HemodinksAPI.Application.Features.Pacientes.Commands.PacienteRules.NormalizeAndValidateCpfAsync(
            clinicB, CpfA, null, CancellationToken.None));
    }

    [Fact]
    public async Task Patient_search_matches_full_cpf_only_inside_current_clinic()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var platform = new AppDbContext(options, ClinicaContextFactory.CreatePlatform(), TestCpfProtection.Instance))
        {
            await platform.Database.EnsureCreatedAsync();
            platform.Clinicas.Add(new Clinica { Id = 2, Nome = "Clinica B", Slug = "clinica-b" });
            var userA = NewUser(Clinica.DefaultId, "pa@example.com", CpfA);
            userA.PerfilId = Perfil.PacientesId;
            platform.Users.Add(userA);
            await platform.SaveChangesAsync();
            platform.Pacientes.Add(new Paciente { ClinicaId = Clinica.DefaultId, UserId = userA.Id, NomePaciente = "Paciente A" });
            await platform.SaveChangesAsync();
        }

        await using var clinicA = new AppDbContext(options, ClinicaContextFactory.CreateDefaultResolved(), TestCpfProtection.Instance);
        var tenantB = new ClinicaContext();
        tenantB.SetCurrent(2, "clinica-b");
        await using var clinicB = new AppDbContext(options, tenantB, TestCpfProtection.Instance);

        Assert.Equal(["Paciente A"], await SearchPatients(clinicA, "529.982.247-25"));
        Assert.Empty(await SearchPatients(clinicA, "52998"));
        Assert.Empty(await SearchPatients(clinicB, CpfA));
    }

    [Fact]
    public void Application_never_filters_on_plaintext_cpf()
    {
        var root = FindRepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "HemodinksAPI.Application"), "*.cs", SearchOption.AllDirectories)
            // Unica excecao: unicidade da fase 1 compara tambem a coluna legada (ver CpfQueryExtensions); sai na fase 2.
            .Where(file => !file.EndsWith($"{Path.DirectorySeparatorChar}CpfQueryExtensions.cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(item => PlaintextCpfFilter().IsMatch(item.line)))
            .Select(item => $"{Path.GetRelativePath(root, item.file)}:{item.number}: {item.line.Trim()}")
            .ToList();

        Assert.True(violations.Count == 0,
            "Filtre CPF por CpfHash (ComputeCpfLookupHash), nunca pela coluna em texto:\n" + string.Join('\n', violations));
    }

    [GeneratedRegex(@"\.Cpf\s*(==|!=)|\.Cpf\.(Contains|StartsWith|EndsWith|Equals)\(|==\s*\w+\.Cpf\b")]
    private static partial Regex PlaintextCpfFilter();

    private static async Task<string[]> SearchUsers(AppDbContext context, string search)
    {
        var handler = new GetAllUsersQueryHandler(context, NullLogger<GetAllUsersQueryHandler>.Instance);
        var result = await handler.Handle(new GetAllUsersQuery { Search = search, PageSize = 50 }, CancellationToken.None);
        return result.Items.Select(item => item.Email).Order().ToArray();
    }

    private static async Task<string[]> SearchPatients(AppDbContext context, string search)
    {
        var digits = HemodinksAPI.Application.Features.Pacientes.Queries.PacienteFilters.GetStructuredSearchDigits(search);
        var query = HemodinksAPI.Application.Features.Pacientes.Queries.PacienteFilters.ApplyFilters(
            context.Pacientes.AsNoTracking(), search, digits, null, null, null, [], [], null, null, null, null,
            supportsFullTextSearch: false,
            cpfHash: digits.Length == 11 ? context.ComputeCpfLookupHash(digits) : null);
        return await query.Select(paciente => paciente.NomePaciente).ToArrayAsync();
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

    private static async Task<SqliteConnection> OpenReconciliationDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // Apenas as colunas usadas; o schema completo tem defaults especificos do SQL Server.
        command.CommandText = """
            CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, ClinicaId INTEGER NOT NULL,
                Nome TEXT, Email TEXT, Telefone TEXT, Senha TEXT, PerfilId INTEGER NOT NULL DEFAULT 2,
                Ativo INTEGER NOT NULL DEFAULT 1, PrecisaTrocarSenha INTEGER NOT NULL DEFAULT 1,
                DataCadastro TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, DataAtualizacao TEXT, DataNascimento TEXT,
                Crm TEXT, CrmUf TEXT, FotoPerfil TEXT, Cpf TEXT, CpfCifrado TEXT, CpfHash TEXT);
            CREATE UNIQUE INDEX IX_Users_ClinicaId_CpfHash ON Users (ClinicaId, CpfHash) WHERE CpfHash IS NOT NULL;
            """;
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static AppDbContext SqliteContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        return new AppDbContext(options, ClinicaContextFactory.CreatePlatform(), TestCpfProtection.Instance);
    }

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
        var request = new CertificateRequest("CN=hemodinks-cpf-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // Exportavel: os testes de configuracao geram o PFX em base64 a partir dele.
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HemodinksAPI.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("HemodinksAPI.slnx nao encontrado.");
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
