using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Job background que cada 30 minutos revisa solicitudes InProgress con recordatorios activos a las
/// que ya les toca un reminder según su <b>intervalo dinámico por-solicitud</b> (configurado por el
/// preparador, con default de tenant). Emite un <c>SignatureRequestReminderDueIntegrationEvent</c> por
/// firmante pendiente (Notification hace el dispatch email/SMS). La expiración corta la serie y hay un
/// cap de seguridad; la decisión de "a quién le toca" vive en el dominio (<c>IsReminderDue</c>) y se
/// refleja en la query del repositorio. Cada solicitud va en su propia unidad (BB-3): el contador y los
/// avisos se confirman juntos.
/// </summary>
public sealed class ReminderScheduler(IServiceProvider serviceProvider, ILogger<ReminderScheduler> logger)
    : BackgroundService
{
    private const string LockKey = "signature:reminder-scheduler";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Reemplaza el delay fijo anterior: espera a que el host completo (Wolverine incluido)
        // termine de arrancar — RunOnceAsync publica por bus, y correr antes revienta con
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
            logger.LogError(ex, "ReminderScheduler iteration failed.");
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
        IReadOnlyList<ReminderCandidate> candidates;
        using (var scope = serviceProvider.CreateScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<ISignaturePlatformSettingsProvider>();
            var maxReminders = (await platform.GetAsync(ct)).MaxRemindersPerRequest;
            candidates = (
                await scope
                    .ServiceProvider.GetRequiredService<ISignatureRequestRepository>()
                    .ListReminderCandidatesAsync(now, maxReminders, ct)
            )
                .Select(r => new ReminderCandidate(r.TenantId, r.Id))
                .ToList();
        }
        if (candidates.Count == 0)
            return;

        var summary = await DispatchAsync(candidates, now, ct);
        logger.LogInformation(
            "ReminderScheduler recorded reminders on {Committed} of {Requests} requests ({Conflicts} modified concurrently, {Failed} failed).",
            summary.Committed,
            candidates.Count,
            summary.Conflicts,
            summary.Failed
        );
    }

    internal sealed record ReminderCandidate(Guid TenantId, Guid SignatureRequestId);

    // SIG-BB3: una unidad por solicitud; un conflicto o un fallo no arrastra a las demás.
    internal Task<JobBatchSummary> DispatchAsync(
        IReadOnlyList<ReminderCandidate> candidates,
        DateTime now,
        CancellationToken ct
    ) =>
        TransactionalJobUnit.RunEachAsync<ReminderCandidate, SignatureDbContext>(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            candidates,
            async (candidate, unit, token) =>
            {
                var request = await unit
                    .Services.GetRequiredService<ISignatureRequestRepository>()
                    .GetByIdAsync(candidate.TenantId, candidate.SignatureRequestId, token);
                if (request is null || request.RecordReminderDispatched(now).IsFailure)
                    return false;

                var tokenService = unit.Services.GetRequiredService<ISigningTokenService>();
                foreach (var signer in PendingSigners(request))
                    await unit.PublishAsync(BuildReminderEvent(request, signer, tokenService));
                return true;
            },
            logger,
            ct
        );

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una responsabilidad
    // ------------------------------------------------------------------

    private static IEnumerable<Signer> PendingSigners(SignatureRequest request) =>
        request.Signers.Where(s => s.Status is SignerStatus.Pending or SignerStatus.InProgress);

    private static SignatureRequestReminderDueIntegrationEvent BuildReminderEvent(
        SignatureRequest request,
        Signer signer,
        ISigningTokenService tokenService
    )
    {
        var payload = new SigningTokenPayload(
            TenantId: request.TenantId,
            SignatureRequestId: request.Id,
            SignerId: signer.Id,
            RevocationEpoch: request.RevocationEpoch,
            ExpiresAtUtc: request.ExpiresAtUtc,
            TokenId: Guid.NewGuid().ToString("N")
        );
        var token = tokenService.Issue(payload);

        return new SignatureRequestReminderDueIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            Email = signer.Email.Value,
            FullName = signer.FullName.Value,
            Language = signer.Language,
            // Si no hay expiración, IsReminderDue ya filtró — nunca llegamos aquí sin valor.
            ExpiresAtUtc = request.ExpiresAtUtc!.Value,
            RemindersSent = request.RemindersSent,
            PublicToken = token,
            PhoneE164 = signer.PhoneNumber?.Value,
            PreferredChannel = SignerChannelResolver.PreferredChannelFor(signer),
            MappedCustomerId = signer.MappedCustomerId,
        };
    }
}
