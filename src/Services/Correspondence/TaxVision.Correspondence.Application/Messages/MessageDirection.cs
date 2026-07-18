namespace TaxVision.Correspondence.Application.Messages;

/// <summary>Fase 15 — de qué lado del thread viene una <see cref="MessageSummary"/>: recibido por el tenant (<c>Inbound</c>, un <c>IncomingEmail</c>) o enviado por el tenant (<c>Outbound</c>, un <c>Draft</c> ya <c>Sent</c>).</summary>
public enum MessageDirection
{
    Inbound,
    Outbound,
}
