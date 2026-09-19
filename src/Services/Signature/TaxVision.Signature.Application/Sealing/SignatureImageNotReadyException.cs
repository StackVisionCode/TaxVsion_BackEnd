namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Se lanza cuando el sellado se dispara pero el PNG de firma de algún firmante todavía no
/// terminó el scan de ClamAV en CloudStorage (proyección <c>FileMetadataRef</c> ausente o
/// <c>Pending</c>). Es una condición <b>transitoria</b> de consistencia eventual: la imagen se
/// sube segundos antes de que el último firmante complete, así que el <c>Completed</c> puede
/// adelantarse al <c>FileAvailable</c>.
///
/// <para>
/// Lanzarla (en vez de degradar en silencio al sello tipográfico o de fallar terminal) hace que
/// Wolverine <b>redelivere el mismo mensaje inbound</b> a este consumer con cooldown — sin
/// re-publicar el evento al exchange (no duplica notificaciones de otros servicios). Cuando el
/// scan marca el archivo <c>Available</c>, un reintento sella con la firma dibujada real; nunca
/// se produce un documento legal al que le falte, de forma inadvertida, la firma que el firmante
/// trazó. La política de reintentos vive en <c>Program.cs</c>.
/// </para>
/// </summary>
public sealed class SignatureImageNotReadyException : Exception
{
    public SignatureImageNotReadyException(string message)
        : base(message) { }
}
