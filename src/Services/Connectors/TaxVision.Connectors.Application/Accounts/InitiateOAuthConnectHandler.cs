using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Common;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

public static class InitiateOAuthConnectHandler
{
    public static async Task<Result<InitiateOAuthConnectResult>> Handle(
        InitiateOAuthConnectCommand cmd,
        IOAuthProviderClientFactory clientFactory,
        IOAuthConnectStateStore stateStore,
        IOptions<ConnectorsPortalOptions> portal,
        IPlatformSettings platform,
        CancellationToken ct
    )
    {
        // Solo se guarda un origen válido de la oficina (o localhost con su puerto en local).
        string? returnOrigin = null;
        if (!string.IsNullOrWhiteSpace(cmd.ReturnOrigin))
        {
            var origin = OAuthReturnOrigin.Create(cmd.ReturnOrigin, portal.Value.BaseUrl, platform.Current.BaseDomain);
            if (origin.IsFailure && !cmd.ReturnOriginFromHeader)
                return Result.Failure<InitiateOAuthConnectResult>(origin.Error);
            returnOrigin = origin.IsSuccess ? origin.Value.Value : null;
        }

        var clientResult = clientFactory.Resolve(cmd.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<InitiateOAuthConnectResult>(clientResult.Error);

        var state = await stateStore.CreateAsync(
            cmd.TenantId,
            cmd.ProviderCode,
            cmd.InitiatedByUserId,
            cmd.InitiatorEmail,
            returnOrigin,
            cmd.AsOffice,
            cmd.InitiatorIsPlatformAdmin,
            ct
        );
        var authorizationUrl = clientResult.Value.BuildAuthorizationUrl(state);

        return Result.Success(new InitiateOAuthConnectResult(authorizationUrl));
    }
}
