using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Subscription.Domain.Billing;

/// <summary>
/// Elección del tenant sobre cómo quiere pagar las renovaciones de su suscripción:
/// Automatic (plataforma cobra off-session) o Manual (el tenant paga tras un aviso).
/// Único registro por tenant. Cuando el modo es Automatic, el agregado conserva la
/// cadena completa de <see cref="TenantPaymentMandate"/> aceptados: el actual activo
/// y los anteriores revocados — evidencia legal del consentimiento vigente al momento
/// de cada cobro.
/// </summary>
public sealed class TenantBillingPreference : TenantEntity
{
    private readonly List<TenantPaymentMandate> _mandates = [];

    public BillingMode Mode { get; private set; }
    public DateTime ChosenAtUtc { get; private set; }
    public Guid ChosenByUserId { get; private set; }

    /// <summary>Null cuando el modo es Manual; "stripe"/"paypal"/... cuando es Automatic.</summary>
    public string? ProviderCode { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<TenantPaymentMandate> Mandates => _mandates;

    private TenantBillingPreference() { }

    public static Result<TenantBillingPreference> Choose(
        Guid tenantId,
        BillingMode mode,
        string? providerCode,
        Guid chosenByUserId,
        string? consentText,
        string? consentIpAddress,
        string? consentUserAgent,
        long amountCents,
        string currency,
        string cadence,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantBillingPreference>(
                new Error("BillingPreference.InvalidTenant", "TenantId is required.")
            );

        if (chosenByUserId == Guid.Empty)
            return Result.Failure<TenantBillingPreference>(
                new Error("BillingPreference.InvalidActor", "ChosenByUserId is required.")
            );

        var preference = new TenantBillingPreference
        {
            Mode = mode,
            ChosenAtUtc = nowUtc,
            ChosenByUserId = chosenByUserId,
            ProviderCode = mode == BillingMode.Automatic ? providerCode : null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        preference.SetTenant(tenantId);

        if (mode == BillingMode.Automatic)
        {
            var mandateResult = TenantPaymentMandate.Create(
                preference.Id,
                tenantId,
                providerCode ?? string.Empty,
                consentText ?? string.Empty,
                consentIpAddress ?? string.Empty,
                consentUserAgent,
                chosenByUserId,
                amountCents,
                currency,
                cadence,
                nowUtc
            );
            if (mandateResult.IsFailure)
                return Result.Failure<TenantBillingPreference>(mandateResult.Error);

            preference._mandates.Add(mandateResult.Value);
        }

        return Result.Success(preference);
    }

    /// <summary>Pasa el tenant a cobro automático y registra el consent del nuevo mandate.
    /// Idempotente: si ya está en Automatic con el mismo proveedor y hay un mandate activo,
    /// no agrega otro — un segundo click no debe producir dos filas de evidencia.</summary>
    public Result SwitchToAutomatic(
        string providerCode,
        Guid actorUserId,
        string consentText,
        string consentIpAddress,
        string? consentUserAgent,
        long amountCents,
        string currency,
        string cadence,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(providerCode))
            return Result.Failure(
                new Error("BillingPreference.InvalidProvider", "ProviderCode is required for automatic mode.")
            );

        if (actorUserId == Guid.Empty)
            return Result.Failure(new Error("BillingPreference.InvalidActor", "ActorUserId is required."));

        var active = ActiveMandate();
        if (Mode == BillingMode.Automatic && active is not null && active.ProviderCode == providerCode)
            return Result.Success();

        active?.Revoke("SwitchedProvider", nowUtc);

        var mandateResult = TenantPaymentMandate.Create(
            Id,
            TenantId,
            providerCode,
            consentText,
            consentIpAddress,
            consentUserAgent,
            actorUserId,
            amountCents,
            currency,
            cadence,
            nowUtc
        );
        if (mandateResult.IsFailure)
            return Result.Failure(mandateResult.Error);

        _mandates.Add(mandateResult.Value);
        Mode = BillingMode.Automatic;
        ProviderCode = providerCode;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Pasa el tenant a aviso-y-pago-manual: revoca el mandate activo (si hay)
    /// para dejar de intentar cobros off-session. Idempotente.</summary>
    public Result SwitchToManual(Guid actorUserId, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure(new Error("BillingPreference.InvalidActor", "ActorUserId is required."));

        if (Mode == BillingMode.Manual)
            return Result.Success();

        var active = ActiveMandate();
        active?.Revoke("SwitchedToManual", nowUtc);

        Mode = BillingMode.Manual;
        ProviderCode = null;
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Cuando sube el precio o cambia la cadencia, el consent anterior deja de cubrir
    /// el nuevo cargo (regulación de pagos recurrentes). Se revoca el activo y se crea uno
    /// nuevo con el monto/cadencia nuevos, manteniendo el histórico.</summary>
    public Result RecordPriceChangeMandate(
        Guid actorUserId,
        string consentText,
        string consentIpAddress,
        string? consentUserAgent,
        long newAmountCents,
        string currency,
        string cadence,
        DateTime nowUtc
    )
    {
        if (Mode != BillingMode.Automatic)
            return Result.Failure(
                new Error(
                    "BillingPreference.NotAutomatic",
                    "Price change mandates only apply when billing is automatic."
                )
            );

        if (string.IsNullOrEmpty(ProviderCode))
            return Result.Failure(
                new Error("BillingPreference.MissingProvider", "ProviderCode must be set when switching mandates.")
            );

        if (actorUserId == Guid.Empty)
            return Result.Failure(new Error("BillingPreference.InvalidActor", "ActorUserId is required."));

        var active = ActiveMandate();
        active?.Revoke("PriceChange", nowUtc);

        var mandateResult = TenantPaymentMandate.Create(
            Id,
            TenantId,
            ProviderCode,
            consentText,
            consentIpAddress,
            consentUserAgent,
            actorUserId,
            newAmountCents,
            currency,
            cadence,
            nowUtc
        );
        if (mandateResult.IsFailure)
            return Result.Failure(mandateResult.Error);

        _mandates.Add(mandateResult.Value);
        Touch(nowUtc);
        return Result.Success();
    }

    /// <summary>Mandate vigente — el único no revocado, más reciente. Null si está en Manual
    /// o si todos los mandates fueron revocados.</summary>
    public TenantPaymentMandate? ActiveMandate()
    {
        TenantPaymentMandate? found = null;
        foreach (var mandate in _mandates)
        {
            if (mandate.IsRevoked)
                continue;
            if (found is null || mandate.AcceptedAtUtc > found.AcceptedAtUtc)
                found = mandate;
        }
        return found;
    }

    private void Touch(DateTime nowUtc) => UpdatedAtUtc = nowUtc;
}
