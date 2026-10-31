using System.ComponentModel.DataAnnotations;

namespace TaxVision.ContentScanner;

/// <summary>
/// Configuración técnica del scanner (modelo, ffmpeg, concurrencia y topes). Los umbrales de decisión NO viven
/// aquí: el scanner solo puntúa y CloudStorage decide con su política guardada en BD.
/// </summary>
public sealed class ContentScannerOptions
{
    public const string SectionName = "ContentScanner";

    [Required]
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Lado de la imagen que espera el modelo (cuadrada).</summary>
    [Range(32, 1024)]
    public int InputSize { get; set; }

    /// <summary>Normalización por canal RGB del preprocesador del modelo.</summary>
    [MinLength(3), MaxLength(3)]
    public float[] Mean { get; set; } = [];

    [MinLength(3), MaxLength(3)]
    public float[] Std { get; set; } = [];

    /// <summary>Índice de la clase "nsfw" en la salida del modelo (su `config.json`).</summary>
    [Range(0, 64)]
    public int NsfwLabelIndex { get; set; }

    /// <summary>Clave compartida con CloudStorage (`X-Scanner-Key`); defensa extra dentro de la red interna.</summary>
    [Required, MinLength(16)]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>ffprobe: dice si un video trae pista de imagen (una nota de voz `.webm` no la trae).</summary>
    [Required]
    public string FfprobePath { get; set; } = string.Empty;

    [Range(5, 600)]
    public int FfmpegTimeoutSeconds { get; set; }

    [Range(1, 16)]
    public int MaxConcurrentScans { get; set; }

    /// <summary>Espera máxima por un turno de escaneo; pasado esto responde 503 y CloudStorage reintenta.</summary>
    [Range(1, 300)]
    public int QueueTimeoutSeconds { get; set; }

    [Range(1, 16)]
    public int InferenceThreads { get; set; }

    [Range(1024, 4L * 1024 * 1024 * 1024)]
    public long MaxUploadBytes { get; set; }

    /// <summary>Topes técnicos del muestreo de video (CloudStorage pide dentro de estos límites).</summary>
    [Range(1, 600)]
    public int MinFrameIntervalSeconds { get; set; }

    [Range(1, 600)]
    public int MaxFrames { get; set; }

    [Required]
    public string TempDirectory { get; set; } = string.Empty;
}
