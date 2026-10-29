namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record CreateFormIntakeLinkRequest(
    string Name,
    Guid FormTemplateId,
    Guid? FormTemplateVersionId,
    string EntryNoticeKey,
    DateTime? ExpiresAtUtc,
    int? MaxSubmissions,
    Guid ResponsibleUserId
);

public sealed record ChangeFormIntakeLinkRequest(string RowVersion);

public sealed record PinFormIntakeLinkVersionRequest(Guid FormTemplateVersionId, string RowVersion);
