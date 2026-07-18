using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

public interface IOutboundEmailProviderClientFactory
{
    /// <summary>IMAP nunca resuelve — no implementa <see cref="IOutboundEmailProviderClient"/> (D3 §3.1, IMAP no envía correo).</summary>
    Result<IOutboundEmailProviderClient> Resolve(ProviderCode providerCode);
}
