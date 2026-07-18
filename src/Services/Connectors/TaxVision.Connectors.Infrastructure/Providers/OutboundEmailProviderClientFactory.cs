using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using TaxVision.Connectors.Infrastructure.Providers.Manual;

namespace TaxVision.Connectors.Infrastructure.Providers;

/// <summary>
/// <c>ProviderCode.Imap</c> resuelve a <see cref="SmtpManualClient"/> desde D3 Compose §8 — el nombre
/// del enum quedó como estaba (evita el costo de un rename que toca migración/DI/tests ya en
/// producción, ver D3 Compose §11.1 nota de nomenclatura), pero representa cualquier cuenta manual
/// IMAP+SMTP capaz de enviar, no solo "IMAP puro". Si la cuenta no tiene <c>SmtpCredentials</c>
/// configuradas, <see cref="SmtpManualClient.SendMessageAsync"/> falla explícito al resolverlas — el
/// factory no necesita saberlo de antemano.
/// </summary>
public sealed class OutboundEmailProviderClientFactory(
    GmailApiClient gmailClient,
    GraphApiClient graphClient,
    SmtpManualClient smtpManualClient
) : IOutboundEmailProviderClientFactory
{
    public Result<IOutboundEmailProviderClient> Resolve(ProviderCode providerCode) =>
        providerCode switch
        {
            ProviderCode.Gmail => Result.Success<IOutboundEmailProviderClient>(gmailClient),
            ProviderCode.Graph => Result.Success<IOutboundEmailProviderClient>(graphClient),
            ProviderCode.Imap => Result.Success<IOutboundEmailProviderClient>(smtpManualClient),
            _ => Result.Failure<IOutboundEmailProviderClient>(
                new Error(
                    "OutboundEmailProviderClientFactory.UnknownProvider",
                    $"No outbound email provider client registered for provider {providerCode}."
                )
            ),
        };
}
