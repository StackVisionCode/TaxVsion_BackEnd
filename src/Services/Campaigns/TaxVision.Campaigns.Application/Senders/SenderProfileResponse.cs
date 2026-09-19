using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Application.Senders;

/// <summary>DTO de salida de un remitente — nunca se devuelve el aggregate al Api.</summary>
public sealed record SenderProfileResponse(
    Guid Id,
    Guid TenantId,
    string Channel,
    string Name,
    string SenderRef,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static SenderProfileResponse From(SenderProfile s) =>
        new(
            s.Id,
            s.TenantId,
            s.Channel.ToString(),
            s.Name,
            s.SenderRef,
            s.Status.ToString(),
            s.CreatedAtUtc,
            s.UpdatedAtUtc
        );
}
