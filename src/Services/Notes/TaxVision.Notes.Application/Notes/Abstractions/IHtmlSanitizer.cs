namespace TaxVision.Notes.Application.Notes.Abstractions;

/// <summary>
/// Puerto de sanitización HTML (Fase 5) — la implementación real (<c>Ganss.Xss.HtmlSanitizer</c>)
/// vive en Infrastructure, nunca en Domain (guardrail 4: dominio puro). Los handlers de
/// create/update SIEMPRE sanitizan aquí antes de llamar <c>NoteContent.Create</c> — ese VO asume
/// que el HTML que recibe ya es seguro (ver su doc-comment).
/// </summary>
public interface IHtmlSanitizer
{
    string Sanitize(string rawHtml);
}
