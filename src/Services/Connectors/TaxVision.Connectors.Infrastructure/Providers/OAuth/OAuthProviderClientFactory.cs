using BuildingBlocks.Results;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers.OAuth;

public sealed class OAuthProviderClientFactory(GoogleOAuthClient googleClient, MicrosoftOAuthClient microsoftClient)
    : IOAuthProviderClientFactory
{
    public Result<IOAuthProviderClient> Resolve(ProviderCode providerCode) =>
        providerCode switch
        {
            ProviderCode.Gmail => Result.Success<IOAuthProviderClient>(googleClient),
            ProviderCode.Graph => Result.Success<IOAuthProviderClient>(microsoftClient),
            ProviderCode.Imap => Result.Failure<IOAuthProviderClient>(
                new Error("OAuthProviderClientFactory.NotSupported", "IMAP accounts do not use OAuth refresh.")
            ),
            _ => Result.Failure<IOAuthProviderClient>(
                new Error(
                    "OAuthProviderClientFactory.UnknownProvider",
                    $"No OAuth client registered for provider {providerCode}."
                )
            ),
        };
}
