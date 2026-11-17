using TaxVision.Growth.SharedKernel.Buyers;

namespace TaxVision.Growth.Infrastructure.Security;

/// <summary>Lista versionada en el repo (recurso incrustado), cargada una vez. Un subdominio de uno listado también cuenta.</summary>
public sealed class FileDisposableEmailDomains : IDisposableEmailDomains
{
    private static readonly Lazy<HashSet<string>> Domains = new(Load);

    public bool IsDisposable(NormalizedEmail email)
    {
        var domain = email.Domain;
        while (true)
        {
            if (Domains.Value.Contains(domain))
                return true;
            var dot = domain.IndexOf('.');
            if (dot < 0 || domain.IndexOf('.', dot + 1) < 0)
                return false;
            domain = domain[(dot + 1)..];
        }
    }

    private static HashSet<string> Load()
    {
        using var stream =
            typeof(FileDisposableEmailDomains).Assembly.GetManifestResourceStream(
                "TaxVision.Growth.DisposableEmailDomains"
            ) ?? throw new InvalidOperationException("The disposable email domain list is missing.");
        using var reader = new StreamReader(stream);
        var domains = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim().ToLowerInvariant();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                domains.Add(trimmed);
        }

        return domains;
    }
}
