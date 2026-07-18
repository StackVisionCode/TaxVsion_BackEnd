using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Application.Layouts;

public sealed record EmailLayoutVersionResponse(
    Guid Id,
    int VersionNumber,
    string Status,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc
);

public sealed record EmailLayoutResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string LayoutKey,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<EmailLayoutVersionResponse> Versions,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public static class EmailLayoutMapper
{
    public static EmailLayoutResponse ToResponse(EmailLayout layout) =>
        new(
            layout.Id,
            layout.Scope.ToString(),
            layout.TenantId,
            layout.LayoutKey.Value,
            layout.Name,
            layout.Description,
            layout.Status.ToString(),
            layout.Versions.Select(ToVersionResponse).ToList(),
            layout.CreatedAtUtc,
            layout.UpdatedAtUtc
        );

    public static EmailLayoutVersionResponse ToVersionResponse(EmailLayoutVersion version) =>
        new(version.Id, version.VersionNumber, version.Status.ToString(), version.PublishedAtUtc, version.CreatedAtUtc);
}
