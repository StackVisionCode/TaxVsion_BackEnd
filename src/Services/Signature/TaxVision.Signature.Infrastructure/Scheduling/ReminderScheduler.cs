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
/// Job background que cada 30 minutos revisa solicitudes InProgress cuya expiración
/// se acerca y aún tienen firmantes pendientes. Emite un
/// <c>SignatureRequestReminderDueIntegrationEvent</c> por firmante pendiente (Notification
/// se encarga del dispatch). Aplica cooldown por solicitud y un cap total de reminders
/// para no spammear.
/// </summary>
public sealed class ReminderScheduler(IServiceProvider serviceProvider, ILogger<ReminderScheduler> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ExpiryWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(12);
    private const int MaxRemindersPerRequest = 3;

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
        var candidates = await repository.ListReminderCandidatesAsync(
            now,
            ExpiryWindow,
            Cooldown,
            MaxRemindersPerRequest,
            ct
        );
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
        var publicUrl = tokenService.BuildPublicUrl(token);

        return new SignatureRequestReminderDueIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignatureRequestId = request.Id,
            SignerId = signer.Id,
            Email = signer.Email.Value,
            FullName = signer.FullName.Value,
            Language = "En",
            ExpiresAtUtc = request.ExpiresAtUtc,
            RemindersSent = request.RemindersSent,
            PublicUrl = publicUrl,
        };
    }
}
