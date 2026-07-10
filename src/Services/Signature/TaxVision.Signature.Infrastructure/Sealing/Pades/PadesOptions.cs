namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Parametros del pipeline PAdES-B ByteRange. Todos tienen defaults sanos; solo se
/// deberian tocar para pruebas de compatibilidad con validadores concretos.
/// </summary>
public sealed class PadesOptions
{
    public const string SectionName = "Signature:Sealing:Pades";

    /// <summary>Bytes reservados dentro de <c>/Contents</c> para el CMS DER (escritos en hex).</summary>
    public int ContentsReservedBytes { get; set; } = 16 * 1024;

    /// <summary>Filter estandar registrado en el catalog de firmas de PDF.</summary>
    public string Filter { get; set; } = "Adobe.PPKLite";

    /// <summary>SubFilter para PAdES-B (RFC 3161/PAdES-B/B-T comparten este).</summary>
    public string SubFilter { get; set; } = "ETSI.CAdES.detached";

    /// <summary>Reason opcional escrito en el signature dictionary.</summary>
    public string? Reason { get; set; }

    /// <summary>Location opcional escrita en el signature dictionary.</summary>
    public string? Location { get; set; }

    /// <summary>ContactInfo opcional escrito en el signature dictionary.</summary>
    public string? ContactInfo { get; set; }
}
