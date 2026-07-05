using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.ServiceTokens.Commands;

/// <summary>
/// Grant client-credentials (M2M): valida clientId+secret y emite un token de servicio corto para un
/// tenant, con los permisos configurados para ese cliente.
/// </summary>
public sealed record IssueServiceTokenCommand(string ClientId, string ClientSecret, Guid TenantId);

public sealed record ServiceTokenResponse(string AccessToken, int ExpiresInSeconds, string TokenType = "Bearer");

public static class IssueServiceTokenHandler
{
    public static Task<Result<ServiceTokenResponse>> Handle(
        IssueServiceTokenCommand command,
        IJwtTokenGenerator tokens,
        IOptions<ServiceAuthOptions> options,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Task.FromResult(Result.Failure<ServiceTokenResponse>(new Error("Auth.InvalidClient", "A tenant is required.")));

        var settings = options.Value;
        var client = settings.Clients.FirstOrDefault(c =>
            string.Equals(c.ClientId, command.ClientId, StringComparison.Ordinal)
        );

        // Comparación en tiempo constante; misma respuesta si el cliente no existe o el secreto no coincide.
        if (client is null || !SecretMatches(client.Secret, command.ClientSecret))
            return Task.FromResult(
                Result.Failure<ServiceTokenResponse>(new Error("Auth.InvalidClient", "Invalid service client credentials."))
            );

        var token = tokens.GenerateServiceToken(
            command.TenantId,
            client.ClientId,
            client.Permissions,
            settings.TokenLifetimeMinutes
        );

        return Task.FromResult(Result.Success(new ServiceTokenResponse(token.Token, token.ExpiresInSeconds)));
    }

    private static bool SecretMatches(string configured, string provided) =>
        !string.IsNullOrEmpty(configured)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configured),
            Encoding.UTF8.GetBytes(provided ?? string.Empty)
        );
}
