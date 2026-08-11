using BuildingBlocks.Results;

namespace TaxVision.Sms.Domain.ValueObjects;

/// <summary>Texto del mensaje. No vacío; tope defensivo generoso (mensajes concatenados). La
/// segmentación GSM-7/UCS-2 real es un servicio aparte que se puede sumar sin tocar este VO.</summary>
public sealed record SmsBody
{
    public const int MaxLength = 4096;

    public string Value { get; }

    private SmsBody(string value) => Value = value;

    public static Result<SmsBody> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<SmsBody>(SmsErrors.InvalidBody);

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxLength)
            return Result.Failure<SmsBody>(SmsErrors.InvalidBody);

        return Result.Success(new SmsBody(trimmed));
    }

    public override string ToString() => Value;
}
