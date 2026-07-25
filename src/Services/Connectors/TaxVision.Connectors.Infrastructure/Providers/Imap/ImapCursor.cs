namespace TaxVision.Connectors.Infrastructure.Providers.Imap;

/// <summary>
/// Cursor de sync IMAP — UidValidity + LiveLastUid (+ ventana de backfill opcional). Codificado
/// como un solo string opaco para calzar con la firma genérica de IEmailProviderClient (mismo
/// tratamiento que Gmail/Graph le dan a su propio cursor). Si UidValidity cambió desde el cursor
/// guardado, el servidor reindexó el mailbox y los UIDs viejos ya no son comparables — hay que
/// resetear a full sync.
///
/// Dos carriles para que un backlog histórico grande nunca bloquee la detección de correo nuevo:
/// LiveLastUid salta directo al UID más alto apenas se detecta un gap grande, y lo que quedó
/// atrás se procesa como BackfillLastUid/BackfillCeiling — ventana de menor prioridad que solo
/// consume el presupuesto de MaxMessagesPerSync que el carril live no usó ese pase. Formato:
/// "{uidValidity}:{liveLastUid}:{backfillLastUid|-}:{backfillCeiling|-}". Cursors persistidos con
/// el formato viejo de 2 partes ("{uidValidity}:{lastUid}") siguen siendo válidos — se interpretan
/// como LiveLastUid sin backfill pendiente.
/// </summary>
public readonly record struct ImapCursor(
    uint UidValidity,
    uint LiveLastUid,
    uint? BackfillLastUid,
    uint? BackfillCeiling
)
{
    public override string ToString() =>
        BackfillLastUid is null && BackfillCeiling is null
            ? $"{UidValidity}:{LiveLastUid}"
            : $"{UidValidity}:{LiveLastUid}:{BackfillLastUid?.ToString() ?? "-"}:{BackfillCeiling?.ToString() ?? "-"}";

    public static ImapCursor? Parse(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        var parts = cursor.Split(':');

        // Formato legacy de 2 partes (pre-backfill-window): sin ventana de backfill.
        if (parts.Length == 2)
            return uint.TryParse(parts[0], out var legacyValidity) && uint.TryParse(parts[1], out var legacyLastUid)
                ? new ImapCursor(legacyValidity, legacyLastUid, null, null)
                : null;

        if (
            parts.Length != 4
            || !uint.TryParse(parts[0], out var uidValidity)
            || !uint.TryParse(parts[1], out var liveLastUid)
        )
            return null;

        uint? backfillLastUid = parts[2] != "-" && uint.TryParse(parts[2], out var bl) ? bl : null;
        uint? backfillCeiling = parts[3] != "-" && uint.TryParse(parts[3], out var bc) ? bc : null;

        return new ImapCursor(uidValidity, liveLastUid, backfillLastUid, backfillCeiling);
    }
}
