namespace TaxVision.Connectors.Application.Accounts;

// Pre-flight de impacto (punto 3.2): buzones personales del empleado (se desconectan y purgan al retirarlo)
// y buzones de oficina ligados a él (se marcan para revisión, no se desconectan). El email es opcional:
// sin él solo cuenta los de oficina que creó/conectó.
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId, string? Email = null);

public sealed record OffboardingImpactResponse(int PersonalMailboxes, int OfficeMailboxes);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ITenantEmailAccountRepository accounts,
        CancellationToken ct
    )
    {
        var personalMailboxes = await accounts.CountByOwnerUserAsync(query.TenantId, query.UserId, ct);
        var tenantAccounts = await accounts.ListByTenantAsync(query.TenantId, ct);
        var officeMailboxes = tenantAccounts.Count(a =>
            a.OffboardingReviewReasonFor(query.UserId, query.Email) is not null
        );
        return new OffboardingImpactResponse(personalMailboxes, officeMailboxes);
    }
}
