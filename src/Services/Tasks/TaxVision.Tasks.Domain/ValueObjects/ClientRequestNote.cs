using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ValueObjects;

/// <summary>
/// Qué se le pide al cliente: «falta el W-2 y el 1099-INT». Es un VO y no un <c>string?</c> porque
/// el texto termina dentro del correo al cliente y sin él el mensaje no sirve.
///
/// <para>Llega ya sanitizado: acá sólo se valida no vacío y longitud.</para>
/// </summary>
public sealed record ClientRequestNote
{
    public const int MaxLength = 2_000;

    public string Value { get; }

    private ClientRequestNote(string value) => Value = value;

    public static Result<ClientRequestNote> Create(string? sanitizedValue)
    {
        if (string.IsNullOrWhiteSpace(sanitizedValue))
            return Result.Failure<ClientRequestNote>(TaskErrors.WaitingOnClient.ExpectedItemsRequired);

        var trimmed = sanitizedValue.Trim();
        return trimmed.Length > MaxLength
            ? Result.Failure<ClientRequestNote>(TaskErrors.WaitingOnClient.ExpectedItemsTooLong)
            : Result.Success(new ClientRequestNote(trimmed));
    }

    public override string ToString() => Value;
}
