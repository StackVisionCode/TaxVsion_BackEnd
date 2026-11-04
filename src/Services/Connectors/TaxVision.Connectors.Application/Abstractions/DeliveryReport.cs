namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>
/// Un aviso de no entrega leído: sus destinatarios más lo que se muestra sin bajar el MIME de nuevo.
/// </summary>
/// <param name="Subject">Asunto del aviso tal como llegó.</param>
/// <param name="From">Dirección del remitente del aviso (ej. mailer-daemon@googlemail.com).</param>
/// <param name="Summary">Texto legible del aviso para humanos. Nunca contenido del correo original.</param>
public sealed record DeliveryReport(
    IReadOnlyList<DeliveryReportEntry> Entries,
    string? Subject,
    string? From,
    string? Summary
)
{
    public static readonly DeliveryReport Empty = new([], null, null, null);
}
