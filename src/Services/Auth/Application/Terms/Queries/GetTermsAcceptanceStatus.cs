using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Terms.Queries;

/// <summary>Fase L1.4 — usado por el frontend para decidir si mostrar el banner de aceptacion antes de que TermsAcceptanceMiddleware bloquee con 409.</summary>
public sealed record TermsAcceptanceStatusResponse(
    bool Accepted,
    string CurrentVersion,
    string? AcceptedVersion,
    DateTime? AcceptedAtUtc
);

public sealed record GetTermsAcceptanceStatusQuery(Guid TenantId);

public static class GetTermsAcceptanceStatusHandler
{
    public static async Task<TermsAcceptanceStatusResponse> Handle(
        GetTermsAcceptanceStatusQuery query,
        ITenantTermsAcceptanceRepository acceptances,
        IOptions<TermsOptions> options,
        CancellationToken ct
    )
    {
        var currentVersion = options.Value.CurrentVersion;
        var latest = await acceptances.GetLatestAsync(query.TenantId, ct);
        return new TermsAcceptanceStatusResponse(
            latest?.TermsVersion == currentVersion,
            currentVersion,
            latest?.TermsVersion,
            latest?.AcceptedAtUtc
        );
    }
}
