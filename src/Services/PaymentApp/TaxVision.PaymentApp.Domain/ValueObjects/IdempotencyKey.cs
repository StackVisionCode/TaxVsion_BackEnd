using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Domain.ValueObjects;

/// <summary>
/// Clave de idempotencia de un intento de cobro. En el flujo de renewals viene tal cual
/// de Subscription (formato <c>subscription-renewal-{id:N}-{yyyyMMdd}</c>, etc.) — este VO
/// no reinterpreta el formato, solo garantiza que nunca esté vacío ni sea absurdamente largo.
/// </summary>
public sealed record IdempotencyKey
{
    public string Value { get; }

    private IdempotencyKey(string value) => Value = value;

    public static Result<IdempotencyKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<IdempotencyKey>(new Error("IdempotencyKey.Empty", "IdempotencyKey is required."));

        if (value.Length > 200)
            return Result.Failure<IdempotencyKey>(new Error("IdempotencyKey.TooLong", "IdempotencyKey must be 200 characters or fewer."));

        return Result.Success(new IdempotencyKey(value.Trim()));
    }

    public override string ToString() => Value;
}
