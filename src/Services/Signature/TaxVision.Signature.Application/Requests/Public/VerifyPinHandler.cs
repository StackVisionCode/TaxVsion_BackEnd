using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// El firmante público envía el PIN recibido por canal fuera-de-banda del preparer.
/// Fases: (1) validar token + epoch, (2) validar formato del PIN, (3) comparar hash
/// con timing-safe, (4) delegar la mutación al aggregate, (5) persistir, (6) publicar
/// evento verified/failed. Un fallo NO libera detalle diagnóstico al cliente para no
/// facilitar enumeración.
/// </summary>
public static class VerifyPinHandler
{
    public static async Task<Result> Handle(
        VerifyPinCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IPinHasher hasher,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, ct);
        if (resolution.IsFailure)
            return Result.Failure(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        var pinValidation = PractitionerPin.Create(cmd.Pin);
        if (pinValidation.IsFailure)
            return pinValidation;

        var isMatch = MatchPinAgainstHash(request, pinValidation.Value.Value, hasher);
        var attemptedAt = DateTime.UtcNow;
        var verify = request.VerifySignerWithPin(signer.Id, isMatch, attemptedAt, cmd.ClientIp, cmd.UserAgent);
        await unitOfWork.SaveChangesAsync(ct);
        await PublishOutcomeAsync(request, signer, verify.IsSuccess, attemptedAt, cmd.ClientIp, correlation, bus);
        return verify;
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static bool MatchPinAgainstHash(SignatureRequest request, string plaintext, IPinHasher hasher)
    {
        if (request.PractitionerPinHash is null)
            return false;
        return hasher.Verify(plaintext, request.PractitionerPinHash);
    }

    private static Task PublishOutcomeAsync(
        SignatureRequest request,
        Signer signer,
        bool wasSuccess,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        wasSuccess
            ? PublishVerifiedAsync(request, signer, attemptedAtUtc, clientIp, correlation, bus)
            : PublishFailedAsync(request, signer, attemptedAtUtc, clientIp, correlation, bus);

    private static Task PublishVerifiedAsync(
        SignatureRequest request,
        Signer signer,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerPinVerifiedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    VerifiedAtUtc = attemptedAtUtc,
                    ClientIp = clientIp,
                }
            )
            .AsTask();

    private static Task PublishFailedAsync(
        SignatureRequest request,
        Signer signer,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerPinFailedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    AttemptedAtUtc = attemptedAtUtc,
                    FailedAttempts = signer.PinFailedAttempts,
                    LockedUntilUtc = signer.PinLockedUntilUtc,
                    ClientIp = clientIp,
                }
            )
            .AsTask();
}
