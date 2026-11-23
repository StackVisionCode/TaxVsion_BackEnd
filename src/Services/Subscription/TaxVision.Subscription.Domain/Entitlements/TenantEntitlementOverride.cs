using System.Globalization;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Entitlements;

/// <summary>
/// Plan a la medida: un valor propio de UNA oficina para un límite, encima de su plan.
/// <para>
/// Es su propio agregado (uno por oficina y clave) y no vive dentro de la suscripción a propósito: no cambia lo
/// que se cobra, ni la edición contratada, ni el ciclo de renovación. Solo cambia la foto de entitlements. Así un
/// error al darlo nunca puede tocar un cobro, y quitarlo devuelve la oficina exactamente a su plan.
/// </para>
/// <para>
/// Las reglas viven aquí y no en el controlador: el valor debe caber en el tipo del límite, el motivo es
/// obligatorio (queda en la auditoría) y el vencimiento, si hay, está en el futuro. Todo se devuelve como
/// <see cref="Result"/>; ninguna regla de negocio lanza excepciones.
/// </para>
/// </summary>
public sealed class TenantEntitlementOverride : TenantEntity
{
    private const int MaxTextLength = 500;

    public string Key { get; private set; } = default!;
    public EntitlementValueType ValueType { get; private set; }

    /// <summary>Escrito igual que lo escribe la foto de entitlements, para que los consumidores lo lean igual.</summary>
    public string Value { get; private set; } = default!;
    public string Reason { get; private set; } = default!;

    /// <summary>null = hasta que se quite a mano.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>Cuándo se recalculó la oficina por el vencimiento. Evita recalcularla en cada pasada del job.</summary>
    public DateTime? ExpiryAppliedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid CreatedBy { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid UpdatedBy { get; private set; }

    private TenantEntitlementOverride() { }

    public static Result<TenantEntitlementOverride> Grant(
        Guid tenantId,
        EntitlementKey key,
        EntitlementValueType valueType,
        string value,
        string reason,
        DateTime? expiresAtUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var terms = ValidateTerms(valueType, value, reason, expiresAtUtc, nowUtc);
        if (terms.IsFailure)
            return Result.Failure<TenantEntitlementOverride>(terms.Error);

        var item = new TenantEntitlementOverride
        {
            Key = key.Value,
            ValueType = valueType,
            Value = terms.Value.Value,
            Reason = terms.Value.Reason,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        item.SetTenant(tenantId);
        return Result.Success(item);
    }

    /// <summary>
    /// Cambia valor, motivo y vencimiento de golpe. Un vencimiento nuevo vuelve a quedar pendiente: si se
    /// extiende una excepción ya vencida, el job la vuelve a vigilar.
    /// </summary>
    public Result Change(string value, string reason, DateTime? expiresAtUtc, Guid actorUserId, DateTime nowUtc)
    {
        var terms = ValidateTerms(ValueType, value, reason, expiresAtUtc, nowUtc);
        if (terms.IsFailure)
            return Result.Failure(terms.Error);

        Value = terms.Value.Value;
        Reason = terms.Value.Reason;
        ExpiresAtUtc = expiresAtUtc;
        ExpiryAppliedAtUtc = null;
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
        return Result.Success();
    }

    /// <summary>El vencimiento es exclusivo: en el instante exacto ya no aplica.</summary>
    public bool IsInForceAt(DateTime nowUtc) => ExpiresAtUtc is null || ExpiresAtUtc > nowUtc;

    /// <summary>
    /// El job marca que ya recalculó la oficina por este vencimiento. Idempotente: repetirlo no cambia la
    /// fecha, así una pasada repetida no publica dos recálculos.
    /// </summary>
    public Result MarkExpiryApplied(DateTime nowUtc)
    {
        if (IsInForceAt(nowUtc))
            return Result.Failure(new Error("EntitlementOverride.NotExpired", "The override is still in force."));

        ExpiryAppliedAtUtc ??= nowUtc;
        return Result.Success();
    }

    /// <summary>La línea que entra a la foto. <see cref="EntitlementSource.Override"/> la distingue del plan y de los add-ons.</summary>
    public EntitlementEntry ToEntry() =>
        new(
            EntitlementKey.Create(Key).Value,
            ValueType,
            Value,
            EntitlementStatus.Active,
            EntitlementSource.Override,
            ExpiresAtUtc
        );

    private static Result<(string Value, string Reason)> ValidateTerms(
        EntitlementValueType valueType,
        string value,
        string reason,
        DateTime? expiresAtUtc,
        DateTime nowUtc
    )
    {
        var normalized = Normalize(valueType, value);
        if (normalized is null)
            return Result.Failure<(string, string)>(
                new Error("EntitlementOverride.InvalidValue", $"The value does not fit a {valueType} limit.")
            );

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length is 0 or > MaxTextLength)
            return Result.Failure<(string, string)>(
                new Error("EntitlementOverride.ReasonRequired", "A reason of up to 500 characters is required.")
            );

        if (expiresAtUtc is { } expiry && expiry <= nowUtc)
            return Result.Failure<(string, string)>(
                new Error("EntitlementOverride.ExpiryInPast", "The expiry must be in the future.")
            );

        return Result.Success((normalized, trimmedReason));
    }

    // Límites y cuotas no negativos; texto invariante para que "1.5" signifique lo mismo en cualquier servidor.
    private static string? Normalize(EntitlementValueType valueType, string value)
    {
        var text = value?.Trim() ?? string.Empty;
        var invariant = CultureInfo.InvariantCulture;
        return valueType switch
        {
            EntitlementValueType.Bool when bool.TryParse(text, out var flag) => flag ? "true" : "false",
            EntitlementValueType.Int when int.TryParse(text, NumberStyles.None, invariant, out var number) =>
                number.ToString(invariant),
            EntitlementValueType.Long when long.TryParse(text, NumberStyles.None, invariant, out var number) =>
                number.ToString(invariant),
            EntitlementValueType.Decimal
                when decimal.TryParse(text, NumberStyles.AllowDecimalPoint, invariant, out var number) =>
                number.ToString(invariant),
            EntitlementValueType.String when text.Length is > 0 and <= MaxTextLength => text,
            _ => null,
        };
    }
}
