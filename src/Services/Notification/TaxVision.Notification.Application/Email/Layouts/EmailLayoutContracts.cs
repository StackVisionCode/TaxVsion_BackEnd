using TaxVision.Notification.Domain.Emailing.Layouts;

namespace TaxVision.Notification.Application.Email.Layouts;

public sealed record EmailLayoutResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string LayoutName,
    string? HtmlStorageKey,
    string? PreviewStorageKey,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public static class EmailLayoutMapper
{
    public static EmailLayoutResponse ToResponse(EmailLayout l) =>
        new(
            l.Id,
            l.Scope.ToString(),
            l.TenantId,
            l.LayoutName,
            l.HtmlStorageKey,
            l.PreviewStorageKey,
            l.IsDefault,
            l.IsActive,
            l.CreatedAtUtc,
            l.UpdatedAtUtc
        );
}
