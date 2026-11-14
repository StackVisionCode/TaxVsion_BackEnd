using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.FinancialInsights.Domain.Imports;

/// <summary>
/// Cupo de IA consumido por un documento en un mes. Se reserva antes de llamar al modelo y nunca se repite para el
/// mismo documento: así un reintento automático no vuelve a cobrar.
/// </summary>
public sealed class AiProcessingReservation : AggregateRoot, ITenantOwned
{
    private AiProcessingReservation() { }

    public Guid ImportDocumentId { get; private set; }
    public DateTime MonthStartUtc { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static DateTime MonthStart(DateTime nowUtc) => new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Result<AiProcessingReservation> Create(Guid tenantId, Guid documentId, string? model, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<AiProcessingReservation>(FinancialInsightsErrors.TenantRequired);
        if (documentId == Guid.Empty)
            return Result.Failure<AiProcessingReservation>(FinancialInsightsErrors.DocumentRequired);
        if (string.IsNullOrWhiteSpace(model))
            return Result.Failure<AiProcessingReservation>(FinancialInsightsErrors.ReservationModelRequired);

        var reservation = new AiProcessingReservation
        {
            Id = Guid.NewGuid(),
            ImportDocumentId = documentId,
            Model = model.Trim(),
            MonthStartUtc = MonthStart(nowUtc),
            ReservedAtUtc = nowUtc,
        };
        reservation.SetTenant(tenantId);
        return Result.Success(reservation);
    }

    public Result RecordUsage(int inputTokens, int outputTokens)
    {
        if (CompletedAtUtc is not null)
            return Result.Failure(FinancialInsightsErrors.ReservationAlreadyCompleted);
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        CompletedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public new Result SetTenant(Guid tenantId)
    {
        var allowed = TenantGuard.CanAssign(TenantId, tenantId);
        if (allowed.IsSuccess)
            base.SetTenant(tenantId);
        return allowed;
    }

    void ITenantOwned.SetTenant(Guid tenantId) => SetTenant(tenantId);
}
