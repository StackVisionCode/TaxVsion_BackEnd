using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Application.Terms.Queries;

/// <summary>Fase L1.4 — usado por el frontend para decidir si mostrar el banner de aceptacion antes de que TermsAcceptanceMiddleware bloquee con 409.</summary>
public sealed record TermsAcceptanceStatusResponse(
    bool Accepted,
    string CurrentVersion,
    string? AcceptedVersion,
    DateTime? AcceptedAtUtc
);

public sealed record GetTermsAcceptanceStatusQuery(Guid TenantId);

/// <summary>
/// PayFlow Fase 6 (retrofit): la version vigente ahora se resuelve contra Onboarding.TermsVersions
/// (Kind=TermsOfService, Locale="en-US"), no contra TermsOptions.CurrentVersion — ver el
/// doc-comment de AcceptTermsHandler para el porque.
/// </summary>
public static class GetTermsAcceptanceStatusHandler
{
    private const string DefaultLocale = "en-US";

    public static async Task<TermsAcceptanceStatusResponse> Handle(
        GetTermsAcceptanceStatusQuery query,
        ITenantTermsAcceptanceRepository acceptances,
        ITermsVersionRepository termsVersions,
        CancellationToken ct
    )
    {
        var currentVersion =
            await termsVersions.GetCurrentAsync(TermsKind.TermsOfService, DefaultLocale, DateTime.UtcNow, ct)
            ?? throw new InvalidOperationException(
                "No TermsVersion is published for TermsOfService/en-US — the Fase 6 retrofit migration should have seeded a legacy row."
            );

        var latest = await acceptances.GetLatestAsync(query.TenantId, ct);
        return new TermsAcceptanceStatusResponse(
            latest?.TermsVersion == currentVersion.Version,
            currentVersion.Version,
            latest?.TermsVersion,
            latest?.AcceptedAtUtc
        );
    }
}
