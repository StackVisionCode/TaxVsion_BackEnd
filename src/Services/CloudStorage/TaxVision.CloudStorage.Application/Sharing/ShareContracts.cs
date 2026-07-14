using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Sharing;

public sealed record ShareLinkResponse(
    Guid Id,
    Guid ResourceId,
    ShareResourceType ResourceType,
    ShareVisibility Visibility,
    SharePermission Permission,
    string TokenLast4,
    bool HasPassword,
    DateTime ExpiresAtUtc,
    int? MaxAccessCount,
    int AccessCount,
    ShareLinkEffectiveStatus Status,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? RevokedAtUtc
);

/// <summary>El PlainToken solo se emite en la respuesta de creacion — nunca se puede volver a consultar.</summary>
public sealed record CreatedShareLinkResponse(ShareLinkResponse Link, string PlainToken);

internal static class ShareLinkResponseMapper
{
    public static ShareLinkResponse Map(ShareLink link, DateTime nowUtc) =>
        new(
            link.Id,
            link.ResourceId,
            link.ResourceType,
            link.Visibility,
            link.Permission,
            link.TokenLast4,
            link.PasswordHash is not null,
            link.ExpiresAtUtc,
            link.MaxAccessCount,
            link.AccessCount,
            link.EffectiveStatus(nowUtc),
            link.CreatedByUserId,
            link.CreatedAtUtc,
            link.RevokedAtUtc
        );
}
