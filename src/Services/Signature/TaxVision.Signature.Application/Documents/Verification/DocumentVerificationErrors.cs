using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Documents.Verification;

public static class DocumentVerificationErrors
{
    /// <summary>Error de API cuando el archivo ya disponible no pasa la revisión del servidor.</summary>
    public static Error Rejected(RequestDocument document, DocumentRejection rejection) =>
        new(rejection.Code, $"'{document.Title}' can't be used: {rejection.Reason}");
}
