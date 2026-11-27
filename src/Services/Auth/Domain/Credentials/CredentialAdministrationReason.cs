using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Motivo de una acción de la oficina (03 §4.3). La nota libre vive solo en el usuario, nunca en la auditoría.</summary>
public sealed record CredentialAdministrationReason(CredentialAdministrationReasonCode Code, string? Note)
{
    public const int MaxNoteLength = 256;

    /// <summary>Con <c>Other</c> la nota es obligatoria; la nota se recorta y se limita a <see cref="MaxNoteLength"/>.</summary>
    public static Result<CredentialAdministrationReason> Create(CredentialAdministrationReasonCode code, string? note)
    {
        if (!Enum.IsDefined(code))
            return Result.Failure<CredentialAdministrationReason>(CredentialAdministrationErrors.InvalidReason);

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (code == CredentialAdministrationReasonCode.Other && trimmed is null)
            return Result.Failure<CredentialAdministrationReason>(CredentialAdministrationErrors.ReasonRequired);

        if (trimmed is { Length: > MaxNoteLength })
            trimmed = trimmed[..MaxNoteLength];
        return Result.Success(new CredentialAdministrationReason(code, trimmed));
    }
}
