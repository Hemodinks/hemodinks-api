using System.Text.RegularExpressions;

namespace HemodinksAPI.Tests;

/// <summary>
/// Trava novos usos de IgnoreQueryFilters no código de produção (issue #225).
/// O filtro global de tenant é fail-closed; contorná-lo só é aceito nos pontos abaixo,
/// cada um com o motivo documentado. Para incluir um novo uso, justifique no PR e acrescente aqui.
/// </summary>
public sealed partial class IgnoreQueryFiltersAllowlistTests
{
    private static readonly IReadOnlyDictionary<string, AllowedUsage> Allowlist =
        new Dictionary<string, AllowedUsage>(StringComparer.Ordinal)
        {
            ["HemodinksAPI.Infrastructure/Data/AppDbContext.cs"] = new(1,
                "PrincipalExistsInClinica: o bloqueio de FK entre clínicas precisa enxergar o registro principal "
                + "para confirmar que ele pertence à mesma ClinicaId; a própria consulta filtra por ClinicaId."),
            ["HemodinksAPI.Application/Features/Users/Commands/ResolveLoginClinicsCommandHandler.cs"] = new(3,
                "Login anônimo: ainda não existe ClinicaId no contexto. Busca usuários ativos pelo e-mail, os vínculos "
                + "desses usuários e as credenciais temporárias válidas, para listar as clínicas em que a senha confere."),
            ["HemodinksAPI.Application/Features/Users/Commands/TemporaryAccessService.cs"] = new(3,
                "Senha temporária é global: verificar se o alvo é SuperAdministrador em qualquer clínica (sem expor dados) "
                + "e, ao concluir a troca, liberar PrecisaTrocarSenha em todos os vínculos da mesma identidade global."),
            ["HemodinksAPI.Application/Features/Users/Commands/PasswordCommandMutations.cs"] = new(2,
                "Revogação global de credenciais: invalida tokens de redefinição de todos os vínculos da mesma "
                + "identidade global, restrito por UsuarioGlobalId."),
        };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "Migrations", "TestResults", "artifacts", "logs", "out", "node_modules"
    };

    [Fact]
    public void Production_code_only_uses_IgnoreQueryFilters_in_documented_places()
    {
        var root = FindRepositoryRoot();
        var usages = ProductionSourceFiles(root)
            .ToDictionary(
                file => Path.GetRelativePath(root, file).Replace('\\', '/'),
                file => CountUsages(File.ReadAllText(file)),
                StringComparer.Ordinal);

        var violations = FindViolations(usages);

        Assert.True(violations.Count == 0,
            "IgnoreQueryFilters fora da lista permitida (ver IgnoreQueryFiltersAllowlistTests):\n"
            + string.Join('\n', violations));
    }

    [Fact]
    public void Every_allowed_usage_has_a_documented_reason()
    {
        Assert.All(Allowlist, entry =>
        {
            Assert.True(entry.Value.Count > 0, entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.Reason), entry.Key);
        });
    }

    [Fact]
    public void Detects_usage_in_a_file_outside_the_allowlist()
    {
        var violations = FindViolations(new Dictionary<string, int>
        {
            ["HemodinksAPI.Application/Features/Pacientes/Queries/NovaConsulta.cs"] = 1
        });

        Assert.Contains(violations, item => item.Contains("NovaConsulta.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Detects_an_extra_usage_in_an_allowed_file()
    {
        var usages = Allowlist.ToDictionary(entry => entry.Key, entry => entry.Value.Count);
        usages["HemodinksAPI.Infrastructure/Data/AppDbContext.cs"] = 2;

        var violations = FindViolations(usages);

        Assert.Single(violations);
        Assert.Contains("AppDbContext.cs", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Detects_a_stale_allowlist_entry()
    {
        var usages = Allowlist.ToDictionary(entry => entry.Key, entry => entry.Value.Count);
        usages["HemodinksAPI.Application/Features/Users/Commands/PasswordCommandMutations.cs"] = 0;

        Assert.Single(FindViolations(usages));
    }

    [Theory]
    [InlineData("context.Users.IgnoreQueryFilters().Where(x => x.Ativo)", 1)]
    [InlineData("context.Users\n    .IgnoreQueryFilters ()\n    .AsNoTracking()", 1)]
    [InlineData("a.IgnoreQueryFilters().Any(b => c.IgnoreQueryFilters().Any())", 2)]
    [InlineData("context.Users.AsNoTracking().Where(x => x.Ativo)", 0)]
    [InlineData("context.Users.IgnoreQueryFilters([\"Tenant\"])", 1)]
    public void Counts_usages_in_source_text(string source, int expected)
    {
        Assert.Equal(expected, CountUsages(source));
    }

    private static List<string> FindViolations(IReadOnlyDictionary<string, int> usages)
    {
        var violations = new List<string>();
        foreach (var (file, count) in usages.Where(item => item.Value > 0).OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!Allowlist.TryGetValue(file, out var allowed))
            {
                violations.Add($"- {file}: {count} uso(s) não listado(s).");
            }
            else if (count != allowed.Count)
            {
                violations.Add($"- {file}: {count} uso(s), lista permite {allowed.Count}.");
            }
        }

        foreach (var file in Allowlist.Keys.Where(file => !usages.TryGetValue(file, out var count) || count == 0).Order(StringComparer.Ordinal))
        {
            violations.Add($"- {file}: listado, mas sem uso; remova da lista.");
        }

        return violations;
    }

    private static int CountUsages(string source) => UsagePattern().Matches(source).Count;

    private static IEnumerable<string> ProductionSourceFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .SkipLast(1)
                .Any(segment => segment.StartsWith('.')
                    || segment.EndsWith(".Tests", StringComparison.Ordinal)
                    || ExcludedDirectories.Contains(segment)));
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

        throw new InvalidOperationException("HemodinksAPI.slnx não encontrado a partir de " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\(")]
    private static partial Regex UsagePattern();

    private sealed record AllowedUsage(int Count, string Reason);
}
