using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Un render por idioma y lector (no por persona): quienes leen igual comparten el mismo correo. Las variables
/// pueden depender del idioma (textos del sistema dentro del correo).
/// </summary>
public sealed class RecipientRenders(
    IScribeRenderClient scribe,
    string eventKey,
    Guid tenantId,
    ScribeRenderAudience audience,
    Func<RecipientLocale, IReadOnlyDictionary<string, object?>> variables
)
{
    private readonly Dictionary<(string Language, ScribeReader Reader), ScribeRenderedEmail> renders = [];

    public RecipientRenders(
        IScribeRenderClient scribe,
        string eventKey,
        Guid tenantId,
        ScribeRenderAudience audience,
        IReadOnlyDictionary<string, object?> variables
    )
        : this(scribe, eventKey, tenantId, audience, _ => variables) { }

    public async Task<ScribeRenderedEmail> ForAsync(RecipientLocale locale, CancellationToken ct)
    {
        var key = (locale.TemplateLanguage, locale.Reader);
        if (!renders.TryGetValue(key, out var rendered))
            renders[key] = rendered = (
                await scribe.RenderForRecipientAsync(eventKey, tenantId, variables(locale), audience, locale, ct)
            ).EnsureRendered(eventKey);
        return rendered;
    }
}
