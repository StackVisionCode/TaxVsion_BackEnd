using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Delivery;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Delivery;

// ------------------------------------------------------------------
// Página pública de entrega (/d/{token}): la autoriza el secreto del enlace, sin sesión.
// ------------------------------------------------------------------

public sealed record GetPublicDeliveryQuery(string Token, Guid? HostTenantId = null);

public sealed record OpenDeliveryFileCommand(string Token, Guid ItemId, Guid? HostTenantId = null);

public sealed record RequestNewDeliveryLinkCommand(string Token, Guid? HostTenantId = null);

public sealed record PublicDeliveryItemView(Guid Id, string Title, string Kind);

/// <summary>
/// Lo que ve quien abre el enlace. Los títulos solo se muestran con el enlace vigente; vencido o revocado
/// se informa el estado y, si la oficina lo permite, la opción de pedir uno nuevo al email enmascarado.
/// </summary>
public sealed record PublicDeliveryView(
    string Status,
    string Purpose,
    string MaskedEmail,
    DateTime ExpiresAtUtc,
    IReadOnlyList<PublicDeliveryItemView> Items,
    bool CanRequestNewLink
);

public sealed record DeliveryFileLink(Uri Url);

public static class DeliveryErrors
{
    // Mismo error para "no existe" y "token inválido": no se revela si un token existió.
    public static readonly Error NotFound = new("Signature.Delivery.NotFound", "This link is not valid.");

    public static readonly Error NotActive = new(
        "Signature.Delivery.NotActive",
        "This link has expired or was revoked."
    );

    public static readonly Error ItemNotFound = new(
        "Signature.Delivery.ItemNotFound",
        "This file is not part of the delivery."
    );

    public static readonly Error FileUnavailable = new(
        "Signature.Delivery.FileUnavailable",
        "The file is not ready yet. Please try again in a minute."
    );

    public static readonly Error ReissueNotAllowed = new(
        "Signature.Delivery.ReissueNotAllowed",
        "A new link can't be requested for this delivery. Please contact the office that sent it."
    );

    public static readonly Error ReissueTooSoon = new(
        "Signature.Delivery.ReissueTooSoon",
        "A new link was sent a few minutes ago. Check your inbox (and spam folder) before asking again."
    );

    public static readonly Error StillActive = new(
        "Signature.Delivery.StillActive",
        "This link is still valid. Use it to download your documents."
    );
}

public static class GetPublicDeliveryHandler
{
    public static async Task<Result<PublicDeliveryView>> Handle(
        GetPublicDeliveryQuery query,
        ISignedDocumentDeliveryRepository deliveries,
        ITenantSignatureSettingsRepository settings,
        CancellationToken ct
    )
    {
        var delivery = await PublicDelivery.FindAsync(query.Token, query.HostTenantId, deliveries, ct);
        if (delivery is null)
            return Result.Failure<PublicDeliveryView>(DeliveryErrors.NotFound);

        var now = DateTime.UtcNow;
        var status = delivery.StatusAt(now);
        var policy = (await settings.GetByTenantIdAsync(delivery.TenantId, ct))?.Delivery ?? DeliveryPolicy.Default();

        return Result.Success(
            new PublicDeliveryView(
                status.ToString(),
                delivery.Purpose.ToString(),
                PublicDelivery.MaskEmail(delivery.RecipientEmail),
                delivery.ExpiresAtUtc,
                status == DeliveryStatus.Active
                    ? delivery
                        .Items.OrderBy(item => item.Order)
                        .Select(item => new PublicDeliveryItemView(item.Id, item.Title, item.Kind.ToString()))
                        .ToList()
                    : [],
                status == DeliveryStatus.Expired && policy.AllowPublicReissue
            )
        );
    }
}

public static class OpenDeliveryFileHandler
{
    public static async Task<Result<DeliveryFileLink>> Handle(
        OpenDeliveryFileCommand command,
        ISignedDocumentDeliveryRepository deliveries,
        ISignatureCloudStorageClient storage,
        IUnitOfWork unitOfWork,
        ILogger<OpenDeliveryFileCommand> logger,
        CancellationToken ct
    )
    {
        var delivery = await PublicDelivery.FindAsync(command.Token, command.HostTenantId, deliveries, ct);
        if (delivery is null)
            return Result.Failure<DeliveryFileLink>(DeliveryErrors.NotFound);

        var item = delivery.Items.SingleOrDefault(candidate => candidate.Id == command.ItemId);
        if (item is null)
            return Result.Failure<DeliveryFileLink>(DeliveryErrors.ItemNotFound);

        var access = delivery.RegisterAccess(DateTime.UtcNow);
        if (access.IsFailure)
            return Result.Failure<DeliveryFileLink>(DeliveryErrors.NotActive);

        // URL firmada de pocos minutos, pedida en este momento: no queda ningún enlace largo dando vueltas.
        var url = await storage.GetDownloadUrlAsync(delivery.TenantId, item.FileId, ct);
        if (url.IsFailure)
        {
            logger.LogWarning(
                "Delivery {DeliveryId} file {FileId} could not be presigned: {Error}",
                delivery.Id,
                item.FileId,
                url.Error.Message
            );
            return Result.Failure<DeliveryFileLink>(DeliveryErrors.FileUnavailable);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new DeliveryFileLink(url.Value));
    }
}

public static class RequestNewDeliveryLinkHandler
{
    public static async Task<Result> Handle(
        RequestNewDeliveryLinkCommand command,
        ISignedDocumentDeliveryRepository deliveries,
        ISignatureRequestRepository requests,
        ITenantSignatureSettingsRepository settings,
        ISignedDocumentDeliveryIssuer issuer,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var delivery = await PublicDelivery.FindAsync(command.Token, command.HostTenantId, deliveries, ct);
        if (delivery is null)
            return Result.Failure(DeliveryErrors.NotFound);

        var now = DateTime.UtcNow;
        var status = delivery.StatusAt(now);
        if (status == DeliveryStatus.Active)
            return Result.Failure(DeliveryErrors.StillActive);

        var policy = (await settings.GetByTenantIdAsync(delivery.TenantId, ct))?.Delivery ?? DeliveryPolicy.Default();
        if (status == DeliveryStatus.Revoked || !policy.AllowPublicReissue)
            return Result.Failure(DeliveryErrors.ReissueNotAllowed);
        if (!delivery.CanBeReissuedPubliclyAt(now))
            return Result.Failure(DeliveryErrors.ReissueTooSoon);

        var request = await requests.GetByIdAsync(delivery.TenantId, delivery.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(DeliveryErrors.ReissueNotAllowed);

        // Siempre al email original de la entrega: quien tiene el enlace no puede redirigirlo.
        var reissued = await issuer.PrepareReissueAsync(delivery, byUserId: null, ct);
        if (reissued.IsFailure)
            return Result.Failure(reissued.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await issuer.PublishAsync(reissued.Value, request, ct);
        return Result.Success();
    }
}

internal static class PublicDelivery
{
    // Bajo el subdominio de otra oficina el enlace "no existe": evita que se use para phishing.
    public static async Task<SignedDocumentDelivery?> FindAsync(
        string token,
        Guid? hostTenantId,
        ISignedDocumentDeliveryRepository deliveries,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return null;
        var delivery = await deliveries.GetByTokenHashAsync(DeliveryToken.HashOf(token.Trim()), ct);
        return delivery is not null && hostTenantId is { } host && host != delivery.TenantId ? null : delivery;
    }

    /// <summary><c>ana.perez@mail.com</c> → <c>a***z@mail.com</c>: lo justo para reconocer el propio email.</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";
        var local = email[..at];
        var masked = local.Length <= 2 ? $"{local[0]}***" : $"{local[0]}***{local[^1]}";
        return masked + email[at..];
    }
}
