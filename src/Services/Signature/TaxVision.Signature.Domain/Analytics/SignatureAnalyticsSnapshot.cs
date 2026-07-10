using BuildingBlocks.Domain;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Domain.Analytics;

/// <summary>
/// Contador diario por tenant y categoría del volumen de firmas. Es un read model que
/// alimentan los consumers de los propios eventos de Signature — no es un aggregate root
/// mutable por el resto del sistema. La granularidad es por día UTC para permitir
/// series de tiempo y consolidaciones semanales/mensuales en el read service.
///
/// <para>
/// Cada método de mutación representa una regla concreta (SRP): no hay <c>Update(patch)</c>.
/// Los contadores nunca disminuyen; los eventos de dominio son append-only por naturaleza,
/// así que el snapshot solo suma.
/// </para>
/// </summary>
public sealed class SignatureAnalyticsSnapshot : TenantEntity
{
    private SignatureAnalyticsSnapshot() { }

    public DateOnly Day { get; private set; }
    public SignatureCategory Category { get; private set; }

    public int RequestsCreated { get; private set; }
    public int RequestsSent { get; private set; }
    public int RequestsCanceled { get; private set; }
    public int RequestsExpired { get; private set; }
    public int RequestsCompleted { get; private set; }
    public int RequestsSealed { get; private set; }
    public int SignersSigned { get; private set; }
    public int SignersRejected { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static SignatureAnalyticsSnapshot CreateEmpty(Guid tenantId, DateOnly day, SignatureCategory category)
    {
        var snapshot = new SignatureAnalyticsSnapshot
        {
            Id = Guid.NewGuid(),
            Day = day,
            Category = category,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        snapshot.SetTenant(tenantId);
        return snapshot;
    }

    public void IncrementCreated() => IncrementCounter(() => RequestsCreated++);

    public void IncrementSent() => IncrementCounter(() => RequestsSent++);

    public void IncrementCanceled() => IncrementCounter(() => RequestsCanceled++);

    public void IncrementExpired() => IncrementCounter(() => RequestsExpired++);

    public void IncrementCompleted() => IncrementCounter(() => RequestsCompleted++);

    public void IncrementSealed() => IncrementCounter(() => RequestsSealed++);

    public void IncrementSignersSigned() => IncrementCounter(() => SignersSigned++);

    public void IncrementSignersRejected() => IncrementCounter(() => SignersRejected++);

    private void IncrementCounter(Action mutate)
    {
        mutate();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
