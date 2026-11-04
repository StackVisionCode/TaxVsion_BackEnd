namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Filtros comunes de los listados de correo (Sent, Drafts, Trash), aplicados en el servidor para que el total y
/// las páginas no mientan. <see cref="Search"/> null = sin texto; <see cref="HasAttachmentsOnly"/> = solo con
/// adjuntos; <see cref="FromUtc"/>/<see cref="ToUtc"/> = rango inclusivo sobre la fecha del correo (envío, último
/// guardado o recepción según la carpeta), null = abierto.
/// </summary>
public sealed record MailListFilter(
    string? Search = null,
    bool HasAttachmentsOnly = false,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null
)
{
    public static readonly MailListFilter None = new();

    public bool InRange(DateTime value) => (FromUtc is null || value >= FromUtc) && (ToUtc is null || value <= ToUtc);
}
