using System.Globalization;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// P-18 — SourceId de la ExternalReference que Signature devuelve en sus hechos:
/// "{requestId:N}:r{revision}". Tasks lo construye al pedir la firma y lo parsea al correlacionar
/// los resultados (sin depender del Id interno de Signature).
/// </summary>
public static class ClientFormSignatureReference
{
    public const string SourceService = "tasks.client_forms";
    public const string SourceType = "ClientFormRevision";

    public static string BuildSourceId(Guid requestId, int revisionNumber) =>
        $"{requestId:N}:r{revisionNumber.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? sourceId, out Guid requestId, out int revisionNumber)
    {
        requestId = Guid.Empty;
        revisionNumber = 0;
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;
        var marker = sourceId.IndexOf(":r", StringComparison.Ordinal);
        if (marker <= 0)
            return false;
        var idPart = sourceId[..marker];
        var revisionPart = sourceId[(marker + 2)..];
        return Guid.TryParseExact(idPart, "N", out requestId)
            && int.TryParse(revisionPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out revisionNumber);
    }
}
