namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Modela el resultado de una única llamada HTTP síncrona y bloqueante a Postmaster
/// (<c>POST /postmaster/correspondence-messages</c>, Fase 14, plan §0/§14) — no un workflow
/// asíncrono. <see cref="Sending"/> dura exactamente lo que tarda esa request: nunca queda
/// "colgado", siempre resuelve a <see cref="Sent"/> o <see cref="Failed"/>.
/// </summary>
public enum DraftStatus
{
    Draft = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Discarded = 4,
}
