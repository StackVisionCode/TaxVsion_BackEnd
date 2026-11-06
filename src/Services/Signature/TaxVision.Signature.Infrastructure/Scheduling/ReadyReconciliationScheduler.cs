using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Documents.Verification;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;

namespace TaxVision.Signature.Infrastructure.Scheduling;

/// <summary>
/// Red de seguridad para verificar el documento original de borradores atascados. Lo normal es que
/// se verifique al crear la solicitud (si el archivo ya está disponible) o al recibir
/// <c>FileAvailable</c>. Pero hay una CARRERA cuando el escaneo es muy rápido: el evento llega
/// buscando borradores que aún no existen y el <c>create</c> lee la proyección justo cuando el
/// consumer la actualiza → el borrador queda sin hash para siempre.
///
/// <para>
/// Este job barre periódicamente los borradores con cierta antigüedad (el corte evita pisar
/// creaciones en vuelo) con documentos pendientes cuyo archivo YA está <c>Available</c> en la
/// proyección local y los revisa en el servidor (verificado o rechazado). Idempotente y cross-tenant. El Status NO cambia: la solicitud sigue en
/// Draft hasta que el preparador decida enviar. Mismo patrón que <see cref="ExpirationScheduler"/>.
/// </para>
/// <para>
/// SIG-BB3: por borrador, primero se bajan de CloudStorage los PDF que la verificación necesita (fuera de la
/// transacción) y después una unidad verifica, guarda y avisa junto.
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
        var cutoff = DateTime.UtcNow - MinAge;
        IReadOnlyList<StrandedDraft> drafts;
        using (var scope = serviceProvider.CreateScope())
            drafts = (
                await scope
                    .ServiceProvider.GetRequiredService<ISignatureRequestRepository>()
                    .ListStrandedDraftsAsync(cutoff, ct)
            )
                .Where(request => request.Documents.Count > 0)
                .Select(request => new StrandedDraft(request.TenantId, request.Id))
                .ToList();
        if (drafts.Count == 0)
            return;

        var announced = await ReconcileAsync(drafts, ct);
        if (announced > 0)
            logger.LogInformation(
                "ReadyReconciliationScheduler verified the documents of {Count} stranded Draft requests.",
                announced
            );
    }

    internal sealed record StrandedDraft(Guid TenantId, Guid SignatureRequestId);

    /// <summary>Una unidad por borrador; devuelve cuántos quedaron listos y se avisaron.</summary>
    internal async Task<int> ReconcileAsync(IReadOnlyList<StrandedDraft> drafts, CancellationToken ct)
    {
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var announced = 0;
        foreach (var draft in drafts)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyDictionary<Guid, byte[]> downloads;
            try
            {
                downloads = await PrefetchDownloadsAsync(scopeFactory, draft, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ReadyReconciliationScheduler could not prepare request {RequestId}.", draft.SignatureRequestId);
                continue;
            }

            var ready = false;
            var result = await TransactionalJobUnit.RunAsync<SignatureDbContext>(
                scopeFactory,
                async (unit, token) =>
                {
                    ready = false;
                    var request = await unit
                        .Services.GetRequiredService<ISignatureRequestRepository>()
                        .GetByIdAsync(draft.TenantId, draft.SignatureRequestId, token);
                    if (request is not { Status: SignatureRequestStatus.Draft } || request.Documents.Count == 0)
                        return false;

                    // El verificador de siempre, pero con CloudStorage servido desde lo ya bajado.
                    var verifier = ActivatorUtilities.CreateInstance<RequestDocumentVerifier>(
                        unit.Services,
                        (ISignatureCloudStorageClient)new PrefetchedDocumentStorage(downloads)
                    );
                    var touched = false;
                    foreach (var document in PendingDocuments(request))
                        touched |=
                            (await verifier.VerifyAsync(request, document.Id, token)).Kind
                            != DocumentVerificationKind.Deferred;
                    if (!touched)
                        return false;

                    // También se guardan los rechazos, para que el borrador deje de figurar como pendiente.
                    if (request.Documents.All(document => document.DocumentHashPre is not null))
                    {
                        await unit.PublishAsync(BuildReadyEvent(request));
                        ready = true;
                    }
                    return true;
                },
                logger,
                ct
            );
            if (result.Outcome == JobUnitOutcome.Committed && ready)
                announced++;
        }

        return announced;
    }

    // Fuera de la transacción: baja de CloudStorage los PDF pendientes que no tienen una inspección guardada.
    private static async Task<IReadOnlyDictionary<Guid, byte[]>> PrefetchDownloadsAsync(
        IServiceScopeFactory scopeFactory,
        StrandedDraft draft,
        CancellationToken ct
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var request = await services
            .GetRequiredService<ISignatureRequestRepository>()
            .GetByIdAsync(draft.TenantId, draft.SignatureRequestId, ct);
        var downloads = new Dictionary<Guid, byte[]>();
        if (request is not { Status: SignatureRequestStatus.Draft })
            return downloads;

        var files = services.GetRequiredService<IFileMetadataRefRepository>();
        var validations = services.GetRequiredService<IDocumentValidationRepository>();
        var storage = services.GetRequiredService<ISignatureCloudStorageClient>();
        foreach (var document in PendingDocuments(request))
        {
            var file = await files.GetByFileIdAsync(request.TenantId, document.OriginalFileId, ct);
            if (file is not { Status: FileScanStatus.Available } || string.IsNullOrEmpty(file.ChecksumSha256))
                continue;
            var known = await validations.FindInspectedAsync(request.TenantId, file.ChecksumSha256, ct);
            if (known?.PageCount is not null)
                continue;

            var download = await storage.DownloadAsync(request.TenantId, document.OriginalFileId, ct);
            if (download.IsSuccess)
                downloads[document.OriginalFileId] = download.Value;
        }

        return downloads;
    }

    private static List<RequestDocument> PendingDocuments(SignatureRequest request) =>
        request
            .Documents.Where(document => document.VerificationStatus == DocumentVerificationStatus.Pending)
            .ToList();

    private static SignatureRequestReadyForSendingIntegrationEvent BuildReadyEvent(SignatureRequest request) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = Guid.NewGuid().ToString("N"),
            SignatureRequestId = request.Id,
            CreatedByUserId = request.CreatedByUserId,
            Documents = request
                .Documents.Where(document => document.DocumentHashPre is not null)
                .Select(document => new DocumentHashDescriptor(
                    document.Id,
                    document.OriginalFileId,
                    document.DocumentHashPre!.Value
                ))
                .ToList(),
        };
}
