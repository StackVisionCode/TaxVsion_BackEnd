using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Job background que cada 30 minutos revisa solicitudes InProgress con recordatorios activos a las
/// que ya les toca un reminder según su <b>intervalo dinámico por-solicitud</b> (configurado por el
/// preparador, con default de tenant). Emite un <c>SignatureRequestReminderDueIntegrationEvent</c> por
/// firmante pendiente (Notification hace el dispatch email/SMS). La expiración corta la serie y hay un
/// cap de seguridad; la decisión de "a quién le toca" vive en el dominio (<c>IsReminderDue</c>) y se
/// refleja en la query del repositorio.
/// </summary>
public sealed class ReminderScheduler(IServiceProvider serviceProvider, ILogger<ReminderScheduler> logger)
    : BackgroundService
{
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

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ISigningTokenService>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTime.UtcNow;
        var candidates = await repository.ListReminderCandidatesAsync(now, ct);
        if (candidates.Count == 0)
            return;

        var events = new List<SignatureRequestReminderDueIntegrationEvent>();
        foreach (var request in candidates)
        {
            var recorded = request.RecordReminderDispatched(now);
            if (recorded.IsFailure)
                continue;

            foreach (var signer in PendingSigners(request))
                events.Add(BuildReminderEvent(request, signer, tokenService));
        }

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var evt in events)
            await bus.PublishAsync(evt);

        logger.LogInformation(
            "ReminderScheduler emitted {Count} reminder events over {Requests} requests.",
            events.Count,
            candidates.Count
        );
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una responsabilidad
    // ------------------------------------------------------------------

    private static IEnumerable<Signer> PendingSigners(SignatureRequest request) =>
        request.Signers.Where(s => s.Status == SignerStatus.Pending);

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
            ExpiresAtUtc = request.ExpiresAtUtc,
            RemindersSent = request.RemindersSent,
            PublicToken = token,
            PhoneE164 = signer.PhoneNumber?.Value,
            PreferredChannel = SignerChannelResolver.PreferredChannelFor(signer),
        };
    }
}
