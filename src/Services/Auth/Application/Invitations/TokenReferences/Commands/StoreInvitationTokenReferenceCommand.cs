using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Invitations.TokenReferences.Commands;

public sealed record StoreInvitationTokenReferenceCommand(string RawToken);

public sealed record StoreInvitationTokenReferenceResponse(Guid Reference);

/// <summary>Fase 18 — lado de escritura del patrón TokenReference aplicado a AdminInvitationRawToken:
/// Tenant genera el token de activación del owner y lo deposita acá antes de publicar
/// TenantCreatedIntegrationEvent, para no mandar el raw token por RabbitMQ (mismo patrón que
/// Onboarding, Fase 9 — reusa el mismo ITokenReferenceStore, sin necesidad de un store nuevo).
/// TTL 30s, one-shot — TenantCreatedConsumer lo consume in-process en Auth.</summary>
public static class StoreInvitationTokenReferenceHandler
{
    public static async Task<Result<StoreInvitationTokenReferenceResponse>> Handle(
        StoreInvitationTokenReferenceCommand command,
        ITokenReferenceStore tokenReferences,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.RawToken))
            return Result.Failure<StoreInvitationTokenReferenceResponse>(
                new Error("Auth.InvalidToken", "RawToken is required.")
            );

        var reference = await tokenReferences.StoreAsync(command.RawToken, ct);
        return Result.Success(new StoreInvitationTokenReferenceResponse(reference));
    }
}
