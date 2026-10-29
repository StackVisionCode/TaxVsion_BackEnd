using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

// Lo devuelve toda mutación del participante: sin él, el siguiente guardado choca por RowVersion.
public sealed record FormRequestRowVersionResponse(string RowVersion)
{
    public static FormRequestRowVersionResponse From(FormRequest request) =>
        new(Convert.ToBase64String(request.RowVersion));
}
