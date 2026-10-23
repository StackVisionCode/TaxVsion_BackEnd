using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

internal sealed record ResolvedFormConsent(
    FormRequest Request,
    FormConsentDefinition Consent,
    ConsentNoticeVersion NoticeVersion
);
