using TaxVision.DataCollection.Engine.Text;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.Api.Requests.ClientForms;

public sealed record CreateConsentNoticeRequest(
    string Key,
    string Title,
    ConsentNoticePurpose Purpose
);

public sealed record UpdateConsentNoticeRequest(string Title, string RowVersion);

public sealed record PublishConsentNoticeVersionRequest(
    string Language,
    RichTextDocument Body,
    string RowVersion
);

public sealed record ArchiveConsentNoticeRequest(string RowVersion);

public sealed record AcceptFormConsentRequest(string ComponentId);
