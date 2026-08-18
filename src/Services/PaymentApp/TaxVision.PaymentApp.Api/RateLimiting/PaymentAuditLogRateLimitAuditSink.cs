using BuildingBlocks.Persistence;
using BuildingBlocks.Web.RateLimiting;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Domain.Audit;

namespace TaxVision.PaymentApp.Api.RateLimiting;

/// <summary>Auditoría independiente post-Fase-9 — cierra el gap de la categoría M para PaymentApp
/// (<c>payment_app.m.refund</c>): un 429 en un reembolso es en sí una señal de seguridad, no solo el
/// intento exitoso (ver <c>IRateLimitAuditSink</c>). Reutiliza <see cref="AuditEntryFactory"/> —
/// mismo helper que <c>RefundSaaSPaymentHandler</c>, sin aggregate real (bloqueado antes de resolver
/// el <c>SaaSPayment</c>) así que AggregateType="RateLimitPolicy"/AggregateId=Guid.Empty. Igual que
/// su par en Auth, hace su propio <see cref="IUnitOfWork.SaveChangesAsync"/> explícito — no hay
/// ningún handler downstream que lo haga por él, el request se corta acá.</summary>
public sealed class PaymentAuditLogRateLimitAuditSink(IPaymentAuditLogWriter audit, IUnitOfWork unitOfWork)
    : IRateLimitAuditSink
{
    public async Task OnBlockedAsync(RateLimitAuditContext context, CancellationToken ct = default)
    {
        await AuditEntryFactory.AppendAsync(
            audit,
            context.TenantId,
            "RateLimitPolicy",
            Guid.Empty,
            PaymentAuditAction.RateLimitBlocked,
            context.UserId,
            context.CorrelationId,
            before: (object?)null,
            after: new { Policy = context.PolicyName },
            reason: null,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}
