using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Job background que cada 15 minutos expira las solicitudes cuyo <c>ExpiresAtUtc</c>
/// ya pasó. Fases explícitas por método privado:
/// <list type="number">
///   <item>Consultar candidatos (usa <c>IgnoreQueryFilters</c> — es un scan cross-tenant).</item>
///   <item>Para cada uno, en su propia unidad (BB-3): aplicar <see cref="SignatureRequest.MarkExpired"/>,
///     publicar el evento y confirmar los dos juntos.</item>
/// </list>
/// </summary>
public sealed class ExpirationScheduler(IServiceProvider serviceProvider, ILogger<ExpirationScheduler> logger)
    : BackgroundService
{
    private const string LockKey = "signature:expiration-scheduler";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reemplaza el delay fijo anterior: espera a que el host completo (Wolverine incluido)
        // termine de arrancar, en vez de asumir un minuto siempre alcanza — RunOnceAsync
        // publica por bus, y correr antes de que Wolverine inicie revienta con
        // WolverineHasNotStartedException.
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

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

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        // SIG-15: una sola réplica por pasada; la otra sale y retoma en su próximo tick.
        await using var handle = await serviceProvider
            .GetRequiredService<IDistributedLock>()
            .AcquireAsync(LockKey, Interval, ct);
        if (!handle.IsAcquired)
            return;

        var now = DateTime.UtcNow;
        IReadOnlyList<ExpirationCandidate> candidates;
        using (var scope = serviceProvider.CreateScope())
            candidates = (
                await scope
                    .ServiceProvider.GetRequiredService<ISignatureRequestRepository>()
                    .ListExpiredCandidatesAsync(now, ct)
            )
                .Select(r => new ExpirationCandidate(r.TenantId, r.Id))
                .ToList();
        if (candidates.Count == 0)
            return;

        var summary = await ExpireAsync(candidates, now, ct);
        logger.LogInformation(
            "ExpirationScheduler expired {Committed} requests ({Conflicts} modified concurrently, {Failed} failed).",
            summary.Committed,
            summary.Conflicts,
            summary.Failed
        );
    }

    internal sealed record ExpirationCandidate(Guid TenantId, Guid SignatureRequestId);

    // SIG-BB3: una unidad por solicitud; la marca y el aviso se confirman juntos. Un conflicto (alguien firmó
    // o rechazó mientras tanto) no arrastra a las demás y la vuelta siguiente lo reevalúa.
    internal Task<JobBatchSummary> ExpireAsync(
        IReadOnlyList<ExpirationCandidate> candidates,
        DateTime now,
        CancellationToken ct
    ) =>
        TransactionalJobUnit.RunEachAsync<ExpirationCandidate, SignatureDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            candidates,
            async (candidate, unit, token) =>
            {
                var request = await unit
                    .Services.GetRequiredService<ISignatureRequestRepository>()
                    .GetByIdAsync(candidate.TenantId, candidate.SignatureRequestId, token);
                if (
                    request is null
                    || request.Status != SignatureRequestStatus.InProgress
                    || request.ExpiresAtUtc > now
                )
                    return false;

                var pending = request
                    .Signers.Where(s => s.Status is SignerStatus.Pending or SignerStatus.InProgress)
                    .ToList();
                var result = request.MarkExpired(now);
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "ExpirationScheduler could not expire request {RequestId}: {Error}",
                        request.Id,
                        result.Error.Message
                    );
                    return false;
                }

                await unit.PublishAsync(BuildExpiredEvent(request, pending, now));
                return true;
            },
            logger,
            ct
        );

    private static SignatureRequestExpiredIntegrationEvent BuildExpiredEvent(
        SignatureRequest request,
        IReadOnlyList<Signer> pending,
        DateTime now
    ) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignatureRequestId = request.Id,
            CreatedByUserId = request.CreatedByUserId,
            ExpiredAtUtc = now,
            RevocationEpoch = request.RevocationEpoch,
            PendingSignerIds = pending.Select(s => s.Id).ToList(),
            PendingSigners = pending
                .Select(s => new SignerContactSnapshot(
                    s.Id,
                    s.Email.Value,
                    s.FullName.Value,
                    s.Language,
                    s.Order,
                    s.MappedCustomerId,
                    s.PhoneNumber?.Value,
                    SignerChannelResolver.PreferredChannelFor(s)
                ))
                .ToList(),
            ExternalReference = request.ExternalReference.ToMessaging(),
        };
}
