using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using Wolverine;

namespace TaxVision.Tasks.Application.ClientRequests.Consumers;

/// <summary>
/// El veredicto del escaneo sobre lo que subió el cliente. Un <c>fileId</c> que no es de ningún
/// pedido sale en silencio: el exchange trae los archivos de todo el monorepo.
/// </summary>
public static class ClientDocumentScanResultConsumer
{
    /// <summary>
    /// Lo que se le dice al cliente cuando su archivo no pasa. Nunca el motivo técnico: «tiene un
    /// virus» no le dice qué hacer y regala información de la infraestructura.
    /// </summary>
    private const string ClientMessage = "No pudimos procesar este archivo. Volvé a subirlo, por favor.";

    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            if (await LoadOwnerAsync(evt.TenantId, evt.FileId, requests, logger, ct) is not { } request)
                return;

            if (request.MarkDocumentAvailable(evt.FileId, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    ) =>
        await RejectAsync(
            evt.TenantId,
            evt.FileId,
            "infected",
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    ) =>
        await RejectAsync(
            evt.TenantId,
            evt.FileId,
            "blocked-by-policy",
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    /// <summary>Lo borraron en CloudStorage: el pedido no puede seguir mostrando un archivo que no está.</summary>
    public static async Task Handle(
        FileDeletedIntegrationEvent evt,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            if (await LoadOwnerAsync(evt.TenantId, evt.FileId, requests, logger, ct) is not { } request)
                return;

            if (request.MarkDocumentDetached(evt.FileId, DateTime.UtcNow))
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task RejectAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        string correlationId,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            if (await LoadOwnerAsync(tenantId, fileId, requests, logger, ct) is not { } request)
                return;

            var document = request.Documents.First(d => d.FileId == fileId && d.IsActive);
            if (!request.MarkDocumentRejected(fileId, reason, DateTime.UtcNow))
                return;

            await bus.PublishAsync(
                new ClientRequestDocumentRejectedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    ClientRequestId = request.Id,
                    CustomerId = request.CustomerId,
                    TaskId = request.TaskId,
                    FileId = fileId,
                    DisplayName = document.DisplayName,
                    Reason = reason,
                    ClientMessage = ClientMessage,
                    RequestedByUserId = request.RequestedByUserId,
                }
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// El repositorio busca sin tenant —el consumer no corre en un scope HTTP—, así que el tenant del
    /// evento se compara contra el dueño real antes de tocar nada.
    /// </summary>
    private static async Task<ClientRequest?> LoadOwnerAsync(
        Guid tenantId,
        Guid fileId,
        IClientRequestRepository requests,
        ILogger<ClientRequest> logger,
        CancellationToken ct
    )
    {
        var request = await requests.GetByDocumentFileIdAsync(fileId, ct);
        if (request is null)
            return null;

        if (request.TenantId == tenantId)
            return request;

        logger.LogWarning(
            "File {FileId} scan event carries tenant {EventTenantId} but the owning client request belongs to {OwnerTenantId} — ignored.",
            fileId,
            tenantId,
            request.TenantId
        );
        return null;
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
