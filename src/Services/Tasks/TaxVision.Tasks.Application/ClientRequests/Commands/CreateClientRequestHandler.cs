using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.ClientRequests.Abstractions;
using TaxVision.Tasks.Domain.ClientRequests;
using Wolverine;

namespace TaxVision.Tasks.Application.ClientRequests.Commands;

public sealed record CreateClientRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid CustomerId,
    Guid? TaskId,
    string? Title,
    string? Details,
    DateTime? DueAtUtc
);

/// <summary>
/// La firma le pide algo al cliente. El título va en el idioma del cliente: es lo que va a leer en
/// su lista y en el correo.
/// </summary>
public static class CreateClientRequestHandler
{
    public static async Task<Result<ClientRequestResponse>> Handle(
        CreateClientRequestCommand command,
        IClientRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var created = ClientRequest.Create(
            command.TenantId,
            command.CustomerId,
            command.ByUserId,
            command.TaskId,
            command.Title,
            command.Details,
            command.DueAtUtc,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<ClientRequestResponse>(created.Error);

        requests.Add(created.Value);

        await bus.PublishAsync(BuildEvent(created.Value, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ClientRequestResponse.From(created.Value));
    }

    private static ClientRequestCreatedIntegrationEvent BuildEvent(ClientRequest request, string correlationId) =>
        new()
        {
            TenantId = request.TenantId,
            CorrelationId = correlationId,
            ClientRequestId = request.Id,
            CustomerId = request.CustomerId,
            TaskId = request.TaskId,
            Title = request.Title,
            Details = request.Details,
            DueAtUtc = request.DueAtUtc,
            RequestedByUserId = request.RequestedByUserId,
        };
}
