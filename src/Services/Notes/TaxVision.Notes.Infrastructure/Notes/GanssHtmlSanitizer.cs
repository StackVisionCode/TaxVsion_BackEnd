using TaxVision.Notes.Application.Notes.Abstractions;
using GanssSanitizer = Ganss.Xss.HtmlSanitizer;

namespace TaxVision.Notes.Infrastructure.Notes;

/// <summary>
/// Fase 5 — implementación real de <see cref="IHtmlSanitizer"/> vía <c>Ganss.Xss.HtmlSanitizer</c>
/// (paquete NuGet <c>HtmlSanitizer</c>). Un solo <c>HtmlSanitizer</c> reusado entre llamadas — la
/// librería documenta explícitamente que la instancia es thread-safe y cara de reconstruir por
/// llamada (compila su whitelist interna de tags/atributos en el constructor). Alias porque
/// <c>Ganss.Xss</c> también define su propia interfaz <c>IHtmlSanitizer</c>, ambigua con la
/// nuestra (<see cref="IHtmlSanitizer"/>) si se importa el namespace completo.
/// </summary>
public sealed class GanssHtmlSanitizer : IHtmlSanitizer
{
    private readonly GanssSanitizer _sanitizer = new();

    public string Sanitize(string rawHtml) => _sanitizer.Sanitize(rawHtml ?? string.Empty);
}
