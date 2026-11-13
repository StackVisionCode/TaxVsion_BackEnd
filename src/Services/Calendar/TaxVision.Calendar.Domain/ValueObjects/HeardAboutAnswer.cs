using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Respuesta a "How did you hear about us?": el código de la opción (no su texto, así renombrar una etiqueta no
/// rompe los reportes) y un detalle corto cuando la opción lo admite ("Other").
/// </summary>
public sealed partial record HeardAboutAnswer
{
    public const int MaxDetailLength = 100;

    public string Code { get; }

    public string? Detail { get; }

    private HeardAboutAnswer(string code, string? detail)
    {
        Code = code;
        Detail = detail;
    }

    public static Result<HeardAboutAnswer> Create(string? code, string? detail, bool allowsDetail)
    {
        if (!IsValidCode(code))
            return Result.Failure<HeardAboutAnswer>(PublicBookingErrors.HeardAboutRequired);

        var text = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        if (text is not null && !allowsDetail)
            return Result.Failure<HeardAboutAnswer>(PublicBookingErrors.HeardAboutDetailNotAllowed);
        if (text is not null && (text.Length > MaxDetailLength || PlainText.HasControlCharacters(text)))
            return Result.Failure<HeardAboutAnswer>(PublicBookingErrors.HeardAboutDetailTooLong);

        return Result.Success(new HeardAboutAnswer(code!, text));
    }

    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex CodePattern();
}
