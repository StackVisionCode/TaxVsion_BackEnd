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
}
