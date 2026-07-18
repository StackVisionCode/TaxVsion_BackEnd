using BuildingBlocks.Results;
using TaxVision.Connectors.Application.OAuth;

namespace TaxVision.Connectors.Application.Accounts;

public static class InitiateOAuthConnectHandler
{
    public static async Task<Result<InitiateOAuthConnectResult>> Handle(
        InitiateOAuthConnectCommand cmd,
        IOAuthProviderClientFactory clientFactory,
        IOAuthConnectStateStore stateStore,
        CancellationToken ct
    )
    {
        var clientResult = clientFactory.Resolve(cmd.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<InitiateOAuthConnectResult>(clientResult.Error);

        var state = await stateStore.CreateAsync(cmd.TenantId, cmd.ProviderCode, cmd.InitiatedByUserId, ct);
        var authorizationUrl = clientResult.Value.BuildAuthorizationUrl(state);

        return Result.Success(new InitiateOAuthConnectResult(authorizationUrl));
    }
}
