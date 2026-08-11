using BuildingBlocks.Domain;

namespace TaxVision.Sms.Domain.Messages;

/// <summary>Referencia de media (MMS) de un envío, `0..N` por <see cref="SmsMessage"/>. Entidad hija:
/// NO guarda binarios — solo la URL/referencia y su metadata como snapshot del intento.</summary>
public sealed class SmsMedia : BaseEntity
{
    public Guid SmsMessageId { get; private set; }
    public string Url { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public string? FileName { get; private set; }
    public long? SizeBytes { get; private set; }

    /// <summary>Id que el proveedor asigna a la media subida (si aplica). Se completa post-envío.</summary>
    public string? ProviderMediaId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private SmsMedia() { }

    internal SmsMedia(Guid smsMessageId, string url, string contentType, string? fileName, long? sizeBytes, DateTime nowUtc)
    {
        SmsMessageId = smsMessageId;
        Url = url;
        ContentType = contentType;
        FileName = fileName;
        SizeBytes = sizeBytes;
        CreatedAtUtc = nowUtc;
    }

    internal void SetProviderMediaId(string providerMediaId) => ProviderMediaId = providerMediaId;
}
