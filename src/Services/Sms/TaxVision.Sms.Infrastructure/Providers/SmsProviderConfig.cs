namespace TaxVision.Sms.Infrastructure.Providers;

/// <summary>Config por proveedor bajo `Sms:Providers:{code}`. Todo dirigido por configuración: agregar
/// un proveedor REST estándar no requiere código nuevo (salvo casos raros que traduce su propio adapter).</summary>
public sealed class SmsProvidersOptions
{
    /// <summary>Se bindea desde la sección `Sms`; el dict vive en `Sms:Providers:{code}`.</summary>
    public const string SectionName = "Sms";

    public Dictionary<string, SmsProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SmsProviderConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string SendPath { get; set; } = "/";
    public string HttpMethod { get; set; } = "POST";

    /// <summary>json | form</summary>
    public string BodyFormat { get; set; } = "json";
    public string? SenderId { get; set; }

    public SmsAuthConfig Auth { get; set; } = new();
    public SmsRequestMap RequestMap { get; set; } = new();
    public SmsResponseMap ResponseMap { get; set; } = new();
    public SmsWebhookConfig Webhook { get; set; } = new();
    public SmsCapabilitiesConfig Capabilities { get; set; } = new();
}

public sealed class SmsAuthConfig
{
    /// <summary>none | basic | bearer | apiKeyHeader</summary>
    public string Type { get; set; } = "none";
    public string? HeaderName { get; set; }
    public string? Credential { get; set; }
}

public sealed class SmsRequestMap
{
    public string To { get; set; } = "to";
    public string From { get; set; } = "from";
    public string Body { get; set; } = "body";
    public string Media { get; set; } = "media";
}

public sealed class SmsResponseMap
{
    /// <summary>Nombre del campo (top-level) en la respuesta con el id del mensaje del proveedor.</summary>
    public string ProviderMessageIdPath { get; set; } = "id";
}

public sealed class SmsWebhookConfig
{
    public string? Secret { get; set; }
    public string SignatureHeader { get; set; } = "X-Signature";
    public string HmacAlgo { get; set; } = "HMACSHA256";
    public string ProviderMessageIdPath { get; set; } = "messageId";
    public string StatusPath { get; set; } = "status";
    public string? ErrorCodePath { get; set; }
    public string EventTypePath { get; set; } = "eventType";
    public string FromPath { get; set; } = "from";
    public string KeywordPath { get; set; } = "text";

    /// <summary>Mapea el valor de estado del proveedor → canónico (accepted/delivered/failed/undeliverable).</summary>
    public Dictionary<string, string> StatusMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SmsCapabilitiesConfig
{
    public bool SupportsDeliveryReceipts { get; set; } = true;
    public bool SupportsInbound { get; set; } = true;
    public bool SupportsBulkSend { get; set; }
    public int MaxBatchSize { get; set; } = 1;
    public bool SupportsMedia { get; set; }
    public bool SupportsMultipleMedia { get; set; }
    public int MaxMediaItems { get; set; } = 1;
    public long MaxMediaSizeBytes { get; set; }
    public List<string> AllowedMediaTypes { get; set; } = [];
}
