using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Job background que cada 15 minutos expira las solicitudes cuyo <c>ExpiresAtUtc</c>
/// ya pasó. Fases explícitas por método privado:
/// <list type="number">
///   <item>Consultar candidatos (usa <c>IgnoreQueryFilters</c> — es un scan cross-tenant).</item>
///   <item>Para cada uno: aplicar <see cref="SignatureRequest.MarkExpired"/> y publicar
///     el evento correspondiente.</item>
///   <item>Persistir en un único SaveChanges por lote.</item>
/// </list>
/// </summary>
public sealed class ExpirationScheduler(IServiceProvider serviceProvider, ILogger<ExpirationScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExpirationScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTime.UtcNow;
        var candidates = await repository.ListExpiredCandidatesAsync(now, ct);
        if (candidates.Count == 0)
            return;

        var eventsToPublish = new List<SignatureRequestExpiredIntegrationEvent>(candidates.Count);
        foreach (var request in candidates)
        {
            var pending = request.Signers.Where(s => s.Status == SignerStatus.Pending).Select(s => s.Id).ToList();

            var result = request.MarkExpired(now);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "ExpirationScheduler could not expire request {RequestId}: {Error}",
                    request.Id,
                    result.Error.Message
                );
                continue;
            }

            eventsToPublish.Add(
                new SignatureRequestExpiredIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    SignatureRequestId = request.Id,
                    ExpiredAtUtc = now,
                    RevocationEpoch = request.RevocationEpoch,
                    PendingSignerIds = pending,
                }
            );
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var evt in eventsToPublish)
            await bus.PublishAsync(evt);

        logger.LogInformation("ExpirationScheduler expired {Count} requests.", eventsToPublish.Count);
    }
}
