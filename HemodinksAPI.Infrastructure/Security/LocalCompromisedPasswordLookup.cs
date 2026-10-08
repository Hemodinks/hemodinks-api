using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using HemodinksAPI.Application.Security;

namespace HemodinksAPI.Infrastructure.Security;

public sealed class LocalCompromisedPasswordLookup : ICompromisedPasswordLookup
{
    internal const string ResourceName = "Hemodinks.PasswordData.ncsc-100k.txt";
    internal const string ExpectedSha256 = "C2E5696882C603B76BB67A47EE970897E5A76FC4C3F5547ABE3D0CA340C576E0";
    private readonly Lazy<FrozenSet<string>?> passwords;

    public LocalCompromisedPasswordLookup() : this(() =>
        typeof(LocalCompromisedPasswordLookup).Assembly.GetManifestResourceStream(ResourceName)) { }

    internal LocalCompromisedPasswordLookup(Func<Stream?> openResource)
    {
        passwords = new Lazy<FrozenSet<string>?>(() => Load(openResource));
    }

    public PasswordLookupResult Check(string candidate)
    {
        var dataset = passwords.Value;
        return dataset == null ? PasswordLookupResult.Unavailable
            : dataset.Contains(candidate) ? PasswordLookupResult.Found : PasswordLookupResult.NotFound;
    }

    private static FrozenSet<string>? Load(Func<Stream?> openResource)
    {
        try
        {
            using var source = openResource();
            if (source == null) return null;
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            // Integrity of public reference data only. Candidate passwords are never cached or logged.
            if (Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))) != ExpectedSha256)
                return null;
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, new UTF8Encoding(false, true));
            var entries = new HashSet<string>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } line) entries.Add(line);
            return entries.Count >= 99000 ? entries.ToFrozenSet(StringComparer.Ordinal) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            // Do not attach the source exception: infrastructure errors need not carry sensitive context.
            return null;
        }
    }
}
