namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Ciclo de vida de una <see cref="SignatureRequest"/>. Transiciones válidas:
/// <list type="bullet">
///   <item><c>Draft → Scheduled</c> (ScheduleSend, F3): el preparador programa el envío a futuro.</item>
///   <item><c>Scheduled → Draft</c> (CancelSchedule, F3): el preparador cancela la programación.</item>
///   <item><c>Scheduled → InProgress</c> (job de polling, F3): llega la hora y se envía.</item>
///   <item><c>Draft → InProgress</c> al enviar (requiere hash + firmantes + campos).</item>
///   <item><c>InProgress → Completed</c> cuando todos los firmantes firman y el sealed está listo.</item>
///   <item><c>InProgress → Rejected</c> cuando cualquier firmante rechaza.</item>
///   <item><c>* → Canceled</c> (desde no-terminal) por acción del staff.</item>
///   <item><c>* → Expired</c> (desde no-terminal) cuando pasa el vencimiento.</item>
/// </list>
/// Terminales: <c>Completed</c>, <c>Rejected</c>, <c>Canceled</c>, <c>Expired</c>.
///
/// <para><see cref="Ready"/> queda obsoleto a partir de F2: ya no se emite nunca, pero se conserva
/// para filas históricas migradas antes del cambio.</para>
/// </summary>
public enum SignatureRequestStatus
{
    Draft,

    /// <summary>Obsoleto desde F2 — conservado solo para filas históricas. El pipeline no lo emite.</summary>
    [System.Obsolete("No se emite desde F2. La disponibilidad del documento es un flag derivado, no un estado.")]
    Ready,
    InProgress,
    Completed,
    Rejected,
    Canceled,
    Expired,

    /// <summary>F3 — Programada: envío pendiente a <c>ScheduledSendAtUtc</c>. Un job polling la mueve a InProgress.</summary>
    Scheduled,
}
