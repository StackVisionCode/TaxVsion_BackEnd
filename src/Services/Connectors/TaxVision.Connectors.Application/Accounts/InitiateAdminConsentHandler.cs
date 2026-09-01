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
        // Admin-consent no conecta un buzón (solo confirma el consentimiento org-wide), así que no
        // hay email de buzón que validar en su callback → initiatorEmail null.
        var state = await stateStore.CreateAsync(
            cmd.TenantId,
            ProviderCode.Graph,
            cmd.InitiatedByUserId,
            initiatorEmail: null,
            ct: ct
        );
        return new InitiateAdminConsentResult(adminConsentClient.BuildAdminConsentUrl(state));
    }
}
