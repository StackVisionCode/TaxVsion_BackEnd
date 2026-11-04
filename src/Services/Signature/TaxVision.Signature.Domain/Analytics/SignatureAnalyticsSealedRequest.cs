using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Analytics;

/// <summary>
/// Marca de que una solicitud ya sumó en <see cref="SignatureAnalyticsSnapshot.RequestsSealed"/>. El sellado
/// de una solicitud puede completarse más de una vez (re-sellado tras un rechazo de CloudStorage), pero en
/// las métricas cuenta una sola: una fila por (tenant, solicitud), con índice único.
/// </summary>
public sealed class SignatureAnalyticsSealedRequest : TenantEntity
{
    private SignatureAnalyticsSealedRequest() { }

    public Guid SignatureRequestId { get; private set; }
    public DateTime CountedAtUtc { get; private set; }

    public static SignatureAnalyticsSealedRequest Create(Guid tenantId, Guid signatureRequestId)
    {
        var mark = new SignatureAnalyticsSealedRequest
        {
            Id = Guid.NewGuid(),
            SignatureRequestId = signatureRequestId,
            CountedAtUtc = DateTime.UtcNow,
        };
        mark.SetTenant(tenantId);
        return mark;
    }
}
