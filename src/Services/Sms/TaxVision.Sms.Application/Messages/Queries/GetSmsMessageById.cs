using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain;

namespace TaxVision.Sms.Application.Messages.Queries;

/// <summary>Detalle de un mensaje del tenant. NotFound si no existe o es de otro tenant.</summary>
public sealed record GetSmsMessageByIdQuery(Guid TenantId, Guid MessageId);

public static class GetSmsMessageByIdHandler
{
    public static async Task<Result<SmsMessageDetailResponse>> Handle(
        GetSmsMessageByIdQuery query,
        ISmsReadService reader,
        CancellationToken ct
    )
    {
        var detail = await reader.GetMessageByIdAsync(query.TenantId, query.MessageId, ct);
        return detail is null
            ? Result.Failure<SmsMessageDetailResponse>(SmsErrors.MessageNotFound)
            : Result.Success(detail);
    }
}
