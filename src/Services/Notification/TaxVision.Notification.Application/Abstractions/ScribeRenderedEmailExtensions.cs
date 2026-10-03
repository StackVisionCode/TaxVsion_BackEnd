namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Único punto de conversión entre lo que devuelve Scribe y lo que se le pide al gateway. Hermana de
/// <see cref="ScribeRenderResultExtensions"/> y por el mismo motivo: que el arreglo sea estructural.
///
/// <para>Antes cada consumer copiaba campo por campo del render al <see cref="EmailDispatchRequest"/>,
/// así que olvidarse de uno no se notaba — el correo salía igual, solo que mal. Pasó con
/// <c>InlineAssets</c>: ningún logo llegó nunca más allá del deserializador y nadie se entero porque
/// compilaba. El día que el render gane otro campo, se agrega acá y lo heredan los veintitantos
/// consumers; olvidarse deja de ser posible porque ya no hay nada que copiar.</para>
/// </summary>
public static class ScribeRenderedEmailExtensions
{
    /// <summary>
    /// Arma el pedido de envío con lo que decide Scribe (asunto, cuerpos, logos inline y carril) más
    /// el destino, que decide el consumer. Lo opcional —adjuntos, Cc/Bcc, idempotencia— se agrega con
    /// <c>with</c>, para que siga siendo explícito y visible en el call site.
    /// </summary>
    public static EmailDispatchRequest ToDispatchRequest(
        this ScribeRenderedEmail render,
        Guid tenantId,
        string to,
        string templateKey,
        Guid? relatedEventId,
        string? correlationId
    ) =>
        new(
            TenantId: tenantId,
            To: to,
            Subject: render.Subject,
            HtmlBody: render.Html,
            TextBody: render.Text ?? string.Empty,
            TemplateKey: templateKey,
            RelatedEventId: relatedEventId,
            CorrelationId: correlationId,
            InlineAssets: render.InlineAssets,
            Scope: render.DispatchScope
        );
}
