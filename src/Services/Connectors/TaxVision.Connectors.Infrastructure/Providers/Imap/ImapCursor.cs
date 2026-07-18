namespace TaxVision.Connectors.Infrastructure.Providers.Imap;

/// <summary>
/// Cursor de sync IMAP — UidValidity + LastUid (§16/§21 del plan). Codificado como un solo string
/// opaco ("{uidValidity}:{lastUid}") para calzar con la firma genérica de IEmailProviderClient
/// (mismo tratamiento que Gmail/Graph le dan a su propio cursor). Si UidValidity cambió desde el
/// cursor guardado, el servidor reindexó el mailbox y los UIDs viejos ya no son comparables —
/// hay que resetear a full sync.
/// </summary>
public readonly record struct ImapCursor(uint UidValidity, uint LastUid)
{
    public override string ToString() => $"{UidValidity}:{LastUid}";

    public static ImapCursor? Parse(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        var parts = cursor.Split(':', 2);
        if (
            parts.Length != 2
            || !uint.TryParse(parts[0], out var uidValidity)
            || !uint.TryParse(parts[1], out var lastUid)
        )
            return null;

        return new ImapCursor(uidValidity, lastUid);
    }
}
