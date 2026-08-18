using BuildingBlocks.Results;

namespace TaxVision.Notes.Domain.ValueObjects;

/// <summary>Paleta fija de colores semánticos — el mapeo <see cref="NoteColorKind"/> → hex vive en el frontend.</summary>
public enum NoteColorKind
{
    Default = 0,
    Important = 1,
    FollowUp = 2,
    Idea = 3,
    Warning = 4,
    Info = 5,
}

/// <summary>
/// Color opcional de una nota (VO semántico, no hex crudo — recomendación de la investigación de
/// 00_Overview). Siempre construible: no hay ningún <see cref="NoteColorKind"/> inválido.
/// </summary>
public sealed record NoteColor(NoteColorKind Kind)
{
    public static Result<NoteColor> Create(NoteColorKind kind) => Result.Success(new NoteColor(kind));
}
