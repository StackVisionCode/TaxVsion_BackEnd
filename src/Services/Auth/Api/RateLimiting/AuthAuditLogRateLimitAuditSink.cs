using BuildingBlocks.Persistence;
using BuildingBlocks.Web.RateLimiting;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Api.RateLimiting;

/// <summary>Auditoría independiente post-Fase-9 — cierra el gap de la categoría M para Auth
/// (<c>auth.m.onboarding_admin_cancel_refund</c>): un 429 en una acción que mueve dinero es en sí
/// una señal de seguridad, no solo el intento exitoso (ver <c>IRateLimitAuditSink</c>). Reutiliza
/// <see cref="IAuthAuditWriter"/> — sin tabla nueva, mismo <c>AuthAuditLog</c> que usan
/// <c>RoleCommands</c>/<c>TenantDomains</c>/etc. A diferencia de esos call sites, acá NO hay un
/// handler downstream que llame <c>unitOfWork.SaveChangesAsync</c> después — el request se corta
/// en el filtro antes de llegar a ningún handler — así que este sink hace su propio
/// <see cref="IUnitOfWork.SaveChangesAsync"/> explícito, o el log quedaría solo en el change
/// tracker y se perdería al terminar el request.</summary>
public sealed class AuthAuditLogRateLimitAuditSink(IAuthAuditWriter audit, IUnitOfWork unitOfWork) : IRateLimitAuditSink
{
    public async Task OnBlockedAsync(RateLimitAuditContext context, CancellationToken ct = default)
    {
        await audit.AddAsync(
            AuthAuditLog.Record(
                context.TenantId,
                context.UserId,
                AuthAuditAction.RateLimitBlocked,
                false,
                context.IpAddress,
                context.UserAgent,
                context.CorrelationId,
                targetType: "RateLimitPolicy",
                detailsJson: $"{{\"policy\":\"{context.PolicyName}\"}}"
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}
