using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

// Blocked lleva el cliente ya creado y la request que impide vincularlo.
public sealed record FormCustomerLinkResponse(
    CustomerLinkStatus Status,
    Guid? CustomerId,
    string? Reason,
    Guid? ConflictingRequestId
)
{
    public static FormCustomerLinkResponse From(FormRequest request)
    {
        var link = request.CustomerLink;
        return new(link.Status, link.CustomerId, link.Reason, link.ConflictingRequestId);
    }
}
