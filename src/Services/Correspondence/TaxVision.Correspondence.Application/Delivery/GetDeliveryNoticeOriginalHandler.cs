using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;

namespace TaxVision.Correspondence.Application.Delivery;

/// <summary>
/// <paramref name="VisibleAccountIds"/> null = todos los buzones; <paramref name="RestrictToUserId"/> null = todos los
/// clientes. Mismo gate que la conversación del aviso.
/// </summary>
public sealed record GetDeliveryNoticeOriginalQuery(
    Guid TenantId,
    Guid DeliveryNoticeId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null
);

/// <summary>
/// "View original notice": el aviso técnico tal como llegó al buzón, pedido en vivo a Connectors (mismo camino que
/// el cuerpo de un entrante; nunca se guarda). 404 si el aviso no existe o su conversación no se ve; 409 si llegó
/// por webhook y no hay aviso que mostrar.
/// </summary>
public static class GetDeliveryNoticeOriginalHandler
{
    public static readonly Error NotFound = new("DeliveryNotice.NotFound", "The delivery notice was not found.");

    public static readonly Error OriginalUnavailable = new(
        "DeliveryNotice.OriginalUnavailable",
        "The original notice is not available for this delivery result."
    );

    public static async Task<Result<MessageBodyResult>> Handle(
        GetDeliveryNoticeOriginalQuery query,
        IDeliveryNoticeRepository notices,
        IEmailThreadRepository emailThreads,
        IConnectorsClient connectorsClient,
        CancellationToken ct
    )
    {
        var notice = await notices.GetByIdAsync(query.TenantId, query.DeliveryNoticeId, ct);
        var thread = notice is null ? null : await emailThreads.GetByIdAsync(query.TenantId, notice.EmailThreadId, ct);
        if (
            notice is null
            || thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                query.VisibleAccountIds,
                query.RestrictToUserId,
                ct
            )
        )
            return Result.Failure<MessageBodyResult>(NotFound);

        if (!notice.CanViewOriginal)
            return Result.Failure<MessageBodyResult>(OriginalUnavailable);

        var body = await connectorsClient.FetchMessageBodyAsync(
            query.TenantId,
            notice.ReportAccountId!.Value,
            notice.ReportProviderMessageId!,
            ct
        );
        return body.IsFailure
            ? Result.Failure<MessageBodyResult>(body.Error)
            : Result.Success(new MessageBodyResult(body.Value.HtmlBody, body.Value.TextBody, body.Value.Headers));
    }
}
