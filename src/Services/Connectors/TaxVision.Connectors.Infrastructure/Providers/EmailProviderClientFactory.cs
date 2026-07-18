using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using ImapProviderClient = TaxVision.Connectors.Infrastructure.Providers.Imap.ImapClient;

namespace TaxVision.Connectors.Infrastructure.Providers;

public sealed class EmailProviderClientFactory(
    GmailApiClient gmailClient,
    GraphApiClient graphClient,
    ImapProviderClient imapClient
) : IEmailProviderClientFactory
{
    public Result<IEmailProviderClient> Resolve(ProviderCode providerCode) =>
        providerCode switch
        {
            ProviderCode.Gmail => Result.Success<IEmailProviderClient>(gmailClient),
            ProviderCode.Graph => Result.Success<IEmailProviderClient>(graphClient),
            ProviderCode.Imap => Result.Success<IEmailProviderClient>(imapClient),
            _ => Result.Failure<IEmailProviderClient>(
                new Error(
                    "EmailProviderClientFactory.UnknownProvider",
                    $"No email provider client registered for provider {providerCode}."
                )
            ),
        };
}
