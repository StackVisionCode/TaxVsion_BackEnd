namespace TaxVision.Notification.Application.Common;

/// <summary>
/// <c>SourceContext</c> de los SMS de Notification: el id de su fila de auditoría. Sms lo devuelve tal cual en sus
/// resultados y así se marca la fila. Lo que no empieza por el prefijo es de otro emisor (campañas, CRM).
/// </summary>
public static class SmsSourceContext
{
    private const string LogPrefix = "notification:log:";

    public static string ForLog(Guid logId) => $"{LogPrefix}{logId:N}";

    public static bool TryGetLogId(string? sourceContext, out Guid logId)
    {
        logId = Guid.Empty;
        return sourceContext is not null
            && sourceContext.StartsWith(LogPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(sourceContext[LogPrefix.Length..], "N", out logId);
    }
}
