using System.Text.Json;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Application.Email.Templates;

public sealed record EmailTemplateResponse(
    Guid Id,
    string Scope,
    Guid? TenantId,
    string TemplateKey,
    string Subject,
    string? Description,
    string? Category,
    IReadOnlyList<string> Variables,
    string Status,
    Guid? CurrentVersionId,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc
);

public sealed record EmailTemplateVersionResponse(
    Guid Id,
    int VersionNumber,
    string Status,
    string HtmlStorageKey,
    Guid HtmlFileId,
    string? DesignStorageKey,
    string? PreviewStorageKey,
    DateTime CreatedAtUtc
);

public sealed record EmailTemplateDetailResponse(
    EmailTemplateResponse Template,
    IReadOnlyList<EmailTemplateVersionResponse> Versions
);

public static class EmailTemplateMapper
{
    public static EmailTemplateResponse ToResponse(EmailTemplate t) =>
        new(
            t.Id,
            t.Scope.ToString(),
            t.TenantId,
            t.TemplateKey,
            t.Subject,
            t.Description,
            t.Category,
            ParseVariables(t.VariablesJson),
            t.Status.ToString(),
            t.CurrentVersionId,
            t.CreatedAtUtc,
            t.PublishedAtUtc
        );

    public static EmailTemplateVersionResponse ToResponse(EmailTemplateVersion v) =>
        new(
            v.Id,
            v.VersionNumber,
            v.Status.ToString(),
            v.HtmlStorageKey,
            v.HtmlFileId,
            v.DesignStorageKey,
            v.PreviewStorageKey,
            v.CreatedAtUtc
        );

    public static IReadOnlyList<string> ParseVariables(string variablesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(variablesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeVariables(IReadOnlyList<string>? variables) =>
        JsonSerializer.Serialize(variables ?? []);
}
