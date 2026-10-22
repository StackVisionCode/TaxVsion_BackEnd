namespace TaxVision.Connectors.Application.Providers;

/// <summary>Resultado de pedir la metadata de un mensaje dentro de un lote: o el mensaje o el error.</summary>
public sealed record ProviderMessageFetch(string ProviderMessageId, RawMessage? Message, EmailProviderException? Error)
{
    public static ProviderMessageFetch Found(string providerMessageId, RawMessage message) =>
        new(providerMessageId, message, null);

    public static ProviderMessageFetch Failed(string providerMessageId, EmailProviderException error) =>
        new(providerMessageId, null, error);
}
