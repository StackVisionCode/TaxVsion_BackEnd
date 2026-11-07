using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Tasks.Application.ClientRequests.Commands;

/// <param name="CustomerId">
/// Del token del portal, nunca de la petición: si lo mandara el cliente, cambiar un id en la llamada
/// bastaría para subir documentos al expediente de otro.
/// </param>
public sealed record SubmitClientDocumentCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ClientRequestId,
    Guid FileId,
    string? DisplayName,
    string? ContentType,
    long SizeBytes
);

/// <summary>
/// El cliente registra el archivo que acaba de subir a CloudStorage. El pedido pasa a
/// <c>Submitted</c> y el preparador recibe el aviso; nadie da nada por bueno todavía.
/// TSK-7: solo lee, sin transacción. La escritura va en <see cref="RecordClientDocumentHandler"/>.
/// </summary>
public static class SubmitClientDocumentHandler
{
    [NonTransactional]
    public static async Task<Result<PortalClientRequestResponse>> Handle(
        SubmitClientDocumentCommand command,
        IClientRequestRepository requests,
        ITaskFileScanStatusClient files,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var owned = await ClientDocumentOwnership.CheckAsync(command, requests, ct);
        if (owned.IsFailure)
            return Result.Failure<PortalClientRequestResponse>(owned.Error);

        // El portal registra el documento cuando el escaneo ya terminó: ese veredicto se publicó antes
        // y no se republica, así que sin preguntarlo el documento quedaba esperando para siempre.
        var status = await files.GetStatusAsync(command.TenantId, command.FileId, ct);
        return await bus.InvokeAsync<Result<PortalClientRequestResponse>>(
            new RecordClientDocumentCommand(command, status),
            ct
        );
    }
}

internal static class ClientDocumentOwnership
{
    public static async Task<Result<ClientRequest>> CheckAsync(
        SubmitClientDocumentCommand command,
        IClientRequestRepository requests,
        CancellationToken ct
    )
    {
        var found = await requests.GetByIdAsync(command.TenantId, command.ClientRequestId, ct);
        if (found.IsFailure)
            return found;

        // El pedido de otro cliente responde «no existe»: confirmar que existe ya sería una fuga.
        return found.Value.CustomerId == command.CustomerId
            ? found
            : Result.Failure<ClientRequest>(ClientRequestErrors.NotYours);
    }
}
