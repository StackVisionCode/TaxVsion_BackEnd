using BuildingBlocks.Results;

namespace TaxVision.Sms.Application.Providers;

/// <summary>
/// Adapter de un proveedor SMS/MMS. Agnóstico: el dominio y los handlers nunca conocen un proveedor
/// concreto — solo esta interfaz. Todas las operaciones esperables devuelven <see cref="Result"/> y NO
/// lanzan por fallos normales del proveedor. Se registra con <see cref="SmsProviderAttribute"/> y se
/// resuelve por <see cref="ISmsAdapterFactory"/> (keyed DI por <see cref="Code"/>). Agregar un proveedor
/// = una clase con el atributo, sin tocar factory, dominio ni handlers.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Código estable del adapter (ej. "generic", "fake", "twilio"). Es la key de keyed-DI.</summary>
    string Code { get; }

    SmsProviderCapabilities Capabilities { get; }

    /// <summary>Transforma el request canónico al formato del proveedor y hace el POST (un mensaje).</summary>
    Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default);

    /// <summary>Envío en lote. Si <see cref="SmsProviderCapabilities.SupportsBulkSend"/> es false, el
    /// caller hace loop sobre <see cref="SendAsync"/>; los adapters con bulk nativo lo agrupan.</summary>
    Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(
        IReadOnlyList<SmsSendRequest> requests,
        CancellationToken ct = default
    );

    /// <summary>Verifica la firma (HMAC/etc.) del webhook con el secreto del proveedor.</summary>
    Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret);

    /// <summary>Transforma el DLR/estado del proveedor al modelo canónico.</summary>
    Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload);

    /// <summary>Transforma un inbound (STOP/START/HELP) al modelo canónico.</summary>
    Result<SmsInboundMessage> ParseInbound(string rawPayload);
}

/// <summary>Factory único de resolución de adapters. El Application nunca hace switch(provider).</summary>
public interface ISmsAdapterFactory
{
    /// <summary>Resuelve el adapter por su código. Un código no registrado es misconfiguración del host.</summary>
    ISmsProvider Resolve(string code);
}

/// <summary>Marca una clase <see cref="ISmsProvider"/> con su código para el registro reflexivo (keyed DI).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SmsProviderAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}
