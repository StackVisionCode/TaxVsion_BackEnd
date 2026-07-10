using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Emite un challenge genérico. Fases: (1) resolver token + firmante, (2) generar OTP,
/// (3) hashear, (4) delegar la emisión al aggregate, (5) persistir, (6) publicar el
/// evento externo con el valor en claro para el courier microservicio.
/// </summary>
public static class IssueVerificationChallengeHandler
{
    private const int OtpLength = 6;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);

    public static async Task<Result> Handle(
        IssueVerificationChallengeCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IPinHasher hasher,
        IOtpCodeGenerator codeGenerator,
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
        var deliveryAddress = ResolveDeliveryAddress(cmd.Method, signer);
        if (deliveryAddress is null)
            return Result.Failure(
                new Error(
                    "Signature.Signer.NoDeliveryAddress",
                    "Signer does not have a delivery address for the requested method."
                )
            );

        var plaintext = codeGenerator.Generate(OtpLength);
        var hash = hasher.Hash(plaintext);
        var issuedAt = DateTime.UtcNow;

        var issueResult = request.IssueVerificationChallenge(signer.Id, cmd.Method, hash, issuedAt, ChallengeLifetime);
        if (issueResult.IsFailure)
            return Result.Failure(issueResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await PublishChallengeAsync(
            request,
            signer,
            cmd.Method,
            plaintext,
            deliveryAddress,
            issueResult.Value.ExpiresAtUtc,
            correlation,
            bus
        );
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static string? ResolveDeliveryAddress(SignerVerificationMethod method, Signer signer) =>
        method switch
        {
            SignerVerificationMethod.SmsOtp => signer.PhoneNumber?.Value,
            SignerVerificationMethod.WhatsAppOtp => signer.PhoneNumber?.Value,
            SignerVerificationMethod.EmailOtp => signer.Email.Value,
            SignerVerificationMethod.KbaQuiz => signer.Email.Value,
            _ => null,
        };

    private static Task PublishChallengeAsync(
        SignatureRequest request,
        Signer signer,
        SignerVerificationMethod method,
        string plaintext,
        string deliveryAddress,
        DateTime expiresAtUtc,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerVerificationChallengeIssuedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    SignerId = signer.Id,
                    Method = method.ToString(),
                    DeliveryAddress = deliveryAddress,
                    PlaintextAnswer = plaintext,
                    SignerFullName = signer.FullName.Value,
                    SignerLanguage = "En",
                    ExpiresAtUtc = expiresAtUtc,
                }
            )
            .AsTask();
}
