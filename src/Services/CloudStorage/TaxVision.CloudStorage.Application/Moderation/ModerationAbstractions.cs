using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Application.Moderation;

/// <summary>La fila única de la política de moderación (tracked, para editarla) y su historial.</summary>
public interface IContentModerationPolicyRepository
{
    Task<ContentModerationPolicy?> GetAsync(CancellationToken ct);
}

public interface IContentModerationPolicyAuditRepository
{
    void Add(ContentModerationPolicyAudit entry);
    Task<IReadOnlyList<ContentModerationPolicyAudit>> ListAsync(int skip, int take, CancellationToken ct);
}

/// <summary>Cola de revisión de PlatformAdmin: cross-tenant por diseño (la revisión es de la plataforma).</summary>
public interface IContentReviewQueue
{
    Task<IReadOnlyList<FileObject>> ListAsync(FileStatus status, int skip, int take, CancellationToken ct);
}

/// <summary>Muestreo de video que se le pide al scanner (sale de la política, no de código).</summary>
public sealed record ContentSampling(int FrameIntervalSeconds, int MaxFrames);

/// <summary>Puntuación sexual 0..1 (peor cuadro en video) que devuelve el scanner; la decisión es de CloudStorage.</summary>
public sealed record ContentClassification(double SexualScore, int Frames);

/// <summary>
/// Puerto hacia el servicio TaxVision.ContentScanner. Solo clasifica; los umbrales viven en
/// <see cref="ContentModerationPolicy"/>. <see cref="IsConfigured"/> false = sin URL configurada.
/// </summary>
public interface IContentClassifier
{
    bool IsConfigured { get; }

    /// <exception cref="ContentClassifierUnavailableException">Caído, ocupado o sin respuesta tras los reintentos.</exception>
    /// <exception cref="ContentNotDecodableException">El scanner no pudo decodificar el archivo.</exception>
    Task<ContentClassification> ClassifyAsync(
        Stream content,
        string contentType,
        ContentSampling sampling,
        CancellationToken ct
    );
}

public sealed class ContentClassifierUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class ContentNotDecodableException(string message) : Exception(message);

/// <summary>Caché en proceso de la política (60 s); se invalida al editarla desde este mismo nodo.</summary>
public sealed class ContentModerationPolicyCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private (ContentModerationSettings? Settings, DateTime ExpiresAtUtc) _entry;

    public bool TryGet(DateTime nowUtc, out ContentModerationSettings? settings)
    {
        lock (_gate)
        {
            settings = _entry.Settings;
            return _entry.ExpiresAtUtc > nowUtc;
        }
    }

    public void Set(ContentModerationSettings? settings, DateTime nowUtc)
    {
        lock (_gate)
            _entry = (settings, nowUtc + TimeToLive);
    }

    public void Invalidate()
    {
        lock (_gate)
            _entry = default;
    }
}
