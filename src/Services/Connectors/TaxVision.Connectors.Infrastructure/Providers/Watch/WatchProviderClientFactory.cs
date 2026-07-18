using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Infrastructure.Providers.Watch;

public sealed class WatchProviderClientFactory(GmailWatchClient gmailClient, GraphWatchClient graphClient)
    : IWatchProviderClientFactory
{
    public Result<IWatchProviderClient> Resolve(ProviderCode providerCode) =>
        providerCode switch
        {
            ProviderCode.Gmail => Result.Success<IWatchProviderClient>(gmailClient),
            ProviderCode.Graph => Result.Success<IWatchProviderClient>(graphClient),
            ProviderCode.Imap => Result.Failure<IWatchProviderClient>(
                new Error(
                    "WatchProviderClientFactory.NotSupported",
                    "IMAP has no push/watch mechanism — accounts using IMAP are activated directly without a ProviderWatchSubscription."
                )
            ),
            _ => Result.Failure<IWatchProviderClient>(
                new Error(
                    "WatchProviderClientFactory.UnknownProvider",
                    $"No watch client registered for provider {providerCode}."
                )
            ),
        };
}
