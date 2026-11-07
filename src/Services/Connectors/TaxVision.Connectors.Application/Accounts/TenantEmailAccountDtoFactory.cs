using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Arma el DTO de cuenta con los permisos que le faltan a su conexión OAuth activa.</summary>
internal static class TenantEmailAccountDtoFactory
{
    public static async Task<IReadOnlyList<TenantEmailAccountDto>> CreateAsync(
        IReadOnlyList<TenantEmailAccount> accounts,
        IOAuthConnectionRepository connectionRepository,
        IOAuthProviderClientFactory clientFactory,
        CancellationToken ct
    )
    {
        if (accounts.Count == 0)
            return [];

        var grantedScopes = await connectionRepository.ListActiveScopesByAccountIdsAsync(
            accounts.Select(a => a.Id).ToList(),
            ct
        );
        return accounts.Select(a => ToDto(a, MissingScopes(a, grantedScopes, clientFactory))).ToList();
    }

    // IMAP o sin conexión activa: no hay grant que ampliar.
    private static IReadOnlyList<string> MissingScopes(
        TenantEmailAccount account,
        IReadOnlyDictionary<Guid, string> grantedScopes,
        IOAuthProviderClientFactory clientFactory
    )
    {
        if (!grantedScopes.TryGetValue(account.Id, out var granted))
            return [];

        var client = clientFactory.Resolve(account.ProviderCode);
        return client.IsFailure
            ? []
            : OAuthScopeSet.Parse(granted).Missing(OAuthScopeSet.Parse(client.Value.ConfiguredScope));
    }

    private static TenantEmailAccountDto ToDto(TenantEmailAccount a, IReadOnlyList<string> missingScopes) =>
        new(
            a.Id,
            a.EmailAddress,
            a.ProviderCode.ToString(),
            a.DisplayName,
            a.Status.ToString(),
            a.ConnectedAtUtc,
            a.CreatedAtUtc,
            a.OwnerUserId,
            a.IsOffice,
            missingScopes,
            a.NeedsReview,
            a.ReviewReason,
            a.ReviewRequiredAtUtc
        );
}
