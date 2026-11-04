using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Offboarding;

/// <summary>
/// Ficha del retiro: quién lo hizo, a quién pasó el trabajo y, si la oficina quiere, el motivo. Es historia
/// (no se reasigna ni se borra): sirve para auditorías y para el WISP (quién cortó el acceso y cuándo).
/// </summary>
public sealed record OffboardingDetails
{
    public const int ReasonNoteMaxLength = 500;

    private OffboardingDetails(Guid offboardedByUserId, Guid? successorUserId, OffboardingReason? reason, string? note)
    {
        OffboardedByUserId = offboardedByUserId;
        SuccessorUserId = successorUserId;
        Reason = reason;
        ReasonNote = note;
    }

    [UserReference(UserReferenceKind.Provenance, Note = "Quién hizo el retiro: historia para auditoría.")]
    public Guid OffboardedByUserId { get; }

    [UserReference(UserReferenceKind.Provenance, Note = "A quién pasó el trabajo: historia, no se reasigna.")]
    public Guid? SuccessorUserId { get; }
    public OffboardingReason? Reason { get; }
    public string? ReasonNote { get; }

    public static Result<OffboardingDetails> Create(
        Guid offboardedByUserId,
        Guid? successorUserId,
        OffboardingReason? reason,
        string? reasonNote
    )
    {
        if (offboardedByUserId == Guid.Empty)
            return Result.Failure<OffboardingDetails>(
                new Error("Offboarding.InvalidActor", "An offboarding needs the person who did it.")
            );
        if (reason is { } value && !Enum.IsDefined(value))
            return Result.Failure<OffboardingDetails>(
                new Error("Offboarding.InvalidReason", "Choose one of the listed reasons.")
            );

        var note = string.IsNullOrWhiteSpace(reasonNote) ? null : reasonNote.Trim();
        if (note is { Length: > ReasonNoteMaxLength })
            return Result.Failure<OffboardingDetails>(
                new Error(
                    "Offboarding.ReasonNoteTooLong",
                    $"The note can't be longer than {ReasonNoteMaxLength} characters."
                )
            );

        return Result.Success(new OffboardingDetails(offboardedByUserId, successorUserId, reason, note));
    }
}
