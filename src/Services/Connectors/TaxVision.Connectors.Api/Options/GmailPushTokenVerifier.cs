using Google.Apis.Auth;

namespace TaxVision.Connectors.Api.Options;

/// <summary>Valida el JWT OIDC de Pub/Sub. Lanza <see cref="InvalidJwtException"/> si no pasa.</summary>
public interface IGmailPushTokenVerifier
{
    /// <param name="token">El JWT, sin el prefijo Bearer.</param>
    /// <param name="audience">Audiencia exigida; null valida solo la firma.</param>
    Task VerifyAsync(string token, string? audience);
}

public sealed class GoogleGmailPushTokenVerifier : IGmailPushTokenVerifier
{
    public Task VerifyAsync(string token, string? audience) =>
        GoogleJsonWebSignature.ValidateAsync(
            token,
            new GoogleJsonWebSignature.ValidationSettings { Audience = audience is null ? null : [audience] }
        );
}
