using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad para adjuntar el hash original a borradores atascados. Lo normal es que
/// el hash se adjunte al crear la solicitud (si el archivo ya está disponible) o al recibir
/// <c>FileAvailable</c>. Pero hay una CARRERA cuando el escaneo es muy rápido: el evento llega
/// buscando borradores que aún no existen y el <c>create</c> lee la proyección justo cuando el
/// consumer la actualiza → el borrador queda sin hash para siempre.
///
/// <para>
/// Este job barre periódicamente los borradores con cierta antigüedad (el corte evita pisar
/// creaciones en vuelo) cuyo archivo YA está <c>Available</c> en la proyección local y les
/// adjunta el hash. Idempotente y cross-tenant. El Status NO cambia: la solicitud sigue en
/// Draft hasta que el preparador decida enviar. Mismo patrón que <see cref="ExpirationScheduler"/>.
/// </para>
/// </summary>
public sealed class ReadyReconciliationScheduler(
    IServiceProvider serviceProvider,
    ILogger<ReadyReconciliationScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>Antigüedad mínima de un borrador para considerarlo "atascado" (no una creación en vuelo).</summary>
    private static readonly TimeSpan MinAge = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            logger.LogError(ex, "ReadyReconciliationScheduler iteration failed.");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISignatureRequestRepository>();
        var fileRepository = scope.ServiceProvider.GetRequiredService<IFileMetadataRefRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var cutoff = DateTime.UtcNow - MinAge;
        var drafts = await repository.ListStrandedDraftsAsync(cutoff, ct);
        if (drafts.Count == 0)
            return;

        var eventsToPublish = new List<SignatureRequestReadyForSendingIntegrationEvent>();
        foreach (var request in drafts)
        {
            var file = await fileRepository.GetByFileIdAsync(request.TenantId, request.OriginalFileId, ct);
            if (file is null || file.Status != FileScanStatus.Available || string.IsNullOrEmpty(file.ChecksumSha256))
                continue; // aún no disponible: sigue esperando el evento, no es un huérfano

            var hashResult = DocumentHash.Create(file.ChecksumSha256);
            if (hashResult.IsFailure)
                continue;

            var attach = request.AttachOriginalHash(hashResult.Value);
            if (attach.IsFailure)
                continue;

            eventsToPublish.Add(
                new SignatureRequestReadyForSendingIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    OriginalFileId = request.OriginalFileId,
                    DocumentHashPre = request.DocumentHashPre!.Value,
                }
            );
        }

        if (eventsToPublish.Count == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        foreach (var evt in eventsToPublish)
            await bus.PublishAsync(evt);

        logger.LogInformation(
            "ReadyReconciliationScheduler attached original hash to {Count} stranded Draft requests.",
            eventsToPublish.Count
        );
    }
}
