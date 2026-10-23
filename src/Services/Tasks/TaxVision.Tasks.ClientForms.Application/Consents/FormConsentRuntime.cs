using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

// Lo que hace falta para leer el componente consent de una request, agrupado para no repetirlo.
internal sealed record FormConsentRuntime(
    IFormRequestRepository Requests,
    IFormTemplateVersionRepository Versions,
    IConsentNoticeRepository Notices,
    IAnswersProtector Protector,
    IFormEngine Engine
);
