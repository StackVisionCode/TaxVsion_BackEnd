using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Delivery;

// ------------------------------------------------------------------
// La oficina ve, reenvía y revoca las entregas de una solicitud (no aparecen en Documents como shares).
// ------------------------------------------------------------------

public sealed record ListRequestDeliveriesQuery(Guid TenantId, Guid SignatureRequestId);

public sealed record ResendDeliveryCommand(Guid TenantId, Guid SignatureRequestId, Guid DeliveryId, Guid ActorUserId);

public sealed record RevokeDeliveryCommand(Guid TenantId, Guid SignatureRequestId, Guid DeliveryId, Guid ActorUserId);

/// <summary>
/// Emite las entregas que falten de una solicitud completada (p. ej. si el evento que las dispara se perdió).
/// Idempotente: un firmante que ya tiene su entrega no recibe otra.
/// </summary>
public sealed record IssueDeliveriesCommand(Guid TenantId, Guid SignatureRequestId, Guid ActorUserId);

public sealed record DeliveryItemResponse(Guid Id, string Title, string Kind);

public sealed record SignedDocumentDeliveryResponse(
    Guid Id,
    Guid SignerId,
    string RecipientEmail,
    string Purpose,
    string Status,
    DateTime CreatedAtUtc,
    DateTime LastIssuedAtUtc,
    DateTime ExpiresAtUtc,
    int IssueCount,
    int AccessCount,
    DateTime? LastAccessedAtUtc,
    DateTime? RevokedAtUtc,
    IReadOnlyList<DeliveryItemResponse> Items
)
{
    public static SignedDocumentDeliveryResponse From(SignedDocumentDelivery delivery, DateTime nowUtc) =>
        new(
            delivery.Id,
            delivery.SignerId,
            delivery.RecipientEmail,
            delivery.Purpose.ToString(),
            delivery.StatusAt(nowUtc).ToString(),
            delivery.CreatedAtUtc,
            delivery.LastIssuedAtUtc,
            delivery.ExpiresAtUtc,
            delivery.IssueCount,
            delivery.AccessCount,
            delivery.LastAccessedAtUtc,
            delivery.RevokedAtUtc,
            delivery.Items.OrderBy(item => item.Order).Select(item => new DeliveryItemResponse(item.Id, item.Title, item.Kind.ToString())).ToList()
        );
}

public static class ListRequestDeliveriesHandler
{
    public static async Task<Result<IReadOnlyList<SignedDocumentDeliveryResponse>>> Handle(
        ListRequestDeliveriesQuery query,
        ISignatureRequestRepository requests,
        ISignedDocumentDeliveryRepository deliveries,
        CancellationToken ct
    )
    {
        if (await requests.GetByIdAsync(query.TenantId, query.SignatureRequestId, ct) is null)
            return Result.Failure<IReadOnlyList<SignedDocumentDeliveryResponse>>(StaffDelivery.RequestNotFound);

        var now = DateTime.UtcNow;
        var list = await deliveries.ListByRequestAsync(query.TenantId, query.SignatureRequestId, ct);
        return Result.Success<IReadOnlyList<SignedDocumentDeliveryResponse>>(
            list.OrderByDescending(delivery => delivery.CreatedAtUtc)
                .Select(delivery => SignedDocumentDeliveryResponse.From(delivery, now))
                .ToList()
        );
    }
}

public static class ResendDeliveryHandler
{
    public static async Task<Result<SignedDocumentDeliveryResponse>> Handle(
        ResendDeliveryCommand command,
        ISignatureRequestRepository requests,
        ISignedDocumentDeliveryRepository deliveries,
        ISignedDocumentDeliveryIssuer issuer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = await requests.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
        var delivery = await deliveries.GetByIdAsync(command.TenantId, command.DeliveryId, ct);
        if (request is null || delivery is null || delivery.SignatureRequestId != request.Id)
            return Result.Failure<SignedDocumentDeliveryResponse>(StaffDelivery.DeliveryNotFound);

        var reissued = await issuer.PrepareReissueAsync(delivery, command.ActorUserId, ct);
        if (reissued.IsFailure)
            return Result.Failure<SignedDocumentDeliveryResponse>(reissued.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await issuer.PublishAsync(reissued.Value, request, ct);
        return Result.Success(SignedDocumentDeliveryResponse.From(delivery, DateTime.UtcNow));
    }
}

public static class IssueDeliveriesHandler
{
    public static async Task<Result<IReadOnlyList<SignedDocumentDeliveryResponse>>> Handle(
        IssueDeliveriesCommand command,
        ISignatureRequestRepository requests,
        ISignedDocumentDeliveryRepository deliveries,
        DeliverableFiles deliverableFiles,
        ISignedDocumentDeliveryIssuer issuer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var request = await requests.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<IReadOnlyList<SignedDocumentDeliveryResponse>>(StaffDelivery.RequestNotFound);
        if (request.Status != SignatureRequestStatus.Completed)
            return Result.Failure<IReadOnlyList<SignedDocumentDeliveryResponse>>(StaffDelivery.RequestNotCompleted);
        if (!DeliverableFiles.HasSomethingToDeliver(request))
            return Result.Failure<IReadOnlyList<SignedDocumentDeliveryResponse>>(StaffDelivery.NothingToDeliver);
        if (!await deliverableFiles.AllAvailableAsync(request, availableFileId: null, ct))
            return Result.Failure<IReadOnlyList<SignedDocumentDeliveryResponse>>(StaffDelivery.FilesNotReady);

        var issued = await issuer.PrepareCompletedAsync(request, ct);
        if (issued.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            foreach (var delivery in issued)
                await issuer.PublishAsync(delivery, request, ct);
        }

        var now = DateTime.UtcNow;
        var list = await deliveries.ListByRequestAsync(command.TenantId, command.SignatureRequestId, ct);
        return Result.Success<IReadOnlyList<SignedDocumentDeliveryResponse>>(
            list.OrderByDescending(delivery => delivery.CreatedAtUtc)
                .Select(delivery => SignedDocumentDeliveryResponse.From(delivery, now))
                .ToList()
        );
    }
}

public static class RevokeDeliveryHandler
{
    public static async Task<Result<SignedDocumentDeliveryResponse>> Handle(
        RevokeDeliveryCommand command,
        ISignedDocumentDeliveryRepository deliveries,
        IAuditChainAppender audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var delivery = await deliveries.GetByIdAsync(command.TenantId, command.DeliveryId, ct);
        if (delivery is null || delivery.SignatureRequestId != command.SignatureRequestId)
            return Result.Failure<SignedDocumentDeliveryResponse>(StaffDelivery.DeliveryNotFound);

        var now = DateTime.UtcNow;
        if (delivery.RevokedAtUtc is null)
        {
            delivery.Revoke(command.ActorUserId, now);
            await audit.AppendAsync(
                delivery.TenantId,
                delivery.SignatureRequestId,
                SignatureAuditEventKind.DeliveryRevoked,
                now,
                new { deliveryId = delivery.Id, signerId = delivery.SignerId, byUserId = command.ActorUserId },
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success(SignedDocumentDeliveryResponse.From(delivery, now));
    }
}

internal static class StaffDelivery
{
    public static readonly Error RequestNotFound = new(
        "Signature.Request.NotFound",
        "The signature request does not exist for this tenant."
    );

    public static readonly Error DeliveryNotFound = new(
        "Signature.Delivery.NotFound",
        "The delivery does not exist for this request."
    );

    public static readonly Error RequestNotCompleted = new(
        "Signature.Delivery.RequestNotCompleted",
        "Signed documents can be sent once everyone has signed."
    );

    public static readonly Error NothingToDeliver = new(
        "Signature.Delivery.NothingToDeliver",
        "This request was not set to email the signed documents or the certificate."
    );

    public static readonly Error FilesNotReady = new(
        "Signature.Delivery.FilesNotReady",
        "The signed files are still being processed. Try again in a moment."
    );
}
