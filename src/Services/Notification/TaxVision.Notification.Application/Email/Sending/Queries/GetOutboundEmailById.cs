using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Email.Sending.Queries;

public sealed record GetOutboundEmailByIdQuery(Guid MessageId, Guid TenantId);

public static class GetOutboundEmailByIdHandler
{
    public static async Task<Result<OutboundEmailResponse>> Handle(
        GetOutboundEmailByIdQuery query,
        IOutboundEmailRepository repository,
        CancellationToken ct
    )
    {
        var message = await repository.GetByIdAsync(query.MessageId, query.TenantId, ct);
        return message is null
            ? Result.Failure<OutboundEmailResponse>(new Error("EmailMessage.NotFound", "Message not found."))
            : Result.Success(OutboundEmailMapper.ToResponse(message));
    }
}
