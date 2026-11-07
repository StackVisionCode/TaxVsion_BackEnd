using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

/// <summary>Lectura M2M para Growth (GRW-1). IgnoreQueryFilters: el pago de onboarding puede no tener tenant todavía
/// y la reserva puede ser de la plataforma.</summary>
public sealed class CodeReservationPaymentReader(PaymentAppDbContext db) : ICodeReservationPaymentReader
{
    public async Task<IReadOnlyList<CodeReservationPaymentRow>> FindByReservationAsync(
        Guid codeReservationId,
        Guid codeReservationPaymentId,
        CancellationToken ct = default
    ) =>
        await db
            .SaaSPayments.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p =>
                p.CodeReservationId == codeReservationId && p.CodeReservationPaymentId == codeReservationPaymentId
            )
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new CodeReservationPaymentRow(p.TenantId, p.Status, p.PromotionSnapshotHash))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CodeReservationPaymentRow>> FindOnboardingAsync(
        Guid onboardingId,
        CancellationToken ct = default
    ) =>
        await db
            .SaaSPayments.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Type == SaaSPaymentType.OnboardingInitial && p.OnboardingId == onboardingId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new CodeReservationPaymentRow(p.TenantId, p.Status, p.PromotionSnapshotHash))
            .ToListAsync(ct);
}
