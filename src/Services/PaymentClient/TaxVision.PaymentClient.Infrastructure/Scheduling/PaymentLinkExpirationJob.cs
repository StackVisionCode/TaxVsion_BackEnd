using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.ExpirePaymentLink;
using Wolverine;

namespace TaxVision.PaymentClient.Infrastructure.Scheduling;

/// <summary>
/// Barre <c>PaymentLink</c>s <c>Active</c> cuyo <c>ExpiresAtUtc</c> ya pasó y los transiciona
/// a <c>Expired</c> — cierra la ventana en la que un link vencido sigue apareciendo como
/// "Active" en la fila hasta que alguien intenta redimirlo (el chequeo en tiempo real de
/// <see cref="Domain.PaymentLinks.PaymentLink.IsRedeemable"/> ya lo bloquea, este job solo
/// mantiene el estado persistido honesto para las queries del tenant).
/// </summary>
public sealed class PaymentLinkExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<PaymentLinkExpirationJob> logger
) : PeriodicPaymentClientJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(10))
{
    private const int BatchSize = 200;

    protected override string JobName => "payment-link-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var links = services.GetRequiredService<IPaymentLinkRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<PaymentLinkExpirationJob>>();

        var expired = await links.GetActiveExpiredBeforeAsync(DateTime.UtcNow, BatchSize, ct);

        foreach (var link in expired)
        {
            // RBAC Fase 5 — bus.InvokeAsync crea un scope Wolverine nuevo; sin este stamp
            // LocalCommandTenantMiddleware no tiene tenant que restaurar y el filtro
            // fail-closed de PaymentClientDbContext bloquearía el handler.
            bus.TenantId = link.TenantId.ToString();
            var result = await bus.InvokeAsync<Result>(new ExpirePaymentLinkCommand(link.TenantId, link.Id), ct);

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "PaymentLinkExpirationJob failed to expire PaymentLink {PaymentLinkId}: {Code} — {Message}",
                    link.Id,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }

        if (expired.Count > 0)
            logger.LogInformation("PaymentLinkExpirationJob expired {Count} link(s).", expired.Count);
    }
}
