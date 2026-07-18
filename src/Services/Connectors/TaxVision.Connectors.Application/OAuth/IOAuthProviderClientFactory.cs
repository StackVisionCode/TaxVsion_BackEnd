using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.OAuth;

public interface IOAuthProviderClientFactory
{
    Result<IOAuthProviderClient> Resolve(ProviderCode providerCode);
}
