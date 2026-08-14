using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;

namespace TaxVision.Tasks.Application.ClientRequests.Commands;

public enum ClientRequestResolution
{
    Accept = 1,
    Reject = 2,
    Cancel = 3,
}

public sealed record ResolveClientRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid ClientRequestId,
    ClientRequestResolution Resolution,
    string? Note
);

/// <summary>
/// Cerrar el pedido es del preparador, no del cliente: subir un archivo no es haber mandado lo que
/// se pidió. Los tres desenlaces comparten handler porque comparten la misma comprobación previa y
/// difieren en una línea.
/// </summary>
public static class ResolveClientRequestHandler
{
    public static async Task<Result<ClientRequestResponse>> Handle(
        ResolveClientRequestCommand command,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await requests.GetByIdAsync(command.TenantId, command.ClientRequestId, ct);
        if (found.IsFailure)
            return Result.Failure<ClientRequestResponse>(found.Error);

        var request = found.Value;
        var now = DateTime.UtcNow;

        var resolved = command.Resolution switch
        {
            ClientRequestResolution.Accept => request.Accept(command.ByUserId, command.Note, now),
            ClientRequestResolution.Reject => request.Reject(command.ByUserId, command.Note, now),
            _ => request.Cancel(command.ByUserId, command.Note, now),
        };
        if (resolved.IsFailure)
            return Result.Failure<ClientRequestResponse>(resolved.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ClientRequestResponse.From(request));
    }
}
