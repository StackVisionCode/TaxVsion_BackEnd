using BuildingBlocks.Domain;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>Registro append-only de un intento de cobro de un <see cref="RecurringSchedule"/>
/// — una fila por intento (inicial o retry), nunca se sobreescribe. Entidad hija: su
/// configuración EF requiere <c>ValueGeneratedNever()</c>.</summary>
public sealed class RecurringPaymentExecution : BaseEntity
{
    public Guid TenantRecurringPaymentId { get; private set; }
    public Guid RecurringScheduleId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime ExecutedAtUtc { get; private set; }
    public Money AmountCharged { get; private set; } = null!;
    public bool Succeeded { get; private set; }
    public string? ProviderResponse { get; private set; }

    private RecurringPaymentExecution() { }

    public static RecurringPaymentExecution Record(
        Guid tenantRecurringPaymentId,
        Guid recurringScheduleId,
        Guid tenantId,
        Money amountCharged,
        bool succeeded,
        string? providerResponse,
        DateTime executedAtUtc
    ) =>
        new()
        {
            TenantRecurringPaymentId = tenantRecurringPaymentId,
            RecurringScheduleId = recurringScheduleId,
            TenantId = tenantId,
            ExecutedAtUtc = executedAtUtc,
            AmountCharged = amountCharged,
            Succeeded = succeeded,
            ProviderResponse = providerResponse,
        };
}
