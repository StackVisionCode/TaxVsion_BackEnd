using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

public interface IEmailProviderClientFactory
{
    Result<IEmailProviderClient> Resolve(ProviderCode providerCode);
}
