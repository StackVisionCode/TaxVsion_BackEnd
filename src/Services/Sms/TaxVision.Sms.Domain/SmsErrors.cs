using BuildingBlocks.Results;

namespace TaxVision.Sms.Domain;

/// <summary>Códigos de error canónicos del dominio SMS. Los códigos de media/proveedor son estables
/// (viajan al caller en `results[].errorCode`) e independientes del proveedor concreto.</summary>
public static class SmsErrors
{
    // Validación de entrada
    public static Error InvalidTenant => new("sms.invalidTenant", "TenantId is required.");
    public static Error InvalidCustomer => new("sms.invalidCustomer", "CustomerId is required.");
    public static Error InvalidDestination => new("sms.invalidDestination", "A valid E.164 destination is required.");
    public static Error InvalidBody => new("sms.invalidBody", "Message body is required.");
    public static Error InvalidIdempotencyKey => new("sms.invalidIdempotencyKey", "IdempotencyKey is invalid.");

    // Visibilidad por asignación: el cliente no es de quien envía. Se rechaza el item, no el lote —
    // mismo criterio que el resto de las validaciones de entrada.
    public static Error CustomerNotAssigned =>
        new("sms.customerNotAssigned", "The customer is not assigned to the sender.");

    // Media (estables, agnósticos del proveedor)
    public static Error MediaNotSupported => new("mediaNotSupported", "The selected provider does not support media.");
    public static Error MultipleMediaNotSupported =>
        new("multipleMediaNotSupported", "The provider supports at most one media item.");
    public static Error MediaCountExceeded => new("mediaCountExceeded", "Too many media items for the provider.");
    public static Error MediaTooLarge => new("mediaTooLarge", "A media item exceeds the provider size limit.");
    public static Error MediaTypeNotSupported =>
        new("mediaTypeNotSupported", "A media content type is not supported by the provider.");
    public static Error InvalidMedia => new("sms.invalidMedia", "A media reference is invalid.");

    // Proveedor
    public static Error ProviderRejected => new("providerRejected", "The provider rejected the message.");
    public static Error ProviderUnavailable => new("providerUnavailable", "The provider is unavailable.");

    // Estado
    public static Error InvalidTransition => new("sms.invalidTransition", "Invalid status transition.");

    // Búsqueda
    public static Error MessageNotFound => new("sms.messageNotFound", "SMS message not found.");

    // Cobro money-OUT (Wallet PEP): el envío individual se cobra reservando antes de despachar. Si el Wallet
    // no responde → 503; si no alcanza el saldo → 402 con el faltante. (El envío de campaña NO pasa por acá:
    // ya lo cobró Campaigns.)
    public static Error WalletUnavailable =>
        new("sms.walletUnavailable", "No se pudo autorizar el cobro del monedero; intentá de nuevo en unos segundos.");

    public static Error InsufficientFunds(long deficitMicros, string currency)
    {
        var dollars = deficitMicros / 1_000_000m;
        return new Error(
            "sms.insufficientFunds",
            $"Saldo insuficiente para enviar: faltan {dollars:0.######} {currency} ({deficitMicros} micros)."
        );
    }
}
