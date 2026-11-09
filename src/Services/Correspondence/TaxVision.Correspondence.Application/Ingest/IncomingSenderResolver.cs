using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Ingest;

/// <param name="CustomerId">El cliente que escribe; null si todavía no lo es.</param>
/// <param name="IsIgnoredSystemSender">Un remitente del sistema que no es cliente: no entra al inbox.</param>
public sealed record IncomingSender(Guid? CustomerId, bool IsIgnoredSystemSender);

/// <summary>Quién escribe: cliente, desconocido o remitente del sistema.</summary>
public sealed class IncomingSenderResolver(
    ICustomerEmailAddressRepository customerEmails,
    IOptions<CorrespondenceIngestOptions> options
)
{
    public async Task<IncomingSender> ResolveAsync(Guid tenantId, EmailAddress from, CancellationToken ct)
    {
        var customer = await customerEmails.FindActiveByAddressAsync(tenantId, from.NormalizedValue, ct);
        // Los avisos de no entrega los procesa Connectors: un hilo "Not a client" los duplicaría.
        return new IncomingSender(
            customer?.CustomerId,
            customer is null && options.Value.IsSystemSender(from.NormalizedValue)
        );
    }
}
