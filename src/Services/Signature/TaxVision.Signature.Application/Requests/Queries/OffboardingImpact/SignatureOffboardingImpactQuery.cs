using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Queries.OffboardingImpact;

// Pre-flight del retiro: lo que pasa en Signature al retirar a este empleado (el CRM lo junta en el diálogo).
public sealed record SignatureOffboardingImpactQuery(Guid TenantId, Guid UserId);

/// <param name="PendingPreparerSignatures">Enviadas donde es preparador sin firmar: pasan al sucesor o avisan a los admins.</param>
/// <param name="PreparerOnDrafts">Borradores donde es preparador: se le quita y se avisa al autor.</param>
/// <param name="DraftRequests">Sin enviar escritas por él: pasan al sucesor o se descartan.</param>
public sealed record SignatureOffboardingImpactResponse(
    int PendingPreparerSignatures,
    int PreparerOnDrafts,
    int DraftRequests
);

public static class SignatureOffboardingImpactHandler
{
    public static async Task<SignatureOffboardingImpactResponse> Handle(
        SignatureOffboardingImpactQuery query,
        ISignatureRequestStaffWorkRepository staffWork,
        CancellationToken ct
    )
    {
        var counts = await staffWork.CountOffboardingImpactAsync(query.TenantId, query.UserId, ct);
        return new SignatureOffboardingImpactResponse(
            counts.PendingPreparerSignatures,
            counts.PreparerOnDrafts,
            counts.DraftRequests
        );
    }
}
