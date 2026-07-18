namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Taxonomía normalizada de fallos de envío (D3 §8) — <see cref="Gmail"/>/<see cref="Graph"/> mapean sus
/// propios códigos de error a esta forma común, así el caller (<c>SendMessageHandler</c>) nunca necesita
/// saber de reason strings/status codes específicos de un proveedor.
/// </summary>
public enum SendFailureReason
{
    /// <summary>401 en ambos proveedores — dispara refresh en Connectors, no un retry del send.</summary>
    AuthExpired,

    /// <summary>Gmail 403 daily/user rate limit, Graph 429 — reintentable respetando Retry-After.</summary>
    QuotaExceeded,

    /// <summary>Gmail 403 domainPolicy, Graph 403 ErrorSendAsDenied — no reintentable.</summary>
    PermissionDenied,

    /// <summary>5xx de cualquiera de los dos — reintentable vía el mismo retry Polly de Fase 10.</summary>
    TransientProviderError,

    /// <summary>400 de cualquiera de los dos — no reintentable.</summary>
    InvalidRequest,

    /// <summary>Solo Graph v1 (D3 Compose §9/§11.2) — total de adjuntos excede el límite de <c>sendMail</c>/<c>reply</c> (3MB). No reintentable; requiere migrar a upload session (fuera de alcance v1).</summary>
    AttachmentTooLarge,
}
