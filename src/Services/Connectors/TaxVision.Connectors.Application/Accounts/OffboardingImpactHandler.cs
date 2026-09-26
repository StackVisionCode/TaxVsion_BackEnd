namespace TaxVision.Connectors.Application.Accounts;

// Pre-flight de impacto (punto 3.2): cuántos buzones personales tiene este empleado (se desconectan y
// se purgan sus secretos al retirarlo).
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int PersonalMailboxes);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        ITenantEmailAccountRepository accounts,
        CancellationToken ct
    )
    {
        var personalMailboxes = await accounts.CountByOwnerUserAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(personalMailboxes);
    }
}
