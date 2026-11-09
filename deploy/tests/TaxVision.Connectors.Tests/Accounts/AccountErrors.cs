using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests;

/// <summary>Causas de prueba para llevar una cuenta a Error.</summary>
internal static class AccountErrors
{
    public static readonly TenantEmailAccountErrorCause RefreshRejected = new(
        TenantEmailAccountErrorSource.OAuthRefresh,
        "OAuthToken.RefreshFailed",
        "invalid_grant",
        "test-correlation"
    );
}
