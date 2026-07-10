using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Reto de verificación emitido a un firmante para un canal concreto (SMS OTP, email OTP,
/// WhatsApp OTP, KBA). El PIN del preparer NO usa esta entidad — vive como propiedad
/// compartida en <see cref="SignatureRequest.PractitionerPinHash"/>.
///
/// <para>
/// El hash del challenge lo produce la capa Application con <c>IPinHasher</c> (mismo
/// PBKDF2). El dominio sólo maneja hashes, timestamps y "consumido".
/// </para>
///
/// <para>
/// Entidad interna del aggregate: sólo se crea/muta via métodos del root
/// <see cref="SignatureRequest"/>.
/// </para>
/// </summary>
public sealed class SignerVerificationChallenge : BaseEntity
{
    public const int MaxAnswerHashLength = 512;

    private SignerVerificationChallenge() { }

    public Guid SignerId { get; private set; }
    public SignerVerificationMethod Method { get; private set; }
    public string AnswerHash { get; private set; } = default!;
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc is not null;

    public bool IsExpiredAt(DateTime now) => now >= ExpiresAtUtc;

    public bool IsActiveAt(DateTime now) => !IsConsumed && !IsExpiredAt(now);

    internal static Result<SignerVerificationChallenge> Create(
        Guid signerId,
        SignerVerificationMethod method,
        string answerHash,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc
    )
    {
        if (signerId == Guid.Empty)
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Challenge.Signer", "SignerId is required.")
            );
        if (string.IsNullOrWhiteSpace(answerHash))
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Challenge.Hash", "Answer hash is required.")
            );
        if (answerHash.Length > MaxAnswerHashLength)
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Challenge.HashLength", "Answer hash is too long.")
            );
        if (expiresAtUtc <= issuedAtUtc)
            return Result.Failure<SignerVerificationChallenge>(
                new Error("Signature.Challenge.Expiry", "ExpiresAt must be later than IssuedAt.")
            );

        return Result.Success(
            new SignerVerificationChallenge
            {
                Id = Guid.NewGuid(),
                SignerId = signerId,
                Method = method,
                AnswerHash = answerHash,
                IssuedAtUtc = issuedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
            }
        );
    }

    internal void MarkConsumed(DateTime consumedAtUtc)
    {
        if (IsConsumed)
            return;
        ConsumedAtUtc = consumedAtUtc;
    }
}
