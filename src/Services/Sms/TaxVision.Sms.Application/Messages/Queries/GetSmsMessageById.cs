using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Detalle de un mensaje del tenant. NotFound si no existe, es de otro tenant o (visibilidad P2) su
/// cliente no está asignado al actor que no ve todo — no filtrar existencia a quien no debe verlo.</summary>
public sealed record GetSmsMessageByIdQuery(Guid TenantId, Guid MessageId, Guid ActorUserId, bool CanViewAll);

public static class GetSmsMessageByIdHandler
{
    public static async Task<Result<SmsMessageDetailResponse>> Handle(
        GetSmsMessageByIdQuery query,
        ISmsReadService reader,
        IOptions<SmsVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var detail = await reader.GetMessageByIdAsync(query.TenantId, query.MessageId, assignedTo, ct);
        return detail is null
            ? Result.Failure<SmsMessageDetailResponse>(SmsErrors.MessageNotFound)
            : Result.Success(detail);
    }
}
