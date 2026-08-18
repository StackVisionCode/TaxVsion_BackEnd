using BuildingBlocks.Results;
using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Domain.ValueObjects;

/// <summary>
/// Referencia polimórfica al target de una nota (ADR-03) — 1 target por nota en v1 (patrón
/// Salesforce ContentNote / HubSpot associations). <c>TargetId</c> es <c>null</c> únicamente
/// cuando <see cref="NoteTargetType.None"/> (nota "suelta", sin target). No valida la existencia
/// del target — es cross-context, se resuelve por proyección/BFF (ADR-09).
/// </summary>
public sealed record NoteReference
{
    public NoteTargetType TargetType { get; }
    public Guid? TargetId { get; }

    private NoteReference(NoteTargetType targetType, Guid? targetId)
    {
        TargetType = targetType;
        TargetId = targetId;
    }

    public static Result<NoteReference> Create(NoteTargetType targetType, Guid? targetId)
    {
        if (targetType == NoteTargetType.None)
            return Result.Success(new NoteReference(NoteTargetType.None, null));

        if (targetId is null || targetId == Guid.Empty)
            return Result.Failure<NoteReference>(NoteErrors.ReferenceTargetRequired);

        return Result.Success(new NoteReference(targetType, targetId));
    }
}
