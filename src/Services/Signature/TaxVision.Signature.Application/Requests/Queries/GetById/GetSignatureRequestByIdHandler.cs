using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Queries.GetById;

public static class GetSignatureRequestByIdHandler
{
    public static async Task<SignatureRequestResponse?> Handle(
        GetSignatureRequestByIdQuery query,
        ISignatureRequestRepository repository,
        ISignatureRequestVisibilityGate visibility,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(query.TenantId, query.SignatureRequestId, ct);
        if (request is null)
            return null;

        // Gate por asignación: la solicitud no tiene cliente propio; su "dueño" es el cliente de sus firmantes.
        var mappedCustomerIds = request
            .Signers.Where(s => s.MappedCustomerId.HasValue)
            .Select(s => s.MappedCustomerId!.Value)
            .Distinct()
            .ToArray();

        var canSee = await visibility.CanActorSeeAsync(
            query.TenantId,
            query.ActorUserId,
            query.CanViewAll,
            mappedCustomerIds,
            ct
        );
        return canSee ? SignatureRequestResponse.From(request) : null;
    }
}
