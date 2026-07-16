using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RetrySaaSPayment;
using Wolverine;

namespace TaxVision.PaymentApp.Infrastructure.Scheduling;

/// <summary>
/// Reintenta cobros Failed cuyo <c>NextRetryAtUtc</c> ya llegó. El backoff (1h → 6h → 24h →
/// se abandona) vive en <c>SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc</c> — este job solo
/// encuentra los candidatos y despacha un <see cref="RetrySaaSPaymentCommand"/> por cada uno.
/// Cuando el retry agota el backoff, <c>RetrySaaSPaymentHandler</c> ya deja
/// <c>NextRetryAtUtc = null</c> (vía <c>MarkFailed(willRetry: false, ...)</c>), así que ese
/// pago simplemente deja de aparecer en la próxima corrida — Subscription se entera del
/// abandono definitivo por el <c>*PaymentFailedIntegrationEvent</c> con <c>WillRetry = false</c>.
/// </summary>
public sealed class DunningJob(IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<DunningJob> logger)
    : PeriodicPaymentAppJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(10))
{
    private const int BatchSize = 100;

    protected override string JobName => "dunning";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var payments = services.GetRequiredService<ISaaSPaymentRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<DunningJob>>();

        var due = await payments.GetDueForRetryAsync(DateTime.UtcNow, BatchSize, ct);

        foreach (var payment in due)
        {
            var result = await bus.InvokeAsync<Result>(
                new RetrySaaSPaymentCommand(payment.TenantId, payment.Id, Guid.Empty), ct);

            if (result.IsFailure)
            {
                logger.LogWarning(
                    "DunningJob retry failed for SaaSPayment {SaaSPaymentId}: {Code} — {Message}",
                    payment.Id, result.Error.Code, result.Error.Message);
            }
        }

        if (due.Count > 0)
            logger.LogInformation("DunningJob processed {Count} due retry(ies).", due.Count);
    }
}
