using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Verifica la respuesta del firmante contra el challenge activo. Fases: (1) resolver
/// token, (2) validar formato mínimo, (3) hash-verify vs challenge activo, (4) delegar
/// mutación al aggregate, (5) persistir, (6) publicar evento verified/failed.
/// </summary>
public static class VerifyChallengeHandler
{
    public static async Task<Result> Handle(
        VerifyChallengeCommand cmd,
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
        if (string.IsNullOrWhiteSpace(cmd.Answer))
            return Result.Failure(new Error("Signature.Public.AnswerEmpty", "Answer is required."));

        var attemptedAt = DateTime.UtcNow;
        var current = signer.CurrentChallengeFor(cmd.Method, attemptedAt);
        var isMatch = current is not null && hasher.Verify(cmd.Answer.Trim(), current.AnswerHash);
        var result = request.VerifyVerificationChallenge(
            signer.Id,
            cmd.Method,
            isMatch,
            attemptedAt,
            cmd.ClientIp,
            cmd.UserAgent
        );
        await unitOfWork.SaveChangesAsync(ct);
        await PublishOutcomeAsync(
            request,
            signer,
            cmd.Method,
            result.IsSuccess,
            attemptedAt,
            cmd.ClientIp,
            correlation,
            bus
        );
        return result;
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static Task PublishOutcomeAsync(
        SignatureRequest request,
        Signer signer,
        SignerVerificationMethod method,
        bool wasSuccess,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        wasSuccess
            ? PublishSucceededAsync(request, signer, method, attemptedAtUtc, clientIp, correlation, bus)
            : PublishFailedAsync(request, signer, method, attemptedAtUtc, clientIp, correlation, bus);

    private static Task PublishSucceededAsync(
        SignatureRequest request,
        Signer signer,
        SignerVerificationMethod method,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerVerificationSucceededIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    Method = method.ToString(),
                    VerifiedAtUtc = attemptedAtUtc,
                    ClientIp = clientIp,
                }
            )
            .AsTask();

    private static Task PublishFailedAsync(
        SignatureRequest request,
        Signer signer,
        SignerVerificationMethod method,
        DateTime attemptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerVerificationFailedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    Method = method.ToString(),
                    AttemptedAtUtc = attemptedAtUtc,
                    FailedAttempts = signer.PinFailedAttempts,
                    LockedUntilUtc = signer.PinLockedUntilUtc,
                    ClientIp = clientIp,
                }
            )
            .AsTask();
}
