using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

public static class InitiateAdminConsentHandler
{
    public static async Task<InitiateAdminConsentResult> Handle(
        InitiateAdminConsentCommand cmd,
        IMicrosoftAdminConsentClient adminConsentClient,
        IOAuthConnectStateStore stateStore,
        CancellationToken ct
    )
    {
        var state = await stateStore.CreateAsync(cmd.TenantId, ProviderCode.Graph, cmd.InitiatedByUserId, ct);
        return new InitiateAdminConsentResult(adminConsentClient.BuildAdminConsentUrl(state));
    }
}
